# AnalyseTool — AI instructions for writing extensions

> **How to use this file:** paste it into Claude / ChatGPT as context, then ask it to build an
> AnalyseTool extension (e.g. "write a command that renumbers selected doors"). It contains the full
> contract, the manifest schema, the rules, and worked examples — everything the model needs to
> generate a correct extension in one shot.

You are helping write **extensions for AnalyseTool**, a Revit 2025/2026/2027 add-in. Extensions add
functionality **without rebuilding the host** — the user drops a folder into their extensions
directory and clicks **Reload**.

---

## 1. The three kinds of extension

| Kind | Ships | Role | Build needed? |
| --- | --- | --- | --- |
| **C# command** | a `.dll` of `IRevitTask` classes | **ADDS** commands | yes (`dotnet build`) |
| **Script** | a plain `.cs` file | ADDS commands, compiled at runtime by Roslyn | **no** |
| **JS / UI** | an HTML page | **CONSUMES** commands via `AT.invoke(...)` | no |

One folder can be C#-only, UI-only, script, or a combination. **The principle:** C#/script
extensions *add* commands to a shared dispatcher; JS pages *consume* them.

---

## 2. The C# contract (this is the whole surface)

```csharp
namespace AnalyseTool.Sdk
{
    public interface IRevitTask
    {
        Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken);
    }

    public interface IRevitContext
    {
        RevitPayload Payload { get; }                         // the JSON the caller sent
        Task<T> RunInRevitAsync<T>(Func<UIApplication, T> work); // touch the model ONLY here
        Task   RunInRevitAsync(Action<UIApplication> work);
    }

    public sealed class RevitPayload
    {
        public T?     As<T>();      // deserialize the payload (case-insensitive)
        public string RawJson { get; }
    }

    // Optional metadata. Without a name argument the wire name = the class name.
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RevitCommandAttribute : Attribute
    {
        public RevitCommandAttribute();
        public RevitCommandAttribute(string name);
        public string? Name { get; }
        public string? Description { get; set; }   // shown to humans + AI (MCP)
        public bool    ReadOnly   { get; set; }    // command only reads the model
        public bool    Destructive{ get; set; }    // command may modify/delete
        public Type?   InputType  { get; set; }    // generates the JSON input schema
        public bool    HiddenFromMcp { get; set; } // callable from JS, hidden from the AI tool list
    }

    // OPTIONAL (SDK 1.1+): implement alongside IRevitTask on a long-running command to report live
    // progress. The host sets Progress before ExecuteAsync (null when nobody listens); from JS use
    // AT.invoke(cmd, payload, { onProgress: p => ... }) — p = { fraction, message }.
    // For the bar to animate, work in CHUNKS with one RunInRevitAsync per chunk and
    // Progress?.Report(new ProgressInfo(done/total, "…")) between them.
    public sealed record ProgressInfo(double Fraction, string? Message = null);
    public interface IProgressAware
    {
        IProgress<ProgressInfo>? Progress { get; set; }
    }
}
```

### The ONE rule
- **Touch the Revit model ONLY inside `RunInRevitAsync`.** Reads and writes both go there. It runs
  on the Revit thread in a valid API context (transactions allowed).
- **Keep slow I/O (HTTP, AI, file reads) OUTSIDE `RunInRevitAsync`** — its body runs synchronously on
  the Revit thread and will freeze the UI. Do slow work first, then marshal only the model touch.
- **Never** touch the WebView, the network, or any transport detail from a command. Return a
  serializable object; the host delivers it. Throw to report an error (the message reaches the caller).

### Minimal C# command

```csharp
using AnalyseTool.Sdk;

namespace Acme.Doors
{
    [RevitCommand(Description = "Returns the number of doors in the active document.", ReadOnly = true)]
    public sealed class CountDoors : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken) =>
            revitContext.RunInRevitAsync<object?>(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                int count = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfCategory(Autodesk.Revit.DB.BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                return new { count };
            });
    }
}
```

### C# command that writes (transaction inside RunInRevitAsync)

```csharp
[RevitCommand(Description = "Sets the Comments parameter on the given elements.",
              Destructive = true, InputType = typeof(Args))]
public sealed class SetComment : IRevitTask
{
    public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
    {
        var args = revitContext.Payload.As<Args>()!;                      // read the payload
        return revitContext.RunInRevitAsync<object?>(app =>
        {
            var doc = app.ActiveUIDocument.Document;
            using var t = new Autodesk.Revit.DB.Transaction(doc, "Acme: set comments");
            t.Start();
            foreach (long id in args.ElementIds)
            {
                var el = doc.GetElement(new Autodesk.Revit.DB.ElementId(id));
                el?.get_Parameter(Autodesk.Revit.DB.BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                  ?.Set(args.Comment);
            }
            t.Commit();
            return new { updated = args.ElementIds.Count };
        });
    }

    internal sealed record Args
    {
        [System.ComponentModel.Description("Element ids to update.")]
        public List<long> ElementIds { get; set; } = new();
        [System.ComponentModel.Description("Text to write into Comments.")]
        public string Comment { get; set; } = "";
    }
}
```

### Command naming
Wire name = `[RevitCommand]` name, else the class name. The host prefixes it with the extension `id`:
```
id "acme.doors"  +  class "CountDoors"  →  "acme.doors.CountDoors"
```
Call it from JS as `AT.invoke("acme.doors.CountDoors")`.

---

## 3. The manifest — `plugin.json` (required, sits in the extension folder root)

```json
{
  "id": "acme.doors",
  "version": "1.0.0",
  "entryAssembly": "Acme.Doors.dll",
  "ui": {
    "entryHtml": "index.html",
    "tab": "AnalyseTool",
    "panel": "Acme",
    "button": {
      "name": "Doors",
      "tooltip": "Open the Doors tool",
      "icon": "icon.png",
      "command": "acme.doors.CountDoors"
    }
  }
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | ✔ | Unique, lowercase, dotted. Becomes the command prefix and the folder name. Valid chars: letters/digits/`.`/`-`/`_`. |
| `version` | ✔ | SemVer string. |
| `entryAssembly` | — | DLL name. **Omit** for UI-only or script extensions. |
| `ui` | — | **Omit** for a command-only extension (callable from JS/MCP but no button). |
| `ui.entryHtml` | — | Page to open. Default `index.html`. |
| `ui.tab` / `ui.panel` | — | Ribbon placement. Default tab `"AnalyseTool"`, panel `"Extensions"`. |
| `ui.button.name` | — | Button label (also the display name). |
| `ui.button.command` | — | If set, clicking the button **runs this command** (shows the result in a dialog) instead of opening the HTML page. Use for command-only extensions that want a button. |
| `ui.dockable` | — | `true` = the button shows the page inside AnalyseTool's shared **dockable pane** (docks like the Project Browser; click again = hide, another dockable button = switch content) instead of a separate window. |

---

## 4. C# project setup (NuGet — the easy way)

```
dotnet add package AnalyseTool.Sdk
```

Minimal `.csproj` — declare the TFM and the Revit API packages yourself (NuGet ignores build props
shipped inside packages during restore, so the SDK package cannot add them for you):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- net8.0-windows for Revit 2025/2026, net10.0-windows for Revit 2027 -->
    <TargetFramework>net8.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <RootNamespace>Acme.Doors</RootNamespace>
    <AssemblyName>Acme.Doors</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AnalyseTool.Sdk" Version="1.1.*">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="2025.*">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="2025.*">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```
Target another Revit year by switching the `Nice3point.Revit.Api.*` version (`2026.*` / `2027.*`) and,
for 2027, the TFM to `net10.0-windows`. Build: `dotnet build -c Release`.

> **Critical:** the host owns `AnalyseTool.Sdk.dll`, the Revit API, and `Newtonsoft.Json`. The
> extension's load context shares the host's copies (type identity), so **do not ship copies of those
> DLLs**. With the NuGet package this is automatic. Deploy only your DLL + `plugin.json` (+ assets).

---

## 5. Script extension (no build at all)

Drop a `.cs` file next to `plugin.json` (with **no** `entryAssembly`). Roslyn compiles it on load.
Two accepted forms:

**Body form** — just statements; `uiapp` / `uidoc` / `doc` are in scope, `return` any object:
```csharp
var walls = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().GetElementCount();
return new { walls };
```
Registered as `<id>.Script`.

**Class form** — a full `IRevitTask` (as in §2), for metadata and multiple commands.

`plugin.json` for a script extension (no `entryAssembly`):
```json
{ "id": "acme.walls", "version": "1.0.0",
  "ui": { "panel": "Acme", "button": { "name": "Count walls", "command": "acme.walls.Script" } } }
```

---

## 6. JS / UI extension

The host opens your `index.html` in a WebView and injects `window.AT`. Any framework works.

```js
// Call any registered command (built-in or from any extension). Returns a Promise.
const res = await window.AT.invoke("acme.doors.CountDoors", /* optional payload */ {});

// Discover what you can call (name, source, description, payload schema, flags):
const { commands } = await window.AT.invoke("GetCommands");
```
- `invoke` is id-correlated, so concurrent calls are fine; it **resolves** with the result and
  **rejects** with the error message.
- Built with a framework: set Vite `base: "./"` (relative assets) and ship `dist` next to `plugin.json`.

---

## 7. Deploy & reload

```
%LOCALAPPDATA%\AnalyseTool\extensions\<RevitYear>\<id>\
    plugin.json
    <YourExt>.dll        (C#)  |  *.cs (script)
    index.html           (UI)
    icon.png             (optional)
```
- `<RevitYear>` = `2025`, `2026` or `2027`.
- Changed code/manifest → **Reload** (AnalyseTool tab → Settings → Reload). No restart.
- A brand-new ribbon button needs a **Revit restart** the first time.

---

## 8. Rules — ALWAYS / NEVER

- **ALWAYS** touch the Revit model only inside `RunInRevitAsync`. Open transactions there.
- **ALWAYS** use a lean input record for `InputType` (only the fields the caller sends) and put a
  `[System.ComponentModel.Description("…")]` on each field. Do **not** reuse rich/nested domain models —
  the generated schema balloons.
- **NEVER** ship copies of `AnalyseTool.Sdk` / Revit API / `Newtonsoft.Json` in the extension output.
- **NEVER** touch the WebView, sockets, or threads from a command — return a serializable object.
- **Category names are language-specific.** On a German Revit a wall's category is `"Wände"`, not
  `"Walls"`. To resolve names, call `GetCategoriesInRevit` first; don't hard-code English names.
- Return plain, serializable data (numbers, strings, lists, anonymous objects). Don't return raw
  Revit `Element`/`Parameter` objects.

---

## 9. AI features — reuse the ONE shared model (do not build your own picker)

AnalyseTool has a **single, global AI (Ollama) model** shared by every window. It is **not** stored in a
C# backend — it lives in the WebView's `localStorage`, which is shared across all plugin windows (same
WebView2 profile + origin). So an AI-powered UI extension must **read** the active model, never re-prompt
the user to pick one.

- **Model selection lives ONLY in the Settings window.** Every other window shows a read-only indicator
  (active model + Ollama on/off). Do not add a model dropdown to your extension UI.
- **localStorage keys** (read these to know the active model): `ollama-model` (model name),
  `ai-model-source` (`"local"` | `"cloud"`), `ai-cloud-models` (JSON array of saved cloud model names).
  A `storage` event fires when another window changes them.
- **Ollama status / local models:** `AT.invoke("OllamaGetModels")` → `{ running: bool, models: string[]|null }`
  (`running:false` = Ollama unreachable; distinct from "running with 0 models").
- **Existing AI commands** (all `HiddenFromMcp`, run Ollama on the host): `OllamaAnalyse`,
  `OllamaEditParameters` (returns suggested parameter edits), `OllamaSuggestName` (one new name from a
  current name + instruction). Pass `{ model, prompt, … }` where `model` is the shared model name.
- In your **own** C# AI command: take the model name in the payload, and run the AI/HTTP call **outside**
  `RunInRevitAsync` (see §2 — slow I/O must not block the Revit thread); marshal only the model touch.

---

## 10. Checklist for a generated extension

- [ ] `plugin.json` with `id` (+ `entryAssembly` for C#, or none for script/UI).
- [ ] C#: one or more `IRevitTask` classes; model access only in `RunInRevitAsync`.
- [ ] `[RevitCommand]` with a clear `Description`; `ReadOnly`/`Destructive` set correctly; `InputType`
      for commands that take arguments.
- [ ] UI: `index.html` calling `window.AT.invoke(...)`; `base: "./"` if framework-built.
- [ ] Tell the user the deploy path and that they click **Reload** (or restart for a new button).
