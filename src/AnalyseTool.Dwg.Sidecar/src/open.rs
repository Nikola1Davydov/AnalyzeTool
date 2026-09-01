//! Opening a drawing, and the two things every op needs afterwards: what its units are, and which
//! entities belong to the space the caller asked for.

use acadrust::document::CadDocument;
use acadrust::entities::EntityType;
use acadrust::types::Handle;
use acadrust::{DwgReadOptions, DwgReader, DxfReader};
use serde::Serialize;
use std::path::Path;

use crate::wire::codes;

/// A failure with the code the wire wants, so callers can `?` their way out.
pub struct OpenError {
    pub code: &'static str,
    pub message: String,
}

impl OpenError {
    fn new(code: &'static str, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
        }
    }
}

pub struct Opened {
    pub doc: CadDocument,
    /// "dwg" or "dxf" — what was actually parsed, not what the caller assumed.
    pub format: &'static str,
}

/// Opens a .dwg or .dxf by extension. The extension is the only signal used: sniffing the magic
/// bytes would let a mislabelled file through into a codec that cannot read it, and the error it
/// then raises is far less clear than "this is not a DWG".
pub fn open(path: &str, failsafe: bool) -> Result<Opened, OpenError> {
    let p = Path::new(path);
    if !p.is_file() {
        return Err(OpenError::new(codes::NOT_FOUND, format!("no such file: {path}")));
    }

    let extension = p
        .extension()
        .and_then(|e| e.to_str())
        .map(|e| e.to_ascii_lowercase())
        .unwrap_or_default();

    match extension.as_str() {
        "dwg" => {
            let options = DwgReadOptions { failsafe };
            let mut reader = DwgReader::from_file_with_options(p, options)
                .map_err(|e| OpenError::new(codes::READ_FAILED, format!("opening DWG failed: {e}")))?;
            let doc = reader
                .read()
                .map_err(|e| OpenError::new(codes::READ_FAILED, format!("reading DWG failed: {e}")))?;
            Ok(Opened { doc, format: "dwg" })
        }
        "dxf" => {
            let reader = DxfReader::from_file(p)
                .map_err(|e| OpenError::new(codes::READ_FAILED, format!("opening DXF failed: {e}")))?;
            let doc = reader
                .read()
                .map_err(|e| OpenError::new(codes::READ_FAILED, format!("reading DXF failed: {e}")))?;
            Ok(Opened { doc, format: "dxf" })
        }
        other => Err(OpenError::new(
            codes::UNSUPPORTED_FORMAT,
            format!("unsupported extension '{other}' — this reader handles .dwg and .dxf"),
        )),
    }
}

/// The drawing's INSUNITS, resolved to a name. This is the single most consequential number in the
/// file for a Revit import: the same polyline is a 10 m wall or a 10 mm gap depending on it, and
/// `unitless` (code 0, very common) means the caller MUST ask the user rather than guess.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Units {
    pub code: i16,
    pub name: &'static str,
}

pub fn units(doc: &CadDocument) -> Units {
    let code = doc.header.insertion_units;
    Units {
        code,
        name: unit_name(code),
    }
}

/// INSUNITS values per the DXF reference. Anything outside the table is reported as `unknown`
/// rather than mapped to a plausible neighbour.
fn unit_name(code: i16) -> &'static str {
    match code {
        0 => "unitless",
        1 => "inches",
        2 => "feet",
        3 => "miles",
        4 => "millimeters",
        5 => "centimeters",
        6 => "meters",
        7 => "kilometers",
        8 => "microinches",
        9 => "mils",
        10 => "yards",
        11 => "angstroms",
        12 => "nanometers",
        13 => "microns",
        14 => "decimeters",
        15 => "decameters",
        16 => "hectometers",
        17 => "gigameters",
        18 => "astronomical units",
        19 => "light years",
        20 => "parsecs",
        21 => "US survey feet",
        22 => "US survey inches",
        23 => "US survey yards",
        24 => "US survey miles",
        _ => "unknown",
    }
}

/// Which part of the drawing an op looks at.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Space {
    Model,
    Paper,
    All,
}

impl Space {
    pub fn parse(value: Option<&str>) -> Result<Space, OpenError> {
        match value.unwrap_or("model").to_ascii_lowercase().as_str() {
            "model" => Ok(Space::Model),
            "paper" => Ok(Space::Paper),
            "all" => Ok(Space::All),
            other => Err(OpenError::new(
                codes::BAD_REQUEST,
                format!("unknown space '{other}' — use model, paper or all"),
            )),
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Space::Model => "model",
            Space::Paper => "paper",
            Space::All => "all",
        }
    }
}

/// The entities of one space, plus anything the caller should know about how they were found.
pub struct Selection<'a> {
    pub entities: Vec<&'a EntityType>,
    pub warnings: Vec<String>,
}

/// Scopes entities to a space.
///
/// `doc.entities()` is every entity in the file — model space, every paper-space layout AND the
/// contents of every block definition. Reporting that as "what is in the drawing" would count a
/// door block's 40 lines once per definition and inflate a layer's entity count by the block
/// contents nobody placed. So model space is resolved through its block record.
///
/// Three ways of finding it, in order, because DWG files in the wild do not all populate the same
/// fields: the record's own entity list, then the owner handle on each entity, then — rather than
/// answering "0 entities" for a file that plainly has some — everything, with a warning saying so.
pub fn select<'a>(doc: &'a CadDocument, space: Space) -> Selection<'a> {
    if space == Space::All {
        return Selection {
            entities: doc.entities().collect(),
            warnings: Vec::new(),
        };
    }

    let (handle, fallback_names): (Handle, &[&str]) = match space {
        Space::Model => (
            doc.header.model_space_block_handle,
            &["*Model_Space", "$MODEL_SPACE"],
        ),
        Space::Paper => (
            doc.header.paper_space_block_handle,
            &["*Paper_Space", "$PAPER_SPACE"],
        ),
        Space::All => unreachable!("handled above"),
    };

    let record = doc
        .block_records
        .iter()
        .find(|r| !handle.is_null() && r.handle == handle)
        .or_else(|| {
            doc.block_records
                .iter()
                .find(|r| fallback_names.iter().any(|n| r.name.eq_ignore_ascii_case(n)))
        });

    let Some(record) = record else {
        return Selection {
            entities: doc.entities().collect(),
            warnings: vec![format!(
                "no {} space block record in this file — every entity is reported instead, so counts \
                 may include block-definition contents",
                space.as_str()
            )],
        };
    };

    // 1. The record's own list of what it owns.
    let listed: Vec<&EntityType> = record
        .entity_handles
        .iter()
        .filter_map(|h| doc.get_entity(*h))
        .filter(|e| !matches!(e, EntityType::Block(_) | EntityType::BlockEnd(_)))
        .collect();
    if !listed.is_empty() {
        return Selection {
            entities: listed,
            warnings: Vec::new(),
        };
    }

    // 2. The owner handle each entity carries.
    let owned: Vec<&EntityType> = doc
        .entities()
        .filter(|e| e.common().owner_handle == record.handle)
        .collect();
    if !owned.is_empty() {
        return Selection {
            entities: owned,
            warnings: Vec::new(),
        };
    }

    // 3. Neither worked. An empty space is a legitimate answer, so only say something when the
    //    file demonstrably has entities that we simply could not attribute.
    let all: Vec<&EntityType> = doc.entities().collect();
    if all.is_empty() {
        return Selection {
            entities: all,
            warnings: Vec::new(),
        };
    }

    Selection {
        entities: all,
        warnings: vec![format!(
            "{} space could not be resolved (the block record is empty and no entity names it as \
             owner) — every entity is reported instead, so counts may include block-definition contents",
            space.as_str()
        )],
    }
}
