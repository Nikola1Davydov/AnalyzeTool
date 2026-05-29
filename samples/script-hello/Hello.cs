// Body-form script extension (pyRevit-style).
//
// No project, no SDK reference, no build — just this .cs next to plugin.json. The host compiles it
// with Roslyn on load/Reload. In scope: `uiapp` (UIApplication), `uidoc` (UIDocument?), `doc`
// (Document?). The body runs inside a valid Revit API context; `return` any serializable object.
//
// Registered command name: "<id>.Script"  →  sample.script.hello.Script
// Call it from a JS extension via  AT.invoke("sample.script.hello.Script")  or over MCP.

var title = doc != null ? doc.Title : "(no active document)";
var wallCount = doc != null
    ? new FilteredElementCollector(doc)
        .OfCategory(BuiltInCategory.OST_Walls)
        .WhereElementIsNotElementType()
        .GetElementCount()
    : 0;

return new { documentName = title, wallCount };
