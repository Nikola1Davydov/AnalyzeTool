# analysetool-dwg — the out-of-process DWG/DXF reader

Reads a `.dwg` or `.dxf` and answers with layers, counts and plain geometry over one JSON object per
line. **Revit never opens the file.** The add-in creates native Revit elements from the part the user
picked, so nothing is imported, nothing is exploded, and none of the `Import-*` line styles an
exploded DWG leaves behind ever enter the project.

## Why this exists as a separate process, in Rust

Reading DWG needs a codec. The alternatives are ODA/Teigha (paid membership), Autodesk RealDWG
(restricted), or Revit's own importer — which is the very thing this feature exists to avoid.
[`acadrust`](https://crates.io/crates/acadrust) is a pure-Rust DWG/DXF codec under **MPL-2.0**, a
file-level copyleft a proprietary plugin may link freely; obligations attach only to modified MPL
files, and none are modified here.

It is a *process* rather than a P/Invoke library for the same reason `AnalyseTool.Mcp` is: DWG is a
reverse-engineered format and a malformed file **can** panic the parser. In-process that is a Revit
crash with someone's unsaved model in it. Here it costs one request — `handle_request` catches the
unwind and answers `parser_panic`. A JSON pipe also needs no `unsafe` shim.

The protocol is deliberately the same shape as [OpenCADStudio](https://github.com/HakanSeven12/OpenCADStudio)'s
`--serve` (one JSON object per line over stdio), so that app could be swapped in as the backend
without touching anything on the C# side.

## Building

```bash
cargo build --release      # target/release/analysetool-dwg[.exe]
cargo test                 # writes a real DWG with the codec and reads it back through the protocol
cargo clippy --all-targets
```

The plugin build (`src/PluginAssets.targets`) runs `cargo build --release` and copies the binary to
`<plugin>\dwg\`. Always release: a debug build of the codec parses a large drawing an order of
magnitude slower. **No Rust toolchain is a warning, not an error** — the plugin builds without it and
the DWG commands answer `sidecar_missing`.

To run the add-in against a build of your own, point `ANALYSETOOL_DWG_SIDECAR` at the executable.

## Protocol

One JSON request per line on stdin, one JSON response per line on stdout. stdout carries the protocol
and nothing else; diagnostics go to stderr, which the host drains into its log.

```
$ analysetool-dwg --once '{"op":"ping"}'
{"id":null,"ok":true,"result":{"codec":"acadrust 0.4.1","protocol":1, ...}}

$ echo '{"id":1,"op":"structure","path":"C:/plans/site.dwg"}' | analysetool-dwg
```

| op | takes | answers |
| --- | --- | --- |
| `ping` | — | name, version, protocol, codec, formats, ops |
| `structure` | `path`, `space`, `failsafe` | layers with per-type counts, blocks, units, version, extents, parse diagnostics |
| `read` | `path`, `layers`, `types`, `space`, `maxEntities`, `failsafe` | entities as flat geometry, plus `matched` / `truncated` / `skippedByType` / `extents` |

`space` is `model` (default), `paper` or `all`. `all` includes the contents of block definitions, so
its counts are higher than what is actually drawn.

Failures answer `{"ok":false,"error":{"code":…,"message":…}}`. The codes — `bad_request`,
`unknown_op`, `missing_argument`, `not_found`, `unsupported_format`, `read_failed`, `parser_panic` —
are what the C# client branches on, so they are part of the contract.

### What crosses the pipe

Coordinates and lengths are in **drawing units** (the ones `units` reports); angles are in
**radians**, which is what both the codec and the Revit API use, so nothing converts them on either
side. `units.code == 0` means the drawing is UNITLESS — very common, impossible to infer, and the
caller must ask a human.

Nine geometry kinds are mapped: `line`, `point`, `circle`, `arc`, `ellipse`, `polyline`, `text`,
`mText`, `insert`. Everything else (HATCH, DIMENSION, 3DSOLID, proxy entities…) is counted by type in
`skippedByType` rather than dropped — "nothing came back" and "4 812 hatches were skipped" are
different problems.

Polylines keep their per-vertex **bulge** (`tan(sweep/4)` of the arc to the next vertex) instead of
being tessellated. Turning arcs into line soup is exactly what makes an exploded DWG unusable, and
doing it here would reproduce the problem this whole thing exists to avoid.

## Known limits

- **Proxy and custom objects** — Civil 3D alignments, corridors and pipe networks are closed formats.
  No open library reads them; they arrive as unknown entities and are counted, not converted.
- `acadrust` is a young single-maintainer crate. Real files from recent AutoCAD releases should be
  piloted before anyone depends on this in production; `failsafe: true` gets diagnostics out of a
  file that otherwise fails outright.
- Reading only. Writing DWG (the "clean the file outside Revit, then link the clean one" workflow) is
  the obvious next step — the codec supports it, this binary does not expose it yet.
