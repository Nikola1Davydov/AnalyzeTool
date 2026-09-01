//! Out-of-process DWG/DXF reader for the AnalyseTool Revit add-in.
//!
//! The point of the whole thing: Revit never opens the DWG. This process does, hands back layers,
//! counts and plain geometry, and the add-in creates NATIVE Revit elements from the part the user
//! picked. Nothing is linked, nothing is exploded, and none of the `Import-*` line styles that an
//! exploded DWG leaves behind ever enter the project.
//!
//! The dispatch lives in the library rather than in `main` so the protocol can be tested without
//! spawning a process — see `tests/roundtrip.rs`.

pub mod open;
pub mod read;
pub mod structure;
pub mod wire;

use serde_json::json;
use std::panic::{catch_unwind, AssertUnwindSafe};

use wire::{codes, Request, Response};

/// Bumped when a change to the wire shape is not backwards compatible. `ping` reports it, and the
/// C# client refuses a sidecar whose number it does not know — a silently mismatched protocol
/// produces wrong geometry, which is far worse than a clear refusal at startup.
pub const PROTOCOL_VERSION: u32 = 1;

/// Handles one input line. Returns None for a blank line, so an idle pipe stays quiet.
pub fn handle_line(line: &str) -> Option<String> {
    let line = line.trim();
    if line.is_empty() {
        return None;
    }

    let response = match serde_json::from_str::<Request>(line) {
        Ok(request) => handle_request(request),
        // The id lives inside the line we just failed to parse, so there is nothing to echo.
        Err(e) => Response::err(None, codes::BAD_REQUEST, format!("malformed request: {e}")),
    };
    Some(response.to_line())
}

/// Runs one request.
///
/// Every op is wrapped in `catch_unwind`: DWG is a reverse-engineered format and a malformed file
/// CAN panic the codec. In-process that would be a Revit crash with the user's unsaved model in
/// it; here it costs one request. This is the single strongest argument for the separate process,
/// so it is not left to chance.
pub fn handle_request(request: Request) -> Response {
    let id = request.id;

    let outcome = catch_unwind(AssertUnwindSafe(|| dispatch(request)));
    match outcome {
        Ok(response) => response,
        Err(payload) => Response::err(
            id,
            codes::PARSER_PANIC,
            format!(
                "the DWG/DXF parser panicked on this file: {}",
                panic_message(&payload)
            ),
        ),
    }
}

fn dispatch(request: Request) -> Response {
    let id = request.id;

    match request.op.as_str() {
        "ping" => Response::ok(
            id,
            json!({
                "name": env!("CARGO_PKG_NAME"),
                "version": env!("CARGO_PKG_VERSION"),
                "protocol": PROTOCOL_VERSION,
                "codec": format!("acadrust {}", acadrust::VERSION),
                "formats": ["dwg", "dxf"],
                "ops": ["ping", "structure", "read"],
            }),
        ),

        op @ ("structure" | "read") => {
            let Some(path) = request.path.as_deref().filter(|p| !p.trim().is_empty()) else {
                return Response::err(id, codes::MISSING_ARGUMENT, format!("'{op}' requires 'path'"));
            };

            let space = match open::Space::parse(request.space.as_deref()) {
                Ok(space) => space,
                Err(e) => return Response::err(id, e.code, e.message),
            };

            let opened = match open::open(path, request.failsafe.unwrap_or(false)) {
                Ok(opened) => opened,
                Err(e) => return Response::err(id, e.code, e.message),
            };

            let value = if op == "structure" {
                serde_json::to_value(structure::build(path, opened.format, &opened.doc, space))
            } else {
                let filters = read::Filters::new(request.layers, request.types, request.max_entities);
                serde_json::to_value(read::build(&opened.doc, space, &filters))
            };

            match value {
                Ok(value) => Response::ok(id, value),
                Err(e) => Response::err(
                    id,
                    codes::READ_FAILED,
                    format!("serializing the result failed: {e}"),
                ),
            }
        }

        other => Response::err(
            id,
            codes::UNKNOWN_OP,
            format!("unknown op '{other}' — this build implements ping, structure and read"),
        ),
    }
}

fn panic_message(payload: &(dyn std::any::Any + Send)) -> String {
    if let Some(s) = payload.downcast_ref::<&str>() {
        return (*s).to_string();
    }
    if let Some(s) = payload.downcast_ref::<String>() {
        return s.clone();
    }
    "no message".to_string()
}
