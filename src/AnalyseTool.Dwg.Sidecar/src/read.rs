//! `op: "read"` — the selected entities, flattened to the smallest shape a Revit importer needs.
//!
//! Deliberately NOT a serialization of `acadrust`'s model. That model is faithful to DXF, which
//! means dozens of fields per entity that a Revit conversion never looks at; sending them would
//! make a 100 000-entity read hundreds of megabytes of JSON. What crosses the pipe is points,
//! radii, angles and the handful of attributes that decide which Revit element gets created.
//!
//! Coordinates and lengths are in DRAWING units — the units `structure` reports. Converting to
//! Revit's internal feet is the C# side's job, because only it knows what the user chose when the
//! file says `unitless`.
//!
//! All angles are RADIANS (acadrust normalises DXF's degrees on read), which is also what the
//! Revit API takes, so nothing converts them on either side.

use acadrust::document::CadDocument;
use acadrust::entities::EntityType;
use acadrust::types::Vector3;
use serde::Serialize;
use std::collections::{BTreeMap, HashSet};

use crate::open::{self, Space, Units};
use crate::structure::{self, color_info, ColorInfo, Extents};

/// Default cap on returned entities. A whole survey drawing does not belong in one response, and a
/// caller that wants more can raise it up to `MAX_ENTITIES_CEILING`.
pub const DEFAULT_MAX_ENTITIES: usize = 20_000;
/// Hard ceiling, so a mistyped cap cannot ask for a multi-gigabyte line.
pub const MAX_ENTITIES_CEILING: usize = 200_000;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ReadResult {
    pub space: &'static str,
    pub units: Units,
    /// Entities matching the filters, before the cap.
    pub matched: usize,
    /// Entities actually returned.
    pub returned: usize,
    /// True when the cap cut the result short — the caller is looking at a prefix, not the answer.
    pub truncated: bool,
    pub entities: Vec<EntityDto>,
    /// Bounding box of the MATCHED entities — not only the returned ones — in drawing units. This is
    /// what a caller recentres by, in the same pass: survey drawings sit hundreds of kilometres from
    /// the origin, far enough out that Revit starts warning about accuracy.
    pub extents: Option<Extents>,
    /// DXF type -> count of entities that matched the filters but have no geometry mapping here.
    /// Named rather than dropped: "nothing came back" and "4 812 HATCHes were skipped" are
    /// different problems and the caller cannot otherwise tell them apart.
    pub skipped_by_type: BTreeMap<String, usize>,
    pub warnings: Vec<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EntityDto {
    /// DWG/DXF handle as hex, the file's own stable id for this entity.
    pub handle: String,
    pub layer: String,
    /// DXF type name, e.g. "LINE".
    pub r#type: String,
    pub color: ColorInfo,
    /// Empty means ByLayer.
    pub line_type: String,
    pub geometry: Geometry,
}

#[derive(Debug, Serialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum Geometry {
    Line {
        start: [f64; 3],
        end: [f64; 3],
    },
    Point {
        location: [f64; 3],
    },
    Circle {
        center: [f64; 3],
        radius: f64,
        normal: [f64; 3],
    },
    Arc {
        center: [f64; 3],
        radius: f64,
        start_angle: f64,
        end_angle: f64,
        normal: [f64; 3],
    },
    Ellipse {
        center: [f64; 3],
        major_axis: [f64; 3],
        minor_axis_ratio: f64,
        start_parameter: f64,
        end_parameter: f64,
        normal: [f64; 3],
    },
    /// A polyline as the file stores it: vertices plus per-vertex bulge. Bulge is tan(sweep/4) of
    /// the arc to the NEXT vertex, 0 for a straight segment — kept rather than pre-tessellated,
    /// because an arc segment turned into 32 short lines is precisely the mess that makes an
    /// exploded DWG unusable in Revit.
    Polyline {
        closed: bool,
        vertices: Vec<PolyVertex>,
    },
    Text {
        value: String,
        insertion: [f64; 3],
        height: f64,
        rotation: f64,
        width_factor: f64,
        style: String,
    },
    MText {
        value: String,
        insertion: [f64; 3],
        height: f64,
        rotation: f64,
        rectangle_width: f64,
        style: String,
    },
    Insert {
        block_name: String,
        insertion: [f64; 3],
        rotation: f64,
        scale: [f64; 3],
        normal: [f64; 3],
    },
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PolyVertex {
    pub point: [f64; 3],
    pub bulge: f64,
}

pub struct Filters {
    pub layers: Option<HashSet<String>>,
    pub types: Option<HashSet<String>>,
    pub max_entities: usize,
}

impl Filters {
    /// Layer and type filters are matched case-insensitively — DWG layer names round-trip with
    /// whatever case the author used, and a filter that misses "A-Wall" because the caller typed
    /// "A-WALL" is a bug report waiting to happen.
    pub fn new(layers: Option<Vec<String>>, types: Option<Vec<String>>, max_entities: Option<usize>) -> Self {
        Self {
            layers: normalize(layers),
            types: normalize(types),
            max_entities: max_entities
                .unwrap_or(DEFAULT_MAX_ENTITIES)
                .clamp(1, MAX_ENTITIES_CEILING),
        }
    }

    fn keeps(&self, entity: &EntityType) -> bool {
        if let Some(layers) = &self.layers {
            if !layers.contains(&entity.common().layer.to_ascii_uppercase()) {
                return false;
            }
        }
        if let Some(types) = &self.types {
            if !types.contains(&entity.as_entity().entity_type().to_ascii_uppercase()) {
                return false;
            }
        }
        true
    }
}

/// An empty list means "no filter", not "match nothing": a caller that sends `layers: []` wants
/// everything, and answering with zero entities would look like an empty drawing.
fn normalize(values: Option<Vec<String>>) -> Option<HashSet<String>> {
    let values = values?;
    if values.is_empty() {
        return None;
    }
    Some(
        values
            .into_iter()
            .map(|v| v.trim().to_ascii_uppercase())
            .collect(),
    )
}

pub fn build(doc: &CadDocument, space: Space, filters: &Filters) -> ReadResult {
    let selection = open::select(doc, space);

    let mut entities = Vec::new();
    let mut skipped_by_type: BTreeMap<String, usize> = BTreeMap::new();
    let mut matched = 0usize;
    let mut truncated = false;
    let mut bounds = None;

    for entity in &selection.entities {
        if !filters.keeps(entity) {
            continue;
        }
        matched += 1;
        structure::accumulate(&mut bounds, entity);

        // Past the cap we keep counting matches — so `matched` stays the true size of the request
        // and the caller can raise the cap knowingly — but stop building DTOs.
        if entities.len() >= filters.max_entities {
            truncated = true;
            continue;
        }

        let type_name = entity.as_entity().entity_type().to_string();
        match geometry(entity) {
            Some(geometry) => entities.push(EntityDto {
                handle: format!("{:X}", entity.common().handle.value()),
                layer: entity.common().layer.clone(),
                r#type: type_name,
                color: color_info(&entity.common().color),
                line_type: entity.common().linetype.clone(),
                geometry,
            }),
            None => *skipped_by_type.entry(type_name).or_insert(0) += 1,
        }
    }

    ReadResult {
        space: space.as_str(),
        units: open::units(doc),
        matched,
        returned: entities.len(),
        truncated,
        entities,
        extents: bounds.map(structure::to_extents),
        skipped_by_type,
        warnings: selection.warnings,
    }
}

/// Maps one entity to wire geometry, or None when this build has no mapping for it.
///
/// The nine mapped types are the ones with a direct Revit counterpart (a curve, a text note, a
/// family instance). HATCH, DIMENSION, LEADER, SOLID3D and the rest are counted and named in
/// `skipped_by_type` instead of being half-converted: a hatch turned into its boundary loops is a
/// different drawing, and that decision belongs to whoever asked for the import, not here.
fn geometry(entity: &EntityType) -> Option<Geometry> {
    match entity {
        EntityType::Line(line) => Some(Geometry::Line {
            start: xyz(line.start),
            end: xyz(line.end),
        }),
        EntityType::Point(point) => Some(Geometry::Point {
            location: xyz(point.location),
        }),
        EntityType::Circle(circle) => Some(Geometry::Circle {
            center: xyz(circle.center),
            radius: circle.radius,
            normal: xyz(circle.normal),
        }),
        EntityType::Arc(arc) => Some(Geometry::Arc {
            center: xyz(arc.center),
            radius: arc.radius,
            start_angle: arc.start_angle,
            end_angle: arc.end_angle,
            normal: xyz(arc.normal),
        }),
        EntityType::Ellipse(ellipse) => Some(Geometry::Ellipse {
            center: xyz(ellipse.center),
            major_axis: xyz(ellipse.major_axis),
            minor_axis_ratio: ellipse.minor_axis_ratio,
            start_parameter: ellipse.start_parameter,
            end_parameter: ellipse.end_parameter,
            normal: xyz(ellipse.normal),
        }),
        // LWPOLYLINE stores 2D points plus one elevation for the whole polyline.
        EntityType::LwPolyline(polyline) => Some(Geometry::Polyline {
            closed: polyline.is_closed,
            vertices: polyline
                .vertices
                .iter()
                .map(|v| PolyVertex {
                    point: [v.location.x, v.location.y, polyline.elevation],
                    bulge: v.bulge,
                })
                .collect(),
        }),
        // The heavy 2D POLYLINE: 3D vertices, per-vertex bulge.
        EntityType::Polyline2D(polyline) => Some(Geometry::Polyline {
            closed: polyline.flags.is_closed(),
            vertices: polyline
                .vertices
                .iter()
                .map(|v| PolyVertex {
                    point: xyz(v.location),
                    bulge: v.bulge,
                })
                .collect(),
        }),
        // The old 3D POLYLINE: no bulge exists on a 3D vertex, every segment is straight.
        EntityType::Polyline(polyline) => Some(Geometry::Polyline {
            closed: polyline.flags.is_closed(),
            vertices: polyline
                .vertices
                .iter()
                .map(|v| PolyVertex {
                    point: xyz(v.location),
                    bulge: 0.0,
                })
                .collect(),
        }),
        EntityType::Polyline3D(polyline) => Some(Geometry::Polyline {
            closed: polyline.flags.closed,
            vertices: polyline
                .vertices
                .iter()
                .map(|v| PolyVertex {
                    point: xyz(v.position),
                    bulge: 0.0,
                })
                .collect(),
        }),
        EntityType::Text(text) => Some(Geometry::Text {
            value: text.value.clone(),
            insertion: xyz(text.insertion_point),
            height: text.height,
            rotation: text.rotation,
            width_factor: text.width_factor,
            style: text.style.clone(),
        }),
        // The raw MTEXT string, formatting codes (\P, \f, stacked fractions) included. Parsing them
        // is the C# side's problem, and it needs the original to do it.
        EntityType::MText(mtext) => Some(Geometry::MText {
            value: mtext.value.clone(),
            insertion: xyz(mtext.insertion_point),
            height: mtext.height,
            rotation: mtext.rotation,
            rectangle_width: mtext.rectangle_width,
            style: mtext.style.clone(),
        }),
        EntityType::Insert(insert) => Some(Geometry::Insert {
            block_name: insert.block_name.clone(),
            insertion: xyz(insert.insert_point),
            rotation: insert.rotation,
            scale: [insert.x_scale(), insert.y_scale(), insert.z_scale()],
            normal: xyz(insert.normal),
        }),
        _ => None,
    }
}

fn xyz(v: Vector3) -> [f64; 3] {
    [v.x, v.y, v.z]
}
