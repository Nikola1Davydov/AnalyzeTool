//! `op: "structure"` — what is in this file, without converting anything.
//!
//! This is the answer to the question a Revit user actually has in front of a 40 MB survey DWG:
//! which layers carry something, how much, and of what. It is cheap enough to run before deciding
//! whether to import at all, which is the entire point of reading the file outside Revit.

use acadrust::document::CadDocument;
use acadrust::entities::EntityType;
use acadrust::types::{BoundingBox3D, Color, LineWeight, Vector3};
use serde::Serialize;
use std::collections::BTreeMap;

use crate::open::{self, Space, Units};

/// Parse diagnostics are capped: a failsafe read of a badly damaged file can produce thousands of
/// identical lines, and a response that large helps nobody. The full count is reported alongside.
const MAX_NOTIFICATIONS: usize = 100;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Structure {
    pub path: String,
    pub format: &'static str,
    /// The DXF version code, e.g. "AC1032".
    pub version: String,
    pub space: &'static str,
    pub units: Units,
    /// Entities in the selected space. NOT the file's total — block definitions are excluded.
    pub entity_count: usize,
    /// DXF type name -> count, e.g. {"LINE": 12043, "LWPOLYLINE": 87}.
    pub by_type: BTreeMap<String, usize>,
    pub layers: Vec<LayerInfo>,
    pub blocks: Vec<BlockInfo>,
    /// Bounding box of the selected space, in drawing units. None when nothing has finite extents.
    pub extents: Option<Extents>,
    pub notifications: Vec<NotificationInfo>,
    pub notification_count: usize,
    pub warnings: Vec<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LayerInfo {
    pub name: String,
    pub color: ColorInfo,
    pub line_type: String,
    /// Layer line weight in millimetres, when it is a concrete value rather than Default/ByBlock.
    pub line_weight_mm: Option<f64>,
    pub off: bool,
    pub frozen: bool,
    pub locked: bool,
    pub plottable: bool,
    /// True when the layer came in through an xref (its name contains `|`).
    pub xref_dependent: bool,
    /// Entities on this layer in the selected space. Zero means the layer is defined but unused —
    /// worth knowing, since those are exactly the ones an import should not create anything for.
    pub entity_count: usize,
    pub by_type: BTreeMap<String, usize>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BlockInfo {
    pub name: String,
    /// Entities in the block DEFINITION.
    pub entity_count: usize,
    /// INSERTs of this block in the selected space. A definition with 0 inserts is dead weight.
    pub insert_count: usize,
    pub is_xref: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ColorInfo {
    /// ACI index (1-255), when the colour is indexed.
    pub index: Option<u16>,
    /// "#RRGGBB", when the colour is a true colour.
    pub rgb: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Extents {
    pub min: [f64; 3],
    pub max: [f64; 3],
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NotificationInfo {
    pub kind: String,
    pub message: String,
}

pub fn build(path: &str, format: &'static str, doc: &CadDocument, space: Space) -> Structure {
    let selection = open::select(doc, space);
    let mut warnings = selection.warnings;

    let mut by_type: BTreeMap<String, usize> = BTreeMap::new();
    let mut per_layer: BTreeMap<String, BTreeMap<String, usize>> = BTreeMap::new();
    let mut inserts_per_block: BTreeMap<String, usize> = BTreeMap::new();
    let mut bounds: Option<BoundingBox3D> = None;

    for entity in &selection.entities {
        let type_name = entity.as_entity().entity_type().to_string();
        *by_type.entry(type_name.clone()).or_insert(0) += 1;
        *per_layer
            .entry(entity.common().layer.clone())
            .or_default()
            .entry(type_name)
            .or_insert(0) += 1;

        if let EntityType::Insert(insert) = entity {
            *inserts_per_block.entry(insert.block_name.clone()).or_insert(0) += 1;
        }

        accumulate(&mut bounds, entity);
    }

    // Every layer in the table, used or not — the unused ones are half of what makes a foreign DWG
    // unreadable, and a caller cannot ask about a layer this response never mentioned.
    let mut layers: Vec<LayerInfo> = doc
        .layers
        .iter()
        .map(|layer| {
            let counts = per_layer.get(&layer.name).cloned().unwrap_or_default();
            LayerInfo {
                name: layer.name.clone(),
                color: color_info(&layer.color),
                line_type: layer.line_type.clone(),
                line_weight_mm: line_weight_mm(&layer.line_weight),
                off: layer.flags.off,
                frozen: layer.flags.frozen,
                locked: layer.flags.locked,
                plottable: layer.is_plottable,
                xref_dependent: layer.flags.xref_dependent,
                entity_count: counts.values().sum(),
                by_type: counts,
            }
        })
        .collect();

    // Entities can name a layer the table does not define (a truncated file, an xref that did not
    // resolve). Revit would silently drop them; report them instead, as layers that exist in fact.
    let orphans: Vec<String> = per_layer
        .keys()
        .filter(|name| !doc.layers.contains(name))
        .cloned()
        .collect();
    if !orphans.is_empty() {
        warnings.push(format!(
            "{} layer name(s) used by entities are missing from the layer table: {}",
            orphans.len(),
            preview(&orphans)
        ));
        for name in orphans {
            let counts = per_layer.get(&name).cloned().unwrap_or_default();
            layers.push(LayerInfo {
                name,
                color: ColorInfo {
                    index: None,
                    rgb: None,
                },
                line_type: String::new(),
                line_weight_mm: None,
                off: false,
                frozen: false,
                locked: false,
                plottable: true,
                xref_dependent: false,
                entity_count: counts.values().sum(),
                by_type: counts,
            });
        }
    }

    layers.sort_by(|a, b| b.entity_count.cmp(&a.entity_count).then(a.name.cmp(&b.name)));

    let mut blocks: Vec<BlockInfo> = doc
        .block_records
        .iter()
        // The two layout records are not blocks anyone inserts; listing them as such is noise.
        .filter(|record| !record.name.starts_with('*') && !record.name.starts_with('$'))
        .map(|record| BlockInfo {
            name: record.name.clone(),
            entity_count: record.entity_handles.len(),
            insert_count: inserts_per_block.get(&record.name).copied().unwrap_or(0),
            is_xref: !record.xref_path.is_empty(),
        })
        .collect();
    blocks.sort_by(|a, b| b.insert_count.cmp(&a.insert_count).then(a.name.cmp(&b.name)));

    let notifications: Vec<NotificationInfo> = doc
        .notifications
        .iter()
        .take(MAX_NOTIFICATIONS)
        .map(|n| NotificationInfo {
            kind: format!("{}", n.notification_type),
            message: n.message.clone(),
        })
        .collect();

    Structure {
        path: path.to_string(),
        format,
        version: doc.version.as_str().to_string(),
        space: space.as_str(),
        units: open::units(doc),
        entity_count: selection.entities.len(),
        by_type,
        layers,
        blocks,
        extents: bounds.map(to_extents),
        notifications,
        notification_count: doc.notifications.len(),
        warnings,
    }
}

/// Grows the running bounding box, skipping entities whose box is not finite. Ray and XLine are
/// infinite by definition, and one NaN from a malformed entity would poison the whole extent.
pub fn accumulate(bounds: &mut Option<BoundingBox3D>, entity: &EntityType) {
    let box3d = entity.as_entity().bounding_box();
    if !finite(box3d.min) || !finite(box3d.max) {
        return;
    }
    *bounds = Some(match bounds.take() {
        Some(current) => current.merge(&box3d),
        None => box3d,
    });
}

pub fn to_extents(b: BoundingBox3D) -> Extents {
    Extents {
        min: xyz(b.min),
        max: xyz(b.max),
    }
}

fn finite(v: Vector3) -> bool {
    v.x.is_finite() && v.y.is_finite() && v.z.is_finite()
}

fn xyz(v: Vector3) -> [f64; 3] {
    [v.x, v.y, v.z]
}

pub fn color_info(color: &Color) -> ColorInfo {
    ColorInfo {
        index: color.index(),
        rgb: color.rgb().map(|(r, g, b)| format!("#{r:02X}{g:02X}{b:02X}")),
    }
}

fn line_weight_mm(weight: &LineWeight) -> Option<f64> {
    match weight {
        LineWeight::Value(_) => weight.millimeters(),
        _ => None,
    }
}

fn preview(names: &[String]) -> String {
    const SHOWN: usize = 5;
    if names.len() <= SHOWN {
        return names.join(", ");
    }
    format!("{}, … (+{} more)", names[..SHOWN].join(", "), names.len() - SHOWN)
}
