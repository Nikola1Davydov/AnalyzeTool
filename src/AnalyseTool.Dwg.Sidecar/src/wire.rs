//! The wire contract. One JSON object per line, in and out — the same shape the C# side declares in
//! `DwgWire.cs`, so a field rename has to be made twice on purpose rather than once by accident.

use serde::{Deserialize, Serialize};

/// One request line. Unknown fields are rejected: a misspelled `maxEntities` that is silently
/// ignored looks exactly like a cap that did not apply, and the caller has no way to tell.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Request {
    /// Correlation id, echoed back. Optional so a hand-typed line still works.
    #[serde(default)]
    pub id: Option<i64>,
    /// `ping` | `structure` | `read`.
    pub op: String,
    /// Absolute path of the .dwg/.dxf to open. Required by everything but `ping`.
    #[serde(default)]
    pub path: Option<String>,
    /// Layer names to keep (case-insensitive). None/empty = every layer.
    #[serde(default)]
    pub layers: Option<Vec<String>>,
    /// DXF type names to keep, e.g. ["LINE","LWPOLYLINE"]. None/empty = every type.
    #[serde(default)]
    pub types: Option<Vec<String>>,
    /// `model` (default) | `paper` | `all`.
    #[serde(default)]
    pub space: Option<String>,
    /// Cap on returned entities (`read` only). Defaults to 20 000, hard-capped at 200 000.
    #[serde(default)]
    pub max_entities: Option<usize>,
    /// Error-tolerant DWG parsing: collect diagnostics instead of failing on the first bad object.
    #[serde(default)]
    pub failsafe: Option<bool>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Response {
    pub id: Option<i64>,
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub result: Option<serde_json::Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<WireError>,
}

/// A machine-readable `code` next to the human `message`: the caller branches on "this file is not
/// a DWG" versus "this DWG is broken" without matching English text.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WireError {
    pub code: String,
    pub message: String,
}

/// Error codes this process can answer with. Kept as constants because the C# side matches on them.
pub mod codes {
    /// The line was not a JSON request this version understands.
    pub const BAD_REQUEST: &str = "bad_request";
    /// `op` is not one this build implements.
    pub const UNKNOWN_OP: &str = "unknown_op";
    /// A required field was missing or empty.
    pub const MISSING_ARGUMENT: &str = "missing_argument";
    /// The path does not exist or is not a file.
    pub const NOT_FOUND: &str = "not_found";
    /// The extension is neither .dwg nor .dxf.
    pub const UNSUPPORTED_FORMAT: &str = "unsupported_format";
    /// The codec refused the file (corrupt, or a construct it does not implement).
    pub const READ_FAILED: &str = "read_failed";
    /// The parser panicked. The file is the cause; the process survives (see `lib::handle_request`).
    pub const PARSER_PANIC: &str = "parser_panic";
}

impl Response {
    pub fn ok(id: Option<i64>, result: serde_json::Value) -> Self {
        Self {
            id,
            ok: true,
            result: Some(result),
            error: None,
        }
    }

    pub fn err(id: Option<i64>, code: &str, message: impl Into<String>) -> Self {
        Self {
            id,
            ok: false,
            result: None,
            error: Some(WireError {
                code: code.to_string(),
                message: message.into(),
            }),
        }
    }

    /// Serializes to one line. Serialization of our own types cannot fail; if it somehow does, a
    /// hand-built error line is still valid JSON, so the caller sees a failure rather than a hang.
    pub fn to_line(&self) -> String {
        serde_json::to_string(self).unwrap_or_else(|e| {
            format!(
                r#"{{"id":null,"ok":false,"error":{{"code":"bad_request","message":{}}}}}"#,
                serde_json::Value::String(format!("response serialization failed: {e}"))
            )
        })
    }
}
