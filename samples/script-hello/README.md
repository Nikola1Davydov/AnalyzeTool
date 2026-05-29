# Script extension sample

A **script extension** is a folder with `plugin.json` + one or more `.cs` files and **no** prebuilt
DLL (no `entryAssembly` in the manifest). The host compiles the `.cs` with Roslyn on load/Reload —
no project, no SDK reference, no `dotnet build`. Edit the `.cs` in VS Code, click **Reload**, done.

## Deploy

Copy this folder into the running Revit version's extensions directory:

```
%LOCALAPPDATA%\AnalyseTool\extensions\<year>\sample.script.hello\
    plugin.json
    Hello.cs
```

(`<year>` = the Revit version, e.g. `2025`.) Then click **Reload** in AnalyseTool Settings — or just
restart Revit. Call the command from a JS extension or the WebView console:

```js
await AT.invoke("sample.script.hello.Script")
// → { documentName: "...", wallCount: 12 }
```

## Two accepted forms (hybrid)

**Body form** (this sample, `Hello.cs`) — just statements. `uiapp` / `uidoc` / `doc` are in scope,
the body runs in a valid Revit API context, `return` any object. The command is named `<id>.Script`.

**Class form** — a full `IRevitTask`, identical to a DLL extension but compiled from source. Use this
for first-class metadata (custom command name, `[RevitCommand]` description, multiple commands):

```csharp
using AnalyseTool.Sdk;

[RevitCommand(Description = "Counts walls in the active document.", ReadOnly = true)]
public sealed class CountWalls : IRevitTask
{
    public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
        ctx.RunInRevitAsync<object?>(uiapp =>
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            var count = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                .OfCategory(Autodesk.Revit.DB.BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .GetElementCount();
            return new { count };
        });
}
```

Compile errors (if any) appear in AnalyseTool Settings next to the extension.
