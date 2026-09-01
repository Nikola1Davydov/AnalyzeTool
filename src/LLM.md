# AnalyseTool — AI instructions for writing extensions

> **How to use this file:** paste it into Claude / ChatGPT as context, then ask it to build an
> AnalyseTool extension (e.g. "write a command that renumbers selected doors"). It contains the full
> contract, the manifest schema, the rules, and worked examples — everything the model needs to
> generate a correct extension in one shot.

You are helping write **extensions for AnalyseTool**, a Revit 2025/2026/2027 add-in. Extensions add
functionality **without rebuilding the host** — the user drops a folder into their extensions
directory and clicks **Reload**.

---

## 1. What to generate

| Kind | Ships | Role | Use it |
| --- | --- | --- | --- |
| **C# command** | a `.dll` of `IRevitTask` classes | **ADDS** commands | **always — this is the default** |
| **JS / UI** | an HTML page | **CONSUMES** commands via `AT.invoke(...)` | when the extension needs a page |
| **Script** | a plain `.cs` file, compiled at load by Roslyn | ADDS commands | only when explicitly asked (§5) |

**The principle:** C# extensions *add* commands to a shared dispatcher; JS pages *consume* them.
One folder can be C#-only, UI-only, or both.

> **Generate a compiled C# project (a `.dll`), not a script.** Skipping the build does not remove
> the compiler — it moves it onto the user's machine, at load time, inside Revit, where a syntax
> error becomes a red banner instead of a build error you can see and fix. Worse, a script has no
> per-year folders, so it cannot declare which Revit versions it supports: code that is valid on
> 2025 and invalid on 2027 looks fine until a user on 2027 opens Revit, and the manager cannot even
> flag it as incompatible. Everything the platform offers for distribution — the package format,
> `PackExtension`, update feeds — is built around per-year DLLs.
>
> Write a script ONLY when the request is explicitly for one (see §5). If a request would be
> naturally served by a script — "just run this quickly", "no project please" — produce the C#
> project anyway and say why in one sentence.

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
        public Type?   OutputType{ get; set; }     // SDK 1.2+: schema of what the command RETURNS
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
| `description` / `publisher` / `website` / `supportUrl` | — | Vendor metadata shown in the extension listing. Recommended when publishing. |
| `icon` | — | Extension-level PNG (relative path) for listings; falls back to `ui.button.icon`. |
| `ui.button.icon` | — | PNG beside `plugin.json`, **or** `glyph:E8A9` — a Segoe MDL2 Assets code point, the same source the host's own buttons use. Crisp at any DPI and nothing to ship. No icon at all draws a letter. |
| `updateFeed` | — | Update source: an HTTPS URL returning `{version, downloadUrl}`, or `github:owner/repo` (latest release, zip asset). Only for published extensions. |
| `entryAssembly` | — | DLL name. **Omit** for UI-only or script extensions. Resolved in the Revit-year subfolder first (`2025\`), then the folder root. |
| `ui` | — | **Omit** for a command-only extension (callable from JS/MCP but no button). |
| `ui.entryHtml` | — | Page to open. Default `index.html`. |
| `ui.tab` / `ui.panel` | — | Ribbon placement. Default tab `"AnalyseTool"`, panel `"Extensions"`. |
| `ui.button.name` | — | Button label (also the display name). |
| `ui.button.command` | — | If set, clicking the button **runs this command** (shows the result in a dialog) instead of opening the HTML page. Use for command-only extensions that want a button. |
| `ui.dockable` | — | `true` = the button shows the page inside AnalyseTool's shared **dockable pane** (docks like the Project Browser; click again = hide, another dockable button = switch content) instead of a separate window. |
| `schema` | — | Manifest FORMAT version, not yours. Absent = 1. Set `2` when you use `ui.buttons`. Older schemas keep loading. |

### 3.1 Several buttons — `ui.buttons`

One `ui.button` is right for one surface. An extension with **two** — say a manager window and a
dockable placement palette — needs `ui.buttons`, because the page to open and whether it docks are
properties of the SURFACE, not of the extension:

```json
{
  "schema": 2,
  "id": "acme.doors",
  "version": "1.0.0",
  "entryAssembly": "Acme.Doors.dll",
  "ui": {
    "tab": "AnalyseTool",
    "panel": "Acme",
    "buttons": [
      { "name": "Manager", "entryHtml": "dist/index.html", "icon": "manager.png" },
      { "name": "Palette", "entryHtml": "dist/palette.html", "dockable": true, "icon": "palette.png" }
    ]
  }
}
```

| Field | Meaning |
| --- | --- |
| `entryHtml` | Page for THIS button. Falls back to `ui.entryHtml`. |
| `dockable` | Docking for THIS button. Falls back to `ui.dockable`. |
| `tab` / `panel` | Placement for THIS button. Falls back to `ui.tab` / `ui.panel`. |
| `order` | Sort order in the panel; equal values keep declaration order. |
| `kind` | `push` (default) · `stacked` · `pulldown`. Unknown values fall back to `push`. |
| `items` | Entries of a `pulldown`. Ignored for other kinds. |
| `name` / `tooltip` / `icon` / `command` | As in the single-button form. |

**`stacked`** — consecutive stacked entries fill rows of three, the shape Revit's own stacked items
make; a fourth starts a new row. Placement comes from the first entry of a run, because a row cannot
straddle two panels.

**`pulldown`** — the head opens the list rather than running the first entry. Children behave like
any button (a page or a `command`) but carry no placement of their own, since they live inside the
parent.

```json
"buttons": [
  { "name": "Manager", "entryHtml": "dist/index.html" },
  { "name": "Palette", "entryHtml": "dist/palette.html", "dockable": true },
  { "name": "Rename", "kind": "stacked", "command": "acme.doors.Rename" },
  { "name": "Purge",  "kind": "stacked", "command": "acme.doors.Purge" },
  { "name": "Export", "kind": "pulldown", "items": [
      { "name": "Excel", "command": "acme.doors.ExportExcel" },
      { "name": "CSV",   "command": "acme.doors.ExportCsv" }
  ]}
]
```

`ui.button` (singular) still works and is still the right choice for one surface — do not rewrite a
working manifest. Use whichever fits; declaring both is not an error, `ui.buttons` simply wins.

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
    <!-- The Revit year drives the TFM, the API packages and the output folder. -->
    <RevitVersion>2025</RevitVersion>
    <!-- net8.0-windows for Revit 2025/2026, net10.0-windows for Revit 2027 — not a free choice,
         the Nice3point package for a year targets that year's runtime (else restore fails: NU1202). -->
    <TargetFramework Condition="'$(RevitVersion)' &lt; '2027'">net8.0-windows</TargetFramework>
    <TargetFramework Condition="'$(RevitVersion)' &gt;= '2027'">net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <RootNamespace>Acme.Doors</RootNamespace>
    <AssemblyName>Acme.Doors</AssemblyName>
    <!-- Build into <extension>\<year>\ — the layout a package uses, so the folder you develop in is
         the folder you ship, and builds for several Revit years coexist. -->
    <OutDir>$(MSBuildProjectDirectory)\$(RevitVersion)\</OutDir>
  </PropertyGroup>
  <ItemGroup>
    <!-- Exact version, never a range: a pinned package is the reason your build cannot be
         broken by someone else's release. -->
    <PackageReference Include="AnalyseTool.Sdk" Version="1.1.2">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="$(RevitVersion).*">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="$(RevitVersion).*">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```
Build: `dotnet build -c Release`. Target another Revit year by editing `RevitVersion` — the TFM, the
API packages and the output folder all follow from it. There is no per-year build configuration and
none is needed: to build for another year without touching the file, pass the property on the
command line, where it overrides the one in the csproj:

```
dotnet build -c Release -p:RevitVersion=2026
dotnet build -c Release -p:RevitVersion=2027
```

Each build lands in its own `<year>\` folder, so the years accumulate side by side — run one command
per year you ship.

> **Critical:** the host owns `AnalyseTool.Sdk.dll`, the Revit API, and `Newtonsoft.Json`. The
> extension's load context shares the host's copies (type identity), so **do not ship copies of those
> DLLs**. With the NuGet package this is automatic. Deploy only your DLL + `plugin.json` (+ assets).

---

## 5. Script extension — NOT the default, read §1 first

Scripts exist for the machine-authored path: an agent trying something out over MCP, the
**Save as command** flow that promotes a working AI snippet into a permanent one, and code
embedded into the host. They are **not** the way to hand an extension to a user — there is no
build, so there is no compile error until Revit loads it, and no year folders, so there is no way
to say which Revit versions it supports.

Generate one only when the request explicitly asks for a script, or when you are the agent running
the code yourself. For anything a person will install, generate the C# project from §4.
If you ARE that agent — connected over MCP — §7.2 has the commands to run, save, read back and
diagnose one without a human copying files.

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

### Your page is a separate application

**`window.AT` is the ENTIRE contract.** Your page runs in its own WebView, its own document, its own
bundle. The host injects the bridge and nothing else: no component library, no theme, no stylesheet,
no global registrations, no CSS variables.

So the page must bring everything it renders with:

- **import every UI component you use, in the file that uses it.** Do not assume a component is
  registered globally — a component that is not registered does not throw, it renders *nothing*, and
  the result is a page that looks half-built with a clean console;
- **ship your own stylesheet**, including a page background. Without one you inherit the WebView
  default, which is not the host's;
- **pick your own theme.** Matching the host visually is fine and welcome — reading its settings is
  not possible and copying its setup file is a dependency that breaks silently when it changes.

This is the frontend half of the rule the C# side already states: a command sees only the SDK, and a
page sees only `window.AT`.

---

## 7. Deploy & reload

```
%LOCALAPPDATA%\AnalyseTool\extensions\<id>\
    plugin.json
    2025\<YourExt>.dll   (C# — one folder per Revit year)
    *.cs                 (script)
    index.html           (UI)
    icon.png             (optional)
```
- The extension folder sits DIRECTLY under a source root — the Revit year is a subfolder INSIDE it,
  never above it. This is the same layout a published package has, so the folder you develop in is
  the one you zip, and builds for several Revit years sit side by side.
- The host picks the running year's build and falls back to a DLL in the folder root, so a hand-made
  single-year extension works without year folders. Scripts and UI are version-independent and
  always live in the root.
- Changed code/manifest → **Reload** (AnalyseTool tab → Settings → Reload). No restart.
- A brand-new ribbon button needs a **Revit restart** the first time.

### 7.0 Migrating an extension from the OLD layout

If you are asked to update an existing extension, check which layout it uses first. The old one put
the Revit year **above** the extension; the current one puts it **inside**:

```
OLD (deprecated, still loads)          NEW
extensions\2025\acme.doors\            extensions\acme.doors\
    plugin.json                            plugin.json          <- ONE copy
    Acme.Doors.dll                         index.html           <- ONE copy
    index.html                             icon.png
extensions\2026\acme.doors\                2025\Acme.Doors.dll
    plugin.json      (duplicate)           2026\Acme.Doors.dll
    Acme.Doors.dll
    index.html       (duplicate)
```

Rules for the conversion — they cover every case:

1. **DLLs** move into a `<year>\` subfolder of the extension, one per Revit version.
2. **Everything else** (`plugin.json`, `*.cs` scripts, `index.html`, `ui/`, `icon.png`, assets) goes
   in the extension root, exactly ONCE. The old layout duplicated these per year; they are
   version-independent, so collapse them to a single copy. If the duplicates differ, the newest wins
   — say so rather than guessing silently.
3. **`plugin.json` needs no change.** The year folders are the version declaration. If an old
   manifest still carries a `targetRevit` field, DELETE it — it is not read.
4. **The csproj** gets `<OutDir>$(MSBuildProjectDirectory)\$(RevitVersion)\</OutDir>` and the
   `RevitVersion`-driven TFM/packages from §4, so the build writes into the right subfolder itself.
   Remove any post-build copy step that targeted `extensions\<year>\<id>\`.

Do NOT delete the old folder as part of the migration — leave that to the user; both layouts load,
so nothing breaks while both exist.

### 7.1 Publish (optional)

For C# extensions built against the `AnalyseTool.Sdk` NuGet package, the SDK ships the whole
publishing pipeline — no extra tooling:

```
dotnet build -t:PackExtension
```

builds the project for Revit 2025/2026/2027 (override with `-p:AnalyseToolPackYears=2025;2026`),
lays out the distribution bundle (per-year DLLs in year subfolders, `plugin.json`/UI at the root)
and zips it to `artifacts/<id>-<version>.zip` — exactly the format users install via Settings →
"Install from file…". Script/UI-only extensions need no build: zip the folder itself.

To publish on GitHub, add `.github/workflows/release.yml` — then publishing is `git tag v1.0.0 &&
git push --tags`, and `"updateFeed": "github:you/your-repo"` in plugin.json gives users update
notifications for free:

```yaml
name: Release
on:
  push:
    tags: ["v*"]        # the tag must match "version" in plugin.json
  workflow_dispatch: {} # or publish the branch as-is; the tag is derived from the manifest
permissions:
  contents: write
jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - id: manifest
        shell: pwsh
        run: echo "version=$((Get-Content plugin.json | ConvertFrom-Json).version)" >> $env:GITHUB_OUTPUT
      # On a tag push the tag is handed to PackExtension, which fails if it disagrees with plugin.json.
      - run: dotnet build -t:PackExtension -p:AnalyseToolExpectedVersion=${{ github.ref_type == 'tag' && github.ref_name || '' }}
      - uses: softprops/action-gh-release@v2
        with:
          tag_name: v${{ steps.manifest.outputs.version }}
          files: artifacts/*.zip
```

**`plugin.json` owns the version.** It travels inside the package and is what the installed
extension reports; a git tag exists only in the repository. So bump `version` there, and let the
tag follow. Publish ONE package per release: re-running a workflow EDITS the existing release
rather than replacing it, so a second zip just piles up next to the first, and the update feed
then refuses to guess which one is the package.

---

### 7.2 Doing all of this yourself (over MCP)

Everything above assumes a person copies files and clicks Reload. If you are connected over MCP you
can run the whole loop, because the host exposes it as commands.

**Gate first.** All of this requires **C# code execution** to be ON in AnalyseTool Settings. While it
is off these tools are not merely refused — they are not in `tools/list` at all, so if you cannot see
them, ask the user to turn the setting on. Only a person can: the command that flips it is hidden
from MCP on purpose, so you cannot grant yourself code execution.

The loop, and what each step answers:

| Step | Command | What comes back |
| --- | --- | --- |
| Learn the rules | `GetAuthoringGuide` | This document. Call it first — it is not otherwise reachable over MCP, and everything below assumes it. |
| Try it | `ExecuteRevitCode { code, description? }` | The snippet's own return value. Nothing is persisted. On a compile failure, `{ error, diagnostics }` — read the diagnostics and fix the code; no human relays them. |
| Keep it | `SaveAsCommand { code, id, name, … }` | `{ ok, created, command, directory, error, diagnostics, warnings }` |
| Give it a form | `SaveExtensionUi { id, name, files, … }` | `{ ok, id, directory, entryHtml, files, error }` |
| Tidy the ribbon | `UpdateExtensionManifest { id, name?, tab?, panel?, removeButton? }` | The rewritten manifest |
| Read it back | `GetScriptSource { id }` | `{ ok, id, directory, files: [{ name, content }] }` — script extensions only |
| Find out why | `GetExtensionDiagnostics` | Per extension: `kind`, `zone`, `enabled`, `compatible`, `error` if it failed to compile or load, and `shadowedBy` when another folder claimed the same id first |
| Apply | `ReloadExtensions` | Only needed for changes made another way — the save commands reload by themselves |

`SaveAsCommand` takes either form from §5 — a bare body it wraps into a named class, or a full
`IRevitTask` you wrote. It **compiles before writing**, so code that does not build never reaches
disk, and it names the button after the command the dispatcher will actually register. `tab` and
`panel` place the button; `readOnly` / `destructive` become the command's own metadata.

**To refine a command you already made, pass `overwrite: true`** — that is the difference between
generating a command and being able to improve it. Read the current source with `GetScriptSource`
first rather than rewriting from memory. Overwrite only replaces a folder this command created
(`Command.cs` + `plugin.json` and nothing else); anything else is refused, so you cannot flatten
someone's hand-built extension by picking its id.

**A save for an existing id goes to that extension's OWN folder** — leave `targetRoot` empty and it
resolves there, wherever it is. This matters most for a script that did not come from this machine: a
team keeps its scripts in a shared folder, everyone adds it as a source, and fixing one has to fix
THAT copy. Naming a different root instead leaves the broken original where the team can see it and
adds a second folder with the same id, one of which then silently wins.

So when the result's `directory` is not the user's own folder, **say so**: a fix in a shared folder is
everyone's fix, and that is their call to make, not yours.

**A command WITH a form** is two saves into the SAME `id`:

1. `SaveAsCommand { id: "niko.sheets", name: "Create sheets", code: … }` — the C# that does the work
   and returns JSON. Note the `command` it reports back, e.g. `niko.sheets.CreateSheets`.
2. `SaveExtensionUi { id: "niko.sheets", files: [{ name: "index.html", content: … }] }` — the page,
   which calls that command with `AT.invoke("niko.sheets.CreateSheets", { … })`.

**The ribbon button follows one rule: if the extension has a page, the button OPENS the page;
if it has none, the button RUNS the command.** So save the page second and the button opens the
form — the host decides by whether `ui.button.command` is set, and adding a page clears it. Both
save commands MERGE into the existing `plugin.json` rather than replacing it, so neither erases the
other's half (nor any vendor metadata already there).

Files are written flat into the extension folder, so `SaveExtensionUi` takes hand-authored
HTML/CSS/JS — plain `window.AT.invoke` as in §6, no build step. A framework project with an
`assets/` tree is not this: that is a folder a person builds and installs.

**One extension, many commands — do this by default.** Each extension gets at most ONE ribbon button
(the host keys them by extension id), so saving ten commands as ten extensions puts ten buttons on
the ribbon. Roslyn compiles every `.cs` in a folder, so the folder was never the limit:

- Pass `fileName: "CreateSheets.cs"` to put another command into an extension that already exists.
  They compile together and share the extension's id, so their wire names are
  `niko.sheets.CreateSheets`, `niko.sheets.RenumberSheets`, and so on.
- Pass `button: false` for commands that should not each get a button — the extension keeps the one
  it has, and every command stays callable from MCP and from JS regardless.
- `UpdateExtensionManifest { id, removeButton: true }` takes an extension off the ribbon entirely
  without touching its code, for a ribbon that has already collected too much.

`overwrite` is asked of the FILE, not the folder: adding a second command is not overwriting the
first, so only re-saving the same `fileName` needs it.

**A command with no button is not a command nobody can run.** The host's **Scripts** button opens a
dockable launcher listing the generated script commands that stand on their own — so a command saved
with `button: false` is still one click from being run, and that is why saving without a button costs
nothing.

Two kinds of command are deliberately absent, both for the same reason — they already have a front
door, and a second one would only skip it:

- **A compiled extension's commands.** It ships its own page and its own ribbon button.
- **The commands behind a page you saved.** Once `SaveExtensionUi` gives an extension an entry page,
  its ribbon button opens that page, and the commands become the page's backend. A form that needs
  two `IRevitTask`s — one to fetch, one to apply — is ONE tool with two steps, and listing the steps
  as two scripts both misdescribes it and invites someone to run "apply" without "fetch".

So the shape of what you build decides where it appears, and both shapes are complete: **commands
only** → they are rows in the launcher, each with a form built from its schema; **commands plus a
page** → one ribbon button that opens the page you designed. What you must not do is assume a
half-built tool is visible: if you have saved the C# and intend to add a form, the commands are
listed until the page lands and then they are not, which is correct rather than a regression.

The launcher BUILDS THE FORM FROM `inputSchema` — the practical reason to declare `InputType` on a
generated command even when nothing chains it. A command that declares one opens its own page with
typed fields and a Run button; a command that declares none has no form to show, and its ▶ simply
runs it. So `InputType` is the difference between a command a person can drive and one they can only
trigger.

**Where a command lives is the user's call, not yours.** Every row in that launcher has a pin that
moves its command onto the ribbon or back off it, and it works for any command — including one from
an extension whose manifest you must never write. So `button` and `removeButton` set the DEFAULT a
command arrives with; the user overrides it afterwards and their override wins. Do not argue with a
ribbon they have already arranged: never flip `button` on an existing command just to make it easier
to find, and say "it is in the Scripts launcher, pin it if you want a button" instead.

A pinned button knows the difference between a command it can run and one it cannot: no `InputType`
means the click runs it, and an `InputType` means the click opens the launcher with the form already
on screen. Another reason a generated command should declare one.

**Where saves land is also the user's call.** With no `targetRoot`, `SaveAsCommand` and
`SaveExtensionUi` write into the folder chosen in Settings → Extension paths (tagged `scripts`),
which is the built-in dev root until the user picks another. Leave `targetRoot` empty unless the user
names a folder — passing the default explicitly overrides a choice they made on purpose.

Two things to expect:

- **`warnings` in the save result.** A full class that declares no `InputType` / `OutputType` saves
  and works, but is opaque over MCP — nobody can tell what to send it or what it returns. Fix it and
  save again with `overwrite: true`. (A wrapped bare body gets no such warning: it takes no arguments
  and returns whatever the body returns, so there is nothing to declare.)
- **A brand-new ribbon button needs one Revit restart** to appear, as in §7. The command itself is
  callable immediately after the reload — you do not have to wait to test it.

---

## 8. Rules — ALWAYS / NEVER

- **ALWAYS** generate a compiled C# project (`.dll`). A script (§5) is only for an explicit request
  or for code you run yourself as an agent — never for an extension a user will install.
- **ALWAYS** touch the Revit model only inside `RunInRevitAsync`. Open transactions there.
- **ALWAYS** use a lean input record for `InputType` (only the fields the caller sends) and put a
  `[System.ComponentModel.Description("…")]` on each field. Do **not** reuse rich/nested domain models —
  the generated schema balloons.
- **ALWAYS** declare `OutputType` too (SDK 1.2+). A command that declares neither is callable but
  opaque over MCP — free-form arguments, free-form answer — so it cannot be chained and its payload
  cannot be checked. If `ExecuteAsync` returns an anonymous object, promote it to a named record
  first: that refactor IS the work, the attribute is the easy half.
- **NEVER** ship copies of `AnalyseTool.Sdk` / Revit API / `Newtonsoft.Json` in the extension output.
- **NEVER** touch the WebView, sockets, or threads from a command — return a serializable object.
- **Category names are language-specific.** On a German Revit a wall's category is `"Wände"`, not
  `"Walls"`. Don't hard-code English names: call `GetModelOverview` — it reports the UI language, the
  display units and a per-category INSTANCE count in one call, so it also tells you which categories
  actually hold geometry — or `GetCategoriesInRevit` for the full list. In your own code prefer
  `BuiltInCategory` (`OST_Walls`), which means the same thing on every install.
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
- **Existing AI commands** (all `HiddenFromMcp`, run Ollama on the host). Pass
  `{ model, prompt, … }`; `model` is the shared model name and is **required** — there is no default.
  Each returns its failure as DATA in an `error` field rather than throwing, so always read `error`
  before the payload:
  - `OllamaAnalyse` → `{ analysis, error }` — free-text analysis. It **streams**: call it with
    `AT.invoke(cmd, payload, { onProgress })` and append `p.message`, which carries the generated text
    as it arrives (`p.fraction` stays 0 — token generation has no honest total). The returned
    `analysis` is authoritative; replace what you streamed with it when the call finishes.
  - `OllamaEditParameters` → `{ edits: [{ elementId, parameter, oldValue, newValue, reason }], raw, error }`
    — proposed edits only; apply them yourself with `SetDataToParameters`.
  - `OllamaSuggestName` → `{ name, error }` — one new name from a current name + instruction.
- **Cancelling a long AI call:** pass an `AbortSignal` — `AT.invoke(cmd, payload, { signal })`. The
  host cancels the model call itself, and the command comes back with `error: "Cancelled."`.
- In your **own** C# AI command: take the model name in the payload, and run the AI/HTTP call **outside**
  `RunInRevitAsync` (see §2 — slow I/O must not block the Revit thread); marshal only the model touch.

---

## 10. Checklist for a generated extension

- [ ] A compiled C# project — not a script — unless a script was explicitly requested (§1, §5).
- [ ] `plugin.json` with `id` (+ `entryAssembly` for C#, or none for UI-only).
- [ ] The csproj builds into `<extension>\<year>\` and derives its TFM from `RevitVersion` (§4).
- [ ] C#: one or more `IRevitTask` classes; model access only in `RunInRevitAsync`.
- [ ] `[RevitCommand]` with a clear `Description`; `ReadOnly`/`Destructive` set correctly; `InputType`
      for commands that take arguments; `OutputType` for what they return (SDK 1.2+).
- [ ] UI: `index.html` calling `window.AT.invoke(...)`; `base: "./"` if framework-built.
- [ ] Tell the user the deploy path and that they click **Reload** (or restart for a new button).
