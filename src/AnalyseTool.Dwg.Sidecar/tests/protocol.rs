//! End-to-end protocol tests: build a drawing with the codec, write a real DWG, then read it back
//! through the same `handle_line` the stdio loop calls. Nothing is mocked — a change that breaks
//! the wire shape, the space scoping or the layer counting fails here.

use acadrust::entities::{Arc, Circle, EntityType, Line, LwPolyline};
use acadrust::types::{Vector2, Vector3};
use acadrust::{CadDocument, DwgWriter};
use analysetool_dwg::handle_line;
use serde_json::Value;
use std::path::PathBuf;

/// Four entities on two layers, in millimetres:
///   A-WALL  — one LINE, one LWPOLYLINE (three vertices, the middle one bulged)
///   0       — one CIRCLE, one ARC
fn sample_document() -> CadDocument {
    let mut doc = CadDocument::new();
    doc.header.insertion_units = 4; // millimetres

    // A table entry needs a handle before it is added: without one the DWG writer cannot point an
    // entity at the layer, and every entity comes back on layer "0" — silently.
    let mut layer = acadrust::Layer::new("A-WALL");
    layer.handle = doc.allocate_handle();
    doc.layers.add(layer).expect("adding the A-WALL layer");

    let mut line = Line::from_coords(0.0, 0.0, 0.0, 5000.0, 0.0, 0.0);
    line.common.layer = "A-WALL".to_string();
    doc.add_entity(EntityType::Line(line)).expect("adding the line");

    let mut polyline = LwPolyline::new();
    polyline.add_point(Vector2::new(0.0, 0.0));
    polyline.add_point_with_bulge(Vector2::new(1000.0, 0.0), 0.5);
    polyline.add_point(Vector2::new(1000.0, 2000.0));
    polyline.common.layer = "A-WALL".to_string();
    doc.add_entity(EntityType::LwPolyline(polyline))
        .expect("adding the polyline");

    let circle = Circle::from_center_radius(Vector3::new(100.0, 200.0, 0.0), 50.0);
    doc.add_entity(EntityType::Circle(circle))
        .expect("adding the circle");

    let arc = Arc::from_center_radius_angles(
        Vector3::new(0.0, 0.0, 0.0),
        300.0,
        0.0,
        std::f64::consts::FRAC_PI_2,
    );
    doc.add_entity(EntityType::Arc(arc)).expect("adding the arc");

    doc
}

/// Writes the sample drawing next to the test binary. `CARGO_TARGET_TMPDIR` is cleaned by cargo, so
/// nothing has to be torn down. The per-test `name` matters: cargo runs these in parallel threads, and
/// one shared path means one test reading the file another is still writing.
fn sample_dwg(name: &str) -> PathBuf {
    let path = PathBuf::from(env!("CARGO_TARGET_TMPDIR")).join(format!("{name}.dwg"));
    DwgWriter::write_to_file(&path, &sample_document()).expect("writing the sample DWG");
    path
}

fn call(request: Value) -> Value {
    let line = handle_line(&request.to_string()).expect("a non-blank request gets a response");
    serde_json::from_str(&line).expect("the response is JSON")
}

fn result_of(response: &Value) -> &Value {
    assert!(
        response["ok"].as_bool().unwrap_or(false),
        "expected ok, got {response}"
    );
    &response["result"]
}

#[test]
fn ping_reports_the_protocol_and_the_codec() {
    let response = call(serde_json::json!({ "id": 1, "op": "ping" }));
    let result = result_of(&response);

    assert_eq!(response["id"], 1);
    assert_eq!(result["protocol"], analysetool_dwg::PROTOCOL_VERSION);
    assert!(
        result["codec"].as_str().unwrap().starts_with("acadrust "),
        "ping should name the codec, got {}",
        result["codec"]
    );
}

#[test]
fn structure_reports_layers_units_and_counts() {
    let path = sample_dwg("structure_reports_layers_units_and_counts");
    let response = call(serde_json::json!({ "id": 2, "op": "structure", "path": path }));
    let result = result_of(&response);

    assert_eq!(result["format"], "dwg");
    assert_eq!(result["space"], "model");
    assert_eq!(result["units"]["code"], 4);
    assert_eq!(result["units"]["name"], "millimeters");
    assert_eq!(result["entityCount"], 4);
    assert_eq!(result["byType"]["LINE"], 1);
    assert_eq!(result["byType"]["LWPOLYLINE"], 1);
    assert_eq!(result["byType"]["CIRCLE"], 1);
    assert_eq!(result["byType"]["ARC"], 1);

    let layers = result["layers"].as_array().expect("layers is an array");
    let wall = layers
        .iter()
        .find(|l| l["name"] == "A-WALL")
        .expect("the A-WALL layer is reported");
    assert_eq!(wall["entityCount"], 2);
    assert_eq!(wall["byType"]["LINE"], 1);

    // Layers are sorted by how much they carry, so the busiest one is first — that ordering is what
    // makes the list usable on a drawing with 200 layers.
    assert!(layers[0]["entityCount"].as_u64() >= layers[1]["entityCount"].as_u64());

    // The drawing spans 5 m in x; the extents must be finite and cover it.
    let extents = &result["extents"];
    assert!(extents["max"][0].as_f64().unwrap() >= 5000.0, "got {extents}");
}

#[test]
fn read_returns_geometry_and_honours_the_layer_filter() {
    let path = sample_dwg("read_returns_geometry_and_honours_the_layer_filter");
    let response = call(serde_json::json!({
        "id": 3, "op": "read", "path": path, "layers": ["a-wall"]
    }));
    let result = result_of(&response);

    // Lower case in the request, upper case in the file: the filter is case-insensitive.
    assert_eq!(result["matched"], 2);
    assert_eq!(result["returned"], 2);
    assert_eq!(result["truncated"], false);

    let entities = result["entities"].as_array().unwrap();
    let line = entities
        .iter()
        .find(|e| e["type"] == "LINE")
        .expect("the line comes back");
    assert_eq!(line["geometry"]["kind"], "line");
    assert_eq!(line["geometry"]["end"][0], 5000.0);
    assert!(!line["handle"].as_str().unwrap().is_empty());

    let polyline = entities
        .iter()
        .find(|e| e["type"] == "LWPOLYLINE")
        .expect("the polyline comes back");
    // Extents cover the matched entities, so a caller can recentre survey coordinates in one pass
    // instead of parsing the file twice.
    assert_eq!(result["extents"]["max"][0].as_f64().unwrap(), 5000.0);

    let vertices = polyline["geometry"]["vertices"].as_array().unwrap();
    assert_eq!(vertices.len(), 3);
    // The bulge survives: an arc segment stays an arc instead of being tessellated into lines.
    assert_eq!(vertices[1]["bulge"], 0.5);
}

#[test]
fn read_caps_the_result_and_says_so() {
    let path = sample_dwg("read_caps_the_result_and_says_so");
    let response = call(serde_json::json!({
        "id": 4, "op": "read", "path": path, "maxEntities": 1
    }));
    let result = result_of(&response);

    assert_eq!(result["returned"], 1);
    assert_eq!(
        result["matched"], 4,
        "matched counts past the cap so the caller can raise it"
    );
    assert_eq!(result["truncated"], true);
}

#[test]
fn read_names_the_types_it_could_not_map() {
    let path = sample_dwg("read_names_the_types_it_could_not_map");
    // ARC is mapped, so filtering to it must leave skippedByType empty — the point of the
    // assertion is that a mapped type never lands in the skipped bucket.
    let response = call(serde_json::json!({
        "id": 5, "op": "read", "path": path, "types": ["arc"]
    }));
    let result = result_of(&response);

    assert_eq!(result["matched"], 1);
    assert_eq!(result["skippedByType"], serde_json::json!({}));
    assert_eq!(result["entities"][0]["geometry"]["kind"], "arc");
}

#[test]
fn failures_carry_a_code_the_caller_can_branch_on() {
    let cases = [
        (serde_json::json!({ "op": "nope" }), "unknown_op"),
        (serde_json::json!({ "op": "structure" }), "missing_argument"),
        (
            serde_json::json!({ "op": "structure", "path": "/no/such/file.dwg" }),
            "not_found",
        ),
        (
            serde_json::json!({ "op": "structure", "path": file!() }),
            "unsupported_format",
        ),
        (
            serde_json::json!({ "op": "structure", "path": "x.dwg", "space": "sideways" }),
            "bad_request",
        ),
    ];

    for (request, expected) in cases {
        let response = call(request.clone());
        assert_eq!(response["ok"], false, "expected a failure for {request}");
        assert_eq!(response["error"]["code"], expected, "for {request}");
    }
}

#[test]
fn a_malformed_line_is_reported_rather_than_crashing_the_process() {
    let response: Value = serde_json::from_str(&handle_line("{ not json").unwrap()).unwrap();
    assert_eq!(response["error"]["code"], "bad_request");

    // An unknown field is refused too: a misspelled cap that is ignored looks like a cap that did
    // not apply, and the caller has no way to tell.
    let response = call(serde_json::json!({ "op": "ping", "maxEntitys": 5 }));
    assert_eq!(response["error"]["code"], "bad_request");

    assert_eq!(handle_line("   "), None, "a blank line gets no response");
}
