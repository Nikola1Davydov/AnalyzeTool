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
| **C# sources** | `.cs` files in a button folder that AnalyseTool compiles (§1.1, §5) | **ADDS** commands | **always — this is the default** |
| **JS / UI** | an HTML page | **CONSUMES** commands via `AT.invoke(...)` | when the extension needs a page |
| **C# project** | a `.dll` per Revit year the author builds (§4) | ADDS commands | only for NuGet packages with dependencies, native libraries, embedded resources or closed code — decide with §4.1 |

**The principle:** C# extensions *add* commands to a shared pipeline; JS pages *consume* them.
One folder can be C#-only, UI-only, or both.

> **Sources are the default since AnalyseTool 2.0.** «New button» in Revit (or `CreateExtension` over MCP)
> creates a button folder (§1.1) with `<Class>.cs` in it; AnalyseTool compiles it with its own Roslyn,
> against the running Revit's API — no .NET SDK, no project, no DLL in the folder. The diff of a version is then
> real code, one source serves every Revit year, and a saved edit runs a moment later. Ship a DLL only for what
> sources cannot do: NuGet packages, closed code, XAML/resources.

### 1.1 Where a button lives — tabs, panels, button folders (the default)

New buttons are laid out as pyRevit does it: the folders ARE the ribbon.

```
%LOCALAPPDATA%\AnalyseTool\extensions\
    Sheets.tab\                     a ribbon tab (a folder of the AnalyseTool pane)
        bundle.json                 optional: { "name": "Sheets", "layout": ["Numbering.panel", "Output.panel"] }
        _shared\*.cs                optional: C# every button of the tab compiles with
        Numbering.panel\            a panel (a section of the folder in the pane)
            bundle.json             optional: { "name": "Numbering", "layout": ["Renumber.pushbutton", "More.pulldown"] }
            Renumber.pushbutton\    ONE button — everything it needs, and nothing else
                bundle.json         { "name", "tooltip", "icon", "command": "Renumber" | "page": "index.html", "dockable" }
                Renumber.cs         its C# (any *.cs in the folder, subfolders too)
                index.html          its page, when it has one
                AGENTS.md, CLAUDE.md, .gitignore
                .git\               its own version history
            More.pulldown\          a pulldown: its buttons are *.pushbutton folders inside
```

- **A button folder is an extension of its own**: id `<tab>.<button>` (`sheets.renumber`), command
  `<tab>.<button>.<Class>` (`sheets.renumber.Renumber`) — in `AT.invoke` too. Its own build, its own versions.
- `bundle.json` of a button is the button entry of §3 with two differences: `command` is the **class name only**, and
  a page is `"page"` (not `entryHtml`). The tab and the panel are the folders it sits in — never a field.
- **Order**: `layout` lists folder names (or names); what it does not list follows alphabetically. Without any
  `bundle.json` the folder names are the names.
- Moving a button = moving its folder (the pane does it, with its history). Into another tab its id changes, and
  AnalyseTool rewrites the `AT.invoke` names in its pages.
- **The 1.x layout stays**: a folder with a `plugin.json` (§3) is read as before, as one folder of the pane with all
  its buttons (in the pane: a section of «Older buttons»). To move one of its buttons into a tab, call
  `MoveExtensionButton` with `toId` = the tab id (`sheets.tab`): it becomes a button folder with its C#, its page
  (as `index.html`) and a new history. Everything below about `plugin.json`, `src\` and versions applies to it; for a button folder, read
  «the extension folder» as «the button folder».

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
    // progress. It only shows between SEVERAL RunInRevitAsync calls: inside one call Revit's thread is
    // busy and nothing reaches the page until it returns. The host sets Progress before ExecuteAsync (null when nobody listens); from JS use
    // AT.invoke(cmd, payload, { onProgress: p => ... }) — p = { fraction, message }. Over MCP the same
    // reports reach the AI client as notifications/progress, and a command that runs longer than a
    // minute is handed back to the client as a job it collects with GetJobResult — so report progress:
    // an agent that sees movement waits instead of retrying.
    //
    // EVERY Transaction AND TransactionGroup STARTS AND ENDS INSIDE ONE RunInRevitAsync. Each call is
    // one external event of Revit, and Revit rolls back whatever is still open when the event returns:
    // a TransactionGroup started in one call and Assimilate()d in a later one throws «The Transaction
    // group has not been started». No group, no transaction spans two calls — ever.
    //
    // So choose, per command:
    // - ONE undo step (most commands: rename, renumber, set parameters): ONE RunInRevitAsync with one
    //   Transaction (or a TransactionGroup with several transactions inside it, assimilated before the
    //   lambda returns). Fine up to a few thousand elements — a few seconds.
    // - Work of minutes: CHUNKS. One RunInRevitAsync per chunk (say 50–200 elements), one Transaction per
    //   chunk, Progress?.Report(new ProgressInfo(done/total, "…")) and the cancellation check between
    //   them. Revit stays responsive, Cancel acts between chunks, the bar moves — and every chunk is an
    //   undo step of its own: say so to the user (or make the command idempotent, so running it again
    //   finishes the job). One transaction for minutes freezes Revit and cannot be cancelled.
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

## 3. The manifest — `plugin.json` (DLL extensions, packages; a button of a tab has `bundle.json`, §1.1)

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
| `ui.dockable` | — | `true` = a **small interface**: the page opens inside the AnalyseTool pane (a screen with ‹ back to its folder), in a frame that provides `AT.invoke`. Without it (the default) a page opens in its own window. Rule: a few fields and a button → `dockable: true`; a big table, a report, a comparison → a window. A command that only needs a few values needs no page at all: give it an `InputType` and the pane builds the form from its schema. |
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

## 4. A DLL extension — when sources are not enough

### 4.1 Sources or a DLL? Decide first

"Complex" alone is NOT a reason for a DLL. A button folder (§5) takes many classes and files, a page with its
own commands (§6), helper code in `_shared\` of the tab, and third-party DLLs in `lib\` — no project, no .NET SDK,
and every saved edit runs a moment later. Take a DLL extension only when one of these holds:

| Need | Why sources cannot | |
| --- | --- | --- |
| A NuGet package with dependencies (ClosedXML, EPPlus, a database driver…) | sources compile against a fixed set; `lib\` takes single DLLs, not a dependency tree | DLL |
| Native libraries (SQLite, SkiaSharp…) | only a build lays out `runtimes\win-x64\native\` | DLL |
| Embedded resources, images or data compiled into the assembly | sources have no resources | DLL |
| Closed code, or a team that builds and tests in Visual Studio / Rider with a debugger | sources are the code itself | DLL |
| One or two extra libraries without dependencies of their own | — | sources + `lib\*.dll` |
| Many commands, a big page, shared helpers | — | sources (several classes, `_shared\`) |

Not sure → sources. A DLL extension is a `plugin.json` folder (§3): it is NOT a tab of button folders, it names its
tab in `ui.tab`, and in the AnalyseTool pane it is a folder of its own.

### 4.2 The folder

The project lives IN the extension folder, next to `plugin.json` — the folder you develop in is the folder you
ship (`PackExtension`, §7.1):

```
%LOCALAPPDATA%\AnalyseTool\extensions\acme.doors\
    plugin.json          id, version, "build": "author", "entryAssembly": "Acme.Doors.dll", ui.tab / ui.panel / ui.buttons
    Acme.Doors.csproj    the project (§4.3)
    Commands\*.cs        your code: every IRevitTask class is a command acme.doors.<Class>
    index.html           optional: a page (§6)
    icon.png             optional: the icon of a button
    2025\Acme.Doors.dll  the build for Revit 2025, with its dependencies and Acme.Doors.deps.json
    2026\…  2027\…       one folder per Revit year you build
    .git\                its version history (the year folders ARE versioned: the DLLs are the extension)
```

`plugin.json` of a DLL extension:

```json
{
  "schema": 2,
  "id": "acme.doors",
  "name": "Doors",
  "version": "1.0.0",
  "description": "Door schedules and checks.",
  "build": "author",
  "entryAssembly": "Acme.Doors.dll",
  "ui": {
    "tab": "Acme",
    "panel": "Doors",
    "buttons": [
      { "name": "Count doors", "tooltip": "Counts the doors of the model.", "icon": "glyph:E8A1", "command": "acme.doors.CountDoors" },
      { "name": "Door table", "tooltip": "Opens the door table.", "icon": "glyph:E8A5", "entryHtml": "index.html", "dockable": false }
    ]
  }
}
```

`"build": "author"` says the DLL is yours: AnalyseTool loads `<year>\<entryAssembly>` and never compiles the folder.
No other project may sit below the folder (its sources would be swept into this one).

### 4.3 The project

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
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Acme.Doors</RootNamespace>
    <AssemblyName>Acme.Doors</AssemblyName>
    <!-- REQUIRED as soon as you use a NuGet package: a class library does not copy its packages to the
         output otherwise. This copies them and writes Acme.Doors.deps.json, which AnalyseTool's loader reads
         to find them (native ones in runtimes\win-x64\native\ too). Without it: FileNotFoundException in Revit. -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <!-- Build into <extension>\<year>\ — the layout a package uses, so the folder you develop in is
         the folder you ship, and builds for several Revit years coexist. -->
    <OutDir>$(MSBuildProjectDirectory)\$(RevitVersion)\</OutDir>
  </PropertyGroup>
  <ItemGroup>
    <!-- Exact version, never a range: a pinned package is the reason your build cannot be
         broken by someone else's release. -->
    <PackageReference Include="AnalyseTool.Sdk" Version="1.3.0">
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
    <!-- Your own packages: a normal reference, they are copied (EnableDynamicLoading) -->
    <PackageReference Include="ClosedXML" Version="0.104.2" />
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
> DLLs**. With the NuGet package this is automatic. Deploy only your DLL, its own dependencies, `plugin.json`
> (+ assets).

### 4.4 Build, load, check

1. Build for the Revit you test in (`dotnet build -c Release -p:RevitVersion=2025`). AnalyseTool watches the folder:
   a new DLL in `<year>\` reloads the extension by itself — no restart, Revit can stay open (the DLL is loaded from
   its bytes, never locked).
2. Check it loaded: `GetExtensions` over MCP (or the folder in the pane): `kind: "Dll"`, its `commands`, and
   `errors` — "No build for Revit 2026" means no DLL in that year folder; an Sdk error means a newer Sdk than the host.
3. Run a command: its button, or the command itself over MCP (`acme.doors.CountDoors`).
4. Save a version (`SaveExtensionVersion`) when it works — the year folders are part of it.

Over MCP there is no tool that creates a DLL extension: write `plugin.json`, the `.csproj` and the code with your own
file tools into `%LOCALAPPDATA%\AnalyseTool\extensions\<id>\` (or a folder of the user's roots), build with
`dotnet build`, then `GetExtensions`. `BuildExtension` and `CreateExtension` are for sources only.

### 4.5 What the loader does — and what that means for your code

- **Its own load context per extension**, collectible: a reload replaces it. `AnalyseTool.Sdk` and `Newtonsoft.Json`
  are always AnalyseTool's copies; the Revit API is Revit's; every other assembly is resolved by `<entry>.deps.json`,
  then looked up next to your DLL. Native libraries the same way (`runtimes\win-x64\native\`, or next to the DLL) —
  those are loaded from their file and stay locked until Revit closes.
- **Loaded from bytes**: `typeof(X).Assembly.Location` is EMPTY. Never find files "next to the DLL" by it — embed
  them as resources (`<EmbeddedResource Include="Data\*.json" />` + `GetManifestResourceStream`), or put them in the
  extension folder and let the page or the user pick them.
- **Static state dies with a reload**: keep caches in memory only for what you can rebuild.
- **No WPF window of your own on another thread**: a command returns data; for a user interface use a page (§6) —
  it opens inside the AnalyseTool pane (`dockable: true`) or in a window, and calls your commands with `AT.invoke`.
  A WPF window in code works on Revit's thread (inside `RunInRevitAsync`), but compiled XAML in a collectible
  context is untested — prefer a page.
- Debugging: attach Visual Studio / Rider to `Revit.exe`; the PDB next to the DLL is loaded with it.

---

## 5. C# sources — AnalyseTool builds them (the default)

```
Sheets.tab\
    _shared\*.cs                 optional: C# every button of the tab compiles with
    Numbering.panel\
        Renumber.pushbutton\
            bundle.json          name, tooltip, icon, "command": "Renumber" | "page": "index.html"
            Renumber.cs          its C# — every *.cs of the folder (subfolders too) and _shared\ compile into ONE assembly
            lib\*.dll            optional: further assemblies it references
            .editor\*.csproj     optional, git-ignored: IntelliSense for VS / VS Code / Rider only
            bin\2025\*.dll       the build, written by AnalyseTool (git-ignored)
```

An extension of `plugin.json` keeps its C# in `src\` instead (and `"build": "host"`); everything else below is the same.

- **No .NET SDK, no NuGet, no build step.** References: the Revit API of the running Revit,
  `AnalyseTool.Sdk`, `Newtonsoft.Json`, the .NET framework and `lib\*.dll`. The implicit usings of an
  SDK-style project (`System`, `System.Linq`, `System.Collections.Generic`, `System.IO`, `System.Threading.Tasks`…)
  are on.
- **Save = reload.** AnalyseTool watches the folder: a saved `.cs` (or `bundle.json`) is compiled and the
  commands replaced a moment later — the button runs the new code. A change in `_shared\` rebuilds every button of
  the tab. The build is cached by a hash of the sources, so a start of Revit with unchanged code compiles nothing.
- **A compile error keeps the last good build running** and is reported with file, line and column
  (the «!» on the tile in the pane, `BuildExtension` / `GetExtensions` over MCP).
- **Full classes only**: one or more `IRevitTask` classes (as in §2), with their command names, `InputType` /
  `OutputType` and metadata — what «New button» creates. A file of bare statements is not a command (it does not
  compile); for a throwaway snippet use `ExecuteCode` over MCP instead.

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
- **The names in the result are the C# names, as written** (Newtonsoft, no renaming): a result class
  `SheetListResult { string Title; List<SheetInfo> Sheets }` arrives as `{ "Title": …, "Sheets": [ { "Number": … } ] }`,
  an anonymous `new { title, count }` as `{ "title": …, "count": … }`. Read them in the page with exactly those names
  (`result.Sheets.map(…)`) — `result.sheets` is `undefined`. The payload you SEND is matched without regard to case.
  Prefer a named result class with `OutputType` (§8) and read its PascalCase names.
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
%LOCALAPPDATA%\AnalyseTool\extensions\            your own buttons (or any folder added in the pane › Folders of buttons)
    Sheets.tab\Numbering.panel\Renumber.pushbutton\   one button: bundle.json, *.cs, index.html, AGENTS.md, CLAUDE.md, .git\
    acme.doors\                                    an extension of plugin.json (a DLL one, or an older folder)
        plugin.json, src\*.cs or 2025\<YourExt>.dll, index.html, .git\
%LOCALAPPDATA%\AnalyseTool\extensions-dist\<id>\  installed packages (.zip, plugin.json)
```
- A tab folder and an extension folder sit DIRECTLY under a root — a Revit year is a subfolder INSIDE a DLL
  extension, never above it.
- **No Reload needed:** a changed file reloads its button, and ribbon buttons appear, change and disappear
  without restarting Revit.
- **Versions.** Each button folder (and each extension folder) is a git repository. The user saves a version with a
  message, sees the history and the diff, restores a version (or one file — unsaved work is saved as a version
  first, so nothing is lost) and can **pin** a button to a version: it runs that version's code whatever the files
  say. Over MCP: `SaveExtensionVersion`, `GetExtensionVersions`, `GetExtensionDiff`, `RestoreExtensionVersion`,
  `PinExtensionVersion` — the id of a button of a tab is `<tab>.<button>`. Do not run git yourself in the folder
  unless asked; never commit `bin\` or `.editor\`. `_shared\` of a tab has no history of its own.

### 7.0 Migrating buttons into tabs

Check the layout first. A folder `<Name>.pushbutton\` with a `bundle.json` is the current layout (§1.1). A folder
with a `plugin.json` is the older one — AnalyseTool 1.x, or written by hand: it still loads (in the pane it is a
section of «Older buttons»), but every new button goes into a tab. Move a button over when you change it anyway.

**With AnalyseTool** (the user, or you over MCP) — one button at a time:

- Pane › «Older buttons» › right-click the button › ✎ › **Move to…** › a tab and a panel.
- Over MCP: `MoveExtensionButton { id: "<old id>", button: "<its name>", panel: "<panel>", toId: "<tab>.tab", before? }`
  (`ListRevitVersions`/`GetExtensions` show what exists; `CreateExtensionFolder { name }` makes a tab).

What the move does:

| From the `plugin.json` folder | To `<Tab>.tab\<Panel>.panel\<Name>.pushbutton\` |
| --- | --- |
| The button entry of `ui.buttons` | `bundle.json`: `name`, `tooltip`, `icon`, `args`, `input`, `ribbon`, `pane`, `dockable` as they were; `command` becomes the class name only; `entryHtml` becomes `page`; `tab`/`panel` are the folders |
| The C# file of its command and of every command its page calls (`AT.invoke('<old id>.X')`) | Moved; COPIED while another button of the old folder still uses it |
| C# files with no command (helpers) | Copied |
| Its page | `index.html`; every `'<old id>.X'` becomes `'<tab>.<button>.X'` |
| Its icon file | Copied |
| The old folder | Saves a version; when no button and no C# is left in it, it goes to the AnalyseTool trash with its history |

The new folder starts its own history. After the move:

1. `BuildExtension { id: "<tab>.<button>" }` — no errors?
2. Files the page loads itself (its own `.css`, `.js`, images) are NOT moved: copy them next to `index.html`.
3. Helpers copied into several buttons: put ONE copy into `<Tab>.tab\_shared\` and delete the copies — every button
   of the tab compiles with `_shared\`.
4. Tell the user what moved and what stayed (`GetExtensions` for the old folder).

**By hand** — the same, without the tool: create the folders, write `bundle.json` from the entry as in the table,
put the `.cs` of the command next to it (one class per button is the rule; shared code to `_shared\`), rename the
`AT.invoke` calls of the page, then remove the entry and the files from the old folder. Saving reloads both.

**What stays a `plugin.json` folder:** a DLL extension (its commands live in its project, §4), an installed package
(its author's updates would undo a change) and AnalyseTool's own buttons.

**The oldest layout** `extensions\<year>\<id>\` (the year ABOVE the extension) still loads too. To fix it, move the
year inside — `extensions\<id>\<year>\*.dll`, everything else (`plugin.json`, pages, icons) once in `<id>\`; if the
duplicates differ, the newest wins — say so. Leave deleting the old folders to the user.

### 7.0a Releasing from GitHub

A DLL extension in a GitHub repository can build every Revit year and, on a `v*` tag, publish the packed zip
as a GitHub Release — the same zip `updateFeed` points at, so tagging IS the release. Add the workflow of §7.1
as `.github/workflows/release.yml`:

```
dotnet build -c Release -t:PackExtension     # the same thing, locally
git tag v1.0.0 && git push --tags            # and on GitHub, with a Release attached
```

The tag must match `version` in `plugin.json`; `PackExtension` fails the build otherwise, because a
mismatch would leave the update badge permanently wrong.

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
| Which Revit | `ListRevitVersions` | The open Revit versions; every other tool takes `"revit": "2026"`, without it the Revit used last. |
| Start a button | `CreateExtension { name, description?, template?, tab?, panel? }` | `<Tab>.tab\<Panel>.panel\<Name>.pushbutton\` (the tab is made when it is new) with `<Class>.cs` that compiles and its first version; id `<tab>.<button>`. |
| Start a tab | `CreateExtensionFolder { name, panel? }` | An empty tab `<Name>.tab\`, id `<name>.tab`. |
| Add a button to a tab | `AddExtensionButton { id: "<name>.tab", name, description?, template?, panel? }` | As `CreateExtension`, in that tab. |
| Organise | `SetExtensionPanel { id: "<name>.tab", panel?, name }` (add or rename a panel), `MoveExtensionButton { id: "<name>.tab", button, panel, before?, toId? }` | Another panel or another tab: its folder moves with its history; in another tab its id changes and its page follows. From a `plugin.json` folder, `toId: "<name>.tab"` migrates the button (§7.0). |
| Rename | `UpdateExtensionFolder { id: "<name>.tab", name }` (the tab), `UpdateExtensionButton { id, button, name, tooltip?, dockable? }` | `bundle.json` only — folders, ids and commands stay. |
| Run a new command | `RunCommand { name, payload?, revit? }` | Runs any command by name — also one your tool list does not show yet (a client that ignores `tools/list_changed`). |
| Read / write code | `ListExtensionFiles`, `ReadExtensionFile`, `WriteExtensionFile { id, path, content }` | Writing reloads at once; the answer carries the compiler diagnostics. |
| Check it | `BuildExtension { id }` | `success` and diagnostics with file/line/column. |
| Try it | the command itself (`<id>.<Command>`), or `ExecuteCode` for a throwaway snippet | Its result. |
| Keep it | `SaveExtensionVersion { id, message }` | The new version (null when nothing changed). |
| History | `GetExtensionVersions`, `GetExtensionDiff { id, from?, to? }`, `RestoreExtensionVersion`, `PinExtensionVersion` | Versions, diffs, restore and pin. |

- **One button, one folder.** A button folder may hold several `IRevitTask` classes (its page can call each:
  `sheets.renumber.Preview`, `sheets.renumber.Apply`); code several buttons need goes to `_shared\` of the tab.
- **A command that takes input belongs behind a page.** A ribbon click has no arguments: give such a command
  a page (`template: CommandWithPage`) that collects the input and calls it with `AT.invoke`.
- **Shared folders are shared.** When an extension's folder is not the user's own (a network folder added in
  Settings), say so before changing it: a fix there is everyone's fix.
- Save a version after a change that works, with a message that says what changed — that is what the user
  will scroll through when something breaks.

---

## 8. Rules — ALWAYS / NEVER

- **ALWAYS** write C# sources in the button folder (§5) unless §4.1 says a DLL: NuGet packages with dependencies,
  native libraries, embedded resources, closed code. "Big" or "complex" alone is not a reason.
- **ALWAYS** set `<EnableDynamicLoading>true</EnableDynamicLoading>` in the project of a DLL extension that uses any
  NuGet package (§4.3) — else its dependencies are not in `<year>\` and the command fails in Revit.
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

- [ ] A button folder `<Tab>.tab\<Panel>.panel\<Name>.pushbutton\` with `bundle.json` and its `*.cs` (§1.1, §5); a C# project only when §4.1 says so.
- [ ] A DLL extension: `plugin.json` with `id`, `"build": "author"`, `entryAssembly`, `ui.tab` (§4.2).
- [ ] Its csproj builds into `<extension>\<year>\`, derives its TFM from `RevitVersion`, has `EnableDynamicLoading` when it uses NuGet packages, and ships no copy of the Sdk, the Revit API or Newtonsoft (§4.3).
- [ ] C#: one or more `IRevitTask` classes; model access only in `RunInRevitAsync`.
- [ ] `[RevitCommand]` with a clear `Description`; `ReadOnly`/`Destructive` set correctly; `InputType`
      for commands that take arguments; `OutputType` for what they return (SDK 1.2+).
- [ ] UI: `index.html` calling `window.AT.invoke(...)`; `base: "./"` if framework-built.
- [ ] Tell the user where the folder is; a saved file reloads by itself, and the button appears without a restart.

---

## 11. Model tests

A **model test** checks the open model the way a unit test checks code: «every door has a fire rating», «no two rooms
share a number», «no duct hits a beam». Tests are C# in a folder of model tests — a source extension whose folder name
ends in **`.tests`** (`bep-project-x.tests\src\*.cs`; its id in plugin.json has no ending). A suite anywhere else does
not load and the folder reports it. A suite is a class (Sdk 1.3+) that implements `IModelTestSuite` (namespace
`AnalyseTool.Sdk.Testing`), with a public parameterless constructor. AnalyseTool calls its
`Define` when the extension loads, and runs the tests from the **Tests** tab of the pane or over MCP. A saved file reloads
them, like a button.

```csharp
using AnalyseTool.Sdk.Testing;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

public sealed class BepProjectX : IModelTestSuite
{
    public void Define(ISuite s)
    {
        s.Name("BEP Project X").Trait("Discipline", "ARC");

        // Rules: one value per element, declarative — the page knows what is checked and words the messages
        s.Elements(BuiltInCategory.OST_Doors)
         .Parameter("Fire Rating").NotEmpty().OneOf("EI30", "EI60", "EI90")
         .FixValue("EI30");                                    // a repair the page offers: writes the value

        s.Elements<Room>()
         .Where(room => room.Area > 0)
         .Parameter(BuiltInParameter.ROOM_NUMBER).NotEmpty().Unique();

        s.Elements(BuiltInCategory.OST_Doors).TypeName().Matches(@"^T\d{2}_");

        // Code per element: soft assertions, every failure is a case of its own
        s.Elements(BuiltInCategory.OST_Doors).Check("Door is in a wall", (door, m) =>
        {
            var instance = (FamilyInstance)door;
            m.Assert.True(instance.Host is Wall, "The door has no host wall");
        }).Fix("Delete", door => door.Document.Delete(door.Id));

        // Code for the whole model
        s.Test("No unplaced rooms", m =>
            m.Assert.Empty(m.Elements<Room>().Where(room => room.Location == null), "The room is not placed"));

        // Geometry is plain Revit API; evidence tells the page what to draw
        s.Elements(BuiltInCategory.OST_DuctCurves).Check("Ducts do not hit beams", (duct, m) =>
        {
            var box = duct.get_BoundingBox(null);
            if (box is null) { m.Assert.Inconclusive("No geometry"); return; }
            var outline = new Outline(box.Min, box.Max);
            foreach (var beam in new FilteredElementCollector(m.Document)
                         .OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType()
                         .WherePasses(new BoundingBoxIntersectsFilter(outline)))
            {
                m.Assert.Fail($"Touches {beam.Name}", new Evidence { Related = [beam], Box = outline });
            }
        }).Slow();
    }
}
```

| Builder | What it declares |
| --- | --- |
| `s.Elements(params BuiltInCategory[])`, `s.Elements<T>()` | The elements (instances; `Elements<WallType>()` for types). `.Where(e => …)` filters. |
| `.Parameter(name \| BuiltInParameter)`, `.TypeParameter(…)`, `.TypeName()`, `.Value(label, e => …)` | A **rule** on one value. Conditions: `NotEmpty()`, `Equal(text)`, `Matches(regex)`, `OneOf(…)`, `InRange(min, max)` (in Revit's internal units: lengths in feet, so `InRange(100.Mm(), 20000.Mm())`, areas `1.M2()`, volumes `0.5.M3()`, angles `45.Deg()`; a failure shows the value in the project's units), `Unique()`. The first that fails is the message; a missing parameter fails every condition. |
| `.Check(name, (element, m) => …)` | Code per element. |
| `s.Test(name, m => …)` | Code once for the model. |
| `.Fix(label, e => …)`, `.FixValue(text)` | A repair for failing elements; it runs in one transaction AnalyseTool opens (one Ctrl+Z). |
| `.Id(id)`, `.Name(name)`, `.Severity(…)`, `.Trait(name, value)`, `.Slow()`, `.Skip(reason)` | Options; call them last, they end the chain. |

Inside a body, `m.Document` is the model, `m.Elements(…)` collects, `m.Assert` records failures — `True`, `NotEmpty`,
`Empty(items, message)` (each item fails; an element item is the failing element), `Fail`, `Inconclusive` — and an
optional `Evidence { Element, Related, Point, Box, Key }` tells the page what to select and draw: with a `Box` or `Point`
(where two solids overlap, where a gap is too small) **Highlight** draws that place red and the elements pale around it;
without, it draws the element. Give a clash its overlap box: the place is what the user looks for. A view in the
Realistic display style does not show the highlight: Shaded, Consistent Colors or Textures does. `Units`: `900.Mm()`,
`0.9.M()`, `25.M2()`, `0.5.M3()`, `45.Deg()` — the API measures in feet, square and cubic feet, radians.

- **Tests never change the model.** They run inside a transaction group that is always rolled back; only a repair the
  user (or you, with `FixModelTestCases`) starts writes.
- **Prefer `BuiltInParameter` / `BuiltInCategory` over names**: a parameter name depends on the language of Revit
  («Mark» is «Kennzeichen» in German).
- **The baseline.** An existing model fails hundreds of times; `AcceptModelTestFailures` accepts the current failures,
  and from then on only new ones fail the test. They are kept in `baseline\<model>.json` of the suite's folder,
  versioned with the tests. Keys follow the element's unique id, so a test keeps its baseline while its id stays —
  give a test whose name may change an `.Id("…")`. **Ask the user before accepting failures**: it hides them.
- **Results.** `passed` means nothing *new* failed; `failed`, `error` (the test threw — a bug in the test, not the
  model), `inconclusive` (Revit could not tell), `skipped` (`.Skip`, or `.Slow()` in a run with `includeSlow: false`).
- **Keep `Define` cheap**: it runs at every load and has no model. Collect in the lambdas.

Over MCP: `CreateModelTestSuite { name }` (a folder `<id>.tests` with an example suite), `GetModelTests`, `RunModelTests { suite?,
tests?, trait?, elementIds?, includeSlow? }`, `GetModelTestResults`, `FixModelTestCases { testId, keys? }`,
`AcceptModelTestFailures { testId, keys?, remove? }`. A good loop: write the test, run it, read what failed, refine
the rule or offer the repair — then save a version.
