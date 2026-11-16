# AnalyseTool — AI instructions for writing extensions

> **How to use this file:** paste it into Claude / ChatGPT as context, then ask it to build an
> AnalyseTool extension (e.g. "write a command that renumbers selected doors"). It contains the full
> contract, the manifest schema, the rules, and worked examples — everything the model needs to
> generate a correct extension in one shot.

You are helping write **extensions for AnalyseTool**, a Revit 2025/2026/2027 add-in. Extensions add
functionality **without rebuilding the host**: an extension is a folder of C# sources (and, if it wants,
an HTML page) that AnalyseTool compiles inside Revit. Saving a file reloads the extension by itself; every
folder has its own version history (git) that the user controls from the AnalyseTool window.

---

## 1. What to generate

| Kind | Ships | Role | Use it |
| --- | --- | --- | --- |
| **C# sources** | `.cs` files in `src\` that AnalyseTool compiles (§5) | **ADDS** commands | **always — this is the default** |
| **JS / UI** | an HTML page | **CONSUMES** commands via `AT.invoke(...)` | when the extension needs a page |
| **C# project** | a `.dll` per Revit year the author builds (§4) | ADDS commands | for NuGet packages, closed source, or XAML/resources |

**The principle:** C# extensions *add* commands to a shared pipeline; JS pages *consume* them.
One folder can be C#-only, UI-only, or both.

> **Sources are the default since AnalyseTool 2.0.** «New button» in Revit (or `CreateExtension` over MCP)
> creates `src\<Name>.cs` and `"build": "host"` in `plugin.json`; AnalyseTool compiles it with its own Roslyn,
> against the running Revit's API — no .NET SDK, no project, no DLL in the folder. The diff of a version is then
> real code, one source serves every Revit year, and a saved edit runs a moment later. Ship a DLL only for what
> sources cannot do: NuGet packages, closed code, XAML/resources.

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
    // AT.invoke(cmd, payload, { onProgress: p => ... }) — p = { fraction, message }. Over MCP the same
    // reports reach the AI client as notifications/progress, and a command that runs longer than a
    // minute is handed back to the client as a job it collects with GetJobResult — so report progress:
    // an agent that sees movement waits instead of retrying.
    //
    // RULE FOR ANYTHING THAT MAY TAKE MORE THAN A FEW SECONDS: work in CHUNKS. One RunInRevitAsync per
    // chunk (say 50–200 elements), one Transaction per chunk, Progress?.Report between chunks. A single
    // RunInRevitAsync holding one transaction for minutes freezes Revit for the person at it, cannot be
    // cancelled until it ends, and shows no progress; chunks keep Revit responsive, let Cancel act
    // between them, and cost nothing you cannot undo: wrap the chunks in a TransactionGroup and
    // Assimilate() it at the end for ONE undo step. (Measured: 7,200 elements in 40-cell chunks took
    // 131 s with a live window; in one transaction 189 s with Revit frozen throughout.)
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
| `icon` | — | Extension-level PNG (relative path) for listings; falls back to `ui.button.icon`. A path that leaves the extension folder (`..\`, an absolute path) is ignored. |
| `ui.button.icon` | — | PNG beside `plugin.json` (a path that leaves the folder is ignored), **or** `glyph:E8A9` — a Segoe MDL2 Assets code point, the same source the host's own buttons use. Crisp at any DPI and nothing to ship. No icon at all draws a letter. |
| `updateFeed` | — | Update source: an HTTPS URL returning `{version, downloadUrl}`, or `github:owner/repo` (latest release, zip asset). Only for published extensions. |
| `entryAssembly` | — | DLL name. **Omit** for UI-only extensions. Resolved in the Revit-year subfolder first (`2025\`), then the folder root. For a host-built folder (§5) that has none, the host sets it to `<id>.dll`. |
| `ui` | — | **Omit** for a command-only extension (callable from JS/MCP but no button). |
| `ui.entryHtml` | — | Page to open. Default `index.html`. |
| `ui.tab` / `ui.panel` | — | Ribbon placement. Default tab `"AnalyseTool"`, panel `"Extensions"`. |
| `ui.button.name` | — | Button label (also the display name). |
| `ui.button.command` | — | If set, clicking the button **runs this command** (shows the result in a dialog) instead of opening the HTML page. Use for command-only extensions that want a button. |
| `ui.button.ribbon` / `ui.button.pane` | — | `false` keeps the button off the Revit ribbon (a tile of the AnalyseTool pane only) or out of the pane (ribbon only). Both default to on. Use `"ribbon": false` for buttons used now and then, so the ribbon stays short. |
| `ui.button.args` | — | The arguments the click passes to `command` — the JSON a page would send with `AT.invoke`, e.g. `{ "category": "OST_Doors" }`. Any pipeline command works, AnalyseTool's own too: `"command": "OpenWindow", "args": { "route": "/statistics" }`. AnalyseTool declares its own ribbon buttons exactly this way. |
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
| `order` | Sort order in the panel: lower first; equal values keep declaration order; 0 (unset) goes after every stated value. Applies to large and small buttons alike, across extensions. |
| `kind` | `push` (default) · `stacked` · `pulldown`. Unknown values fall back to `push`. |
| `items` | Entries of a `pulldown`. Ignored for other kinds. |
| `name` / `tooltip` / `icon` / `command` / `args` | As in the single-button form. Every click is counted for the Statistics page by extension id and button name. |

**`stacked`** — a small button. The host lays ALL small buttons of a panel into columns of three, the
shape Revit's own stacked items make — across extensions, ordered by `order`, then extension id,
then declaration, after the panel's large buttons. So one small button from each of two extensions
stands in one column, not two. Placement comes from the first entry of a run, because a column
cannot straddle two panels.

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

## 5. C# sources — AnalyseTool builds them (the default)

```
<id>\
    plugin.json            id, buttons, "build": "host"
    src\CountWalls.cs      the sources — every .cs under src\ compiles into ONE assembly
    lib\*.dll              optional: further assemblies the sources reference
    .editor\<id>.csproj    optional, git-ignored: IntelliSense for VS / VS Code / Rider only
    bin\2025\<id>.<hash>.dll   the build, written by AnalyseTool (git-ignored)
```

- **No .NET SDK, no NuGet, no build step.** References: the Revit API of the running Revit,
  `AnalyseTool.Sdk`, `Newtonsoft.Json`, the .NET framework and `lib\*.dll`. The implicit usings of an
  SDK-style project (`System`, `System.Linq`, `System.Collections.Generic`, `System.IO`, `System.Threading.Tasks`…)
  are on.
- **Save = reload.** AnalyseTool watches the folder: a saved `.cs` (or `plugin.json`) is compiled and the
  commands replaced a moment later — the button runs the new code. The build is cached by a hash of the
  sources, so a start of Revit with unchanged code compiles nothing.
- **A compile error keeps the last good build running** and is reported with file, line and column
  (the extension page in AnalyseTool, `BuildExtension` / `GetExtensions` over MCP).
- **`"build": "host"`** in `plugin.json` makes the folder a source extension even with a `.csproj` beside it
  (the `.editor` project). Without the field the rule of 1.x applies: sources and no `.csproj` → host-built.

Two forms are accepted:

**Body form** (only when `src\` holds a single file without any type declaration) — just statements;
`uiapp` / `uidoc` / `doc` are in scope, `return` any object. `Autodesk.Revit.DB`, `Autodesk.Revit.UI` and the
implicit usings are imported; `using` directives on the FIRST lines are lifted above the class.
```csharp
var walls = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().GetElementCount();
return new { walls };
```
It is wrapped into a class named `Script`, so the command is `<id>.Script`.

**Class form** — one or more full `IRevitTask` classes (as in §2), for your own command names,
`InputType`/`OutputType` and metadata. Prefer it; it is what «New button» creates.

---

## 6. JS / UI extension

The host opens your `index.html` in a WebView and injects `window.AT`. Any framework works.

```js
// Call any registered command (built-in or from any extension). Returns a Promise.
const res = await window.AT.invoke("acme.doors.CountDoors", /* optional payload */ {});

// Discover the extensions and their commands (ids, descriptions, load errors):
const { extensions } = await window.AT.invoke("GetExtensions");
```
- `invoke` is id-correlated, so concurrent calls are fine; it **resolves** with the result and
  **rejects** with an `Error` whose `message` says why and whose `code` is the error code (`not_found`, `failed`, …).
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

## 7. Folders, reload and versions

```
%LOCALAPPDATA%\AnalyseTool\extensions\<id>\       your own extensions (or any folder added in Settings)
%LOCALAPPDATA%\AnalyseTool\extensions-dist\<id>\  installed packages (.zip)
    plugin.json
    src\*.cs             (sources — AnalyseTool builds them, §5)
    2025\<YourExt>.dll   (or a DLL per Revit year, §4)
    index.html           (UI)
    AGENTS.md, CLAUDE.md (written by «New button»: they point to this guide, LLM.md next to the add-in)
    .git\                (the extension's own version history)
```
- The extension folder sits DIRECTLY under a root — a Revit year is a subfolder INSIDE it, never above it.
- **No Reload needed:** a changed file reloads its extension, and ribbon buttons appear, change and disappear
  without restarting Revit.
- **Versions.** Each extension folder is a git repository. The user saves a version with a message, sees the
  history and the diff, restores a version (or one file — unsaved work is saved as a version first, so nothing
  is lost) and can **pin** a source extension to a version: the button runs that version's code whatever the
  files say. Over MCP: `SaveExtensionVersion`, `GetExtensionVersions`, `GetExtensionDiff`,
  `RestoreExtensionVersion`, `PinExtensionVersion`. Do not run git yourself in the folder unless asked; never
  commit `bin\` or `.editor\`.

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
2. **Everything else** (`plugin.json`, `index.html`, `ui/`, `icon.png`, assets) goes
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

### 7.0a Releasing from GitHub

A C# extension created by the template ships `.github/workflows/ci.yml`. It builds every Revit year
and, on a `v*` tag, publishes the packed zip as a GitHub Release — the same zip
`updateFeed` points at, so tagging IS the release:

```
dotnet build -c Release -t:PackExtension     # the same thing, locally
git tag v1.0.0 && git push --tags            # and on GitHub, with a Release attached
```

The tag must match `version` in `plugin.json`; the workflow fails the build otherwise, because a
mismatch would leave the update badge in the Extensions window permanently wrong. Delete the workflow if you
distribute another way — nothing else depends on it.

### 7.1 Publish (optional)

For C# extensions built against the `AnalyseTool.Sdk` NuGet package, the SDK ships the whole
publishing pipeline — no extra tooling:

```
dotnet build -t:PackExtension
```

builds the project for Revit 2025/2026/2027 (override with `-p:AnalyseToolPackYears=2025;2026`),
lays out the distribution bundle (per-year DLLs in year subfolders, `plugin.json`/UI at the root)
and zips it to `artifacts/<id>-<version>.zip` — exactly the format users install via Extensions →
Install → "From a file". UI-only extensions need no build: zip the folder itself.

To publish on GitHub, add `.github/workflows/release.yml` — then publishing is `git tag v1.0.0 &&
git push --tags`, and `"updateFeed": "github:you/your-repo"` in plugin.json gives users update
notifications for free:

Once it is published, the repository itself is the install source: users reach it through Extensions
→ Packages (the shipped list, or their own `%LOCALAPPDATA%\AnalyseTool\catalog.json`), or by pasting the
repository into Install → "From a repository". Both routes download the zip from your release.

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

Connected over MCP you run the whole loop yourself — AnalyseTool exposes it as tools:

| Step | Tool | What comes back |
| --- | --- | --- |
| Learn the rules | `GetAuthoringGuide` | This document. |
| See what exists | `GetExtensions` | Every extension: id, folder, kind (`Source`/`Dll`/`Page`), commands, load errors, compiler diagnostics, pinned version. |
| Start a folder | `CreateExtensionFolder { name, description?, panel? }` | An empty folder (extension) with its history; buttons come next. |
| Add a button | `AddExtensionButton { id, name, description?, template? }` | `src\<Name>.cs` (and/or a page) and the entry in `plugin.json`, saved as a version. |
| Start a button | `CreateExtension { name, description?, template?, panel? }` | The new extension with its folder; `src\<Name>.cs` compiles, the first version is saved, the ribbon button is there. |
| Read / write code | `ListExtensionFiles`, `ReadExtensionFile`, `WriteExtensionFile { id, path, content }` | Writing reloads at once; the answer carries the compiler diagnostics. |
| Check it | `BuildExtension { id }` | `success` and diagnostics with file/line/column. |
| Try it | the command itself (`<id>.<Command>`), or `ExecuteCode` for a throwaway snippet | Its result. |
| Keep it | `SaveExtensionVersion { id, message }` | The new version (null when nothing changed). |
| History | `GetExtensionVersions`, `GetExtensionDiff { id, from?, to? }`, `RestoreExtensionVersion`, `PinExtensionVersion` | Versions, diffs, restore and pin. |

- **One extension, many commands — do this by default.** One folder holds as many `IRevitTask` classes as
  you like; they share the id: `niko.sheets.CreateSheets`, `niko.sheets.RenumberSheets`.
- **A command that takes input belongs behind a page.** A ribbon click has no arguments: give such a command
  a page (`template: CommandWithPage`) that collects the input and calls it with `AT.invoke`.
- **Shared folders are shared.** When an extension's folder is not the user's own (a network folder added in
  Settings), say so before changing it: a fix there is everyone's fix.
- Save a version after a change that works, with a message that says what changed — that is what the user
  will scroll through when something breaks.

---

## 8. Rules — ALWAYS / NEVER

- **ALWAYS** write C# sources in `src\` (§5) unless the extension needs NuGet packages, closed code or
  XAML/resources — then a C# project (§4).
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

## 9. AI features

AnalyseTool has **no AI of its own**. The AI is the client the user connects over MCP (Claude,
Cursor…), and it reaches Revit through the commands — built-in and from extensions. So an extension
does not need a model picker or AI settings: make its commands well-described (`Description`,
`InputType`, `OutputType`) and the connected assistant can use them.

If your own C# command calls an AI or any HTTP service itself, take what it needs (endpoint, key,
model) in the payload or from your own settings file, and run the call **outside** `RunInRevitAsync`
(see §2 — slow I/O must not block the Revit thread); marshal only the model touch.

---

## 10. Checklist for a generated extension

- [ ] C# sources in `src\` with `"build": "host"` (§5); a C# project only for NuGet / closed code / resources.
- [ ] `plugin.json` with `id` (+ `entryAssembly` for C#, or none for UI-only).
- [ ] The csproj builds into `<extension>\<year>\` and derives its TFM from `RevitVersion` (§4).
- [ ] C#: one or more `IRevitTask` classes; model access only in `RunInRevitAsync`.
- [ ] `[RevitCommand]` with a clear `Description`; `ReadOnly`/`Destructive` set correctly; `InputType`
      for commands that take arguments; `OutputType` for what they return (SDK 1.2+).
- [ ] UI: `index.html` calling `window.AT.invoke(...)`; `base: "./"` if framework-built.
- [ ] Tell the user where the folder is; a saved file reloads by itself, and the button appears without a restart.
