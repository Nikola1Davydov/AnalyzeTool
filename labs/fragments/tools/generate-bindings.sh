#!/usr/bin/env bash
# Regenerates the FlatBuffers bindings from schema/index.fbs.
#
#   C#     -> src/AnalyseTool.Fragments/Schema/   (checked in; flatc is not a build dependency)
#   Python -> tools/gen/py/                       (gitignored; only the inspector needs it)
#
# flatc must match the runtime the generated code asserts against — see FLATC_VERSION. That is also
# the version @thatopen/fragments pins, so our buffers stay byte-compatible with their reader.
set -euo pipefail

FLATC_VERSION="25.2.10"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$here")"
schema="$root/schema/index.fbs"

flatc="${FLATC:-$(command -v flatc || true)}"
if [[ -z "$flatc" ]]; then
  echo "flatc not found. Install v$FLATC_VERSION:"
  echo "  Linux : https://github.com/google/flatbuffers/releases/download/v$FLATC_VERSION/Linux.flatc.binary.g++-13.zip"
  echo "  Windows: https://github.com/google/flatbuffers/releases/download/v$FLATC_VERSION/Windows.flatc.binary.zip"
  echo "Then re-run, or point FLATC at the binary."
  exit 1
fi

echo "using $($flatc --version)  (expected flatc version $FLATC_VERSION)"

# The vendored schema has no namespace declaration, so flatc would emit Model/Material/Transform into
# the global namespace — and a global 'Transform' collides head-on with Autodesk.Revit.DB.Transform
# once this moves into the Revit-facing code. Prepend one for the C# pass only.
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
{ echo "namespace AnalyseTool.Fragments.Schema;"; echo; cat "$schema"; } > "$tmp/index.fbs"

echo "generating C# -> src/AnalyseTool.Fragments/Schema/"
rm -f "$root/src/AnalyseTool.Fragments/Schema/"*.cs
"$flatc" --csharp -o "$tmp/cs" "$tmp/index.fbs"
cp "$tmp/cs/AnalyseTool/Fragments/Schema/"*.cs "$root/src/AnalyseTool.Fragments/Schema/"

# Python keeps the flat, namespace-free layout the inspector imports directly.
echo "generating Python -> tools/gen/py/"
rm -rf "$here/gen/py"
"$flatc" --python -o "$here/gen/py" "$schema"

echo "done."
