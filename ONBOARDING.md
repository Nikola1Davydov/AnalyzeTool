# Writing AnalyseTool Extensions

AnalyseTool lets you add your own functionality **without rebuilding the host**. You drop a
folder into your local extensions directory, and AnalyseTool loads it — no restart, no Reload button — and your code
shows up — a new command callable from JavaScript, a ribbon button, a UI page, or all three.

This guide is for **extension authors**. It covers the three kinds of extension, the folder
layout, the manifest, the C# command contract, the JS UI contract, the build/deploy/reload loop,
and how to publish so your users get updates.

---

## 1. The mental model

There are two kinds of extension, and they play different roles:

| Kind | What it ships | What it does | Build needed? |
| --- | --- | --- | --- |
| **C# extension** | C# sources in `src\` (the default) — or a `.dll` per Revit year | **ADDS** commands to the host's command pipeline | no for sources (AnalyseTool compiles them), yes for a DLL |
| **JS / UI extension** | an HTML page (any framework) | **CONSUMES** commands by calling `window.AT.invoke(...)` | no |

> **The one principle:** C# extensions *add* commands; JS extensions *consume* them.

A single extension folder can be any of these, or a combination. The sample
(`samples/Acme.Sample`) is a C# project + UI: a `Hello` command plus an `index.html` page with a button
that calls it.

**Since AnalyseTool 2.0 a new button is C# sources in a folder of its own.** «New button» in the pane creates
`<Tab>.tab\<Panel>.panel\<Name>.pushbutton\` with `bundle.json` and `<Name>.cs` — the folders are the ribbon, as in
pyRevit (§2). AnalyseTool compiles the sources itself, with its own Roslyn against the running Revit's API — no .NET
SDK, no project, no DLL. Save a file and the button reloads; it runs the new code a moment later. Every button folder
has **its own version history** (git):
save a version, compare, restore, or pin the button to an older version — from the extension's page in
AnalyseTool (§6) or over MCP (§9). Build a DLL project (§4) only for what sources cannot do: NuGet
packages, closed code, XAML/resources.

Every command — built-in or from any C# extension — is reachable through the same channels:

```
your page  ──AT.invoke("acme.sample.Hello")──▶  WebView2 transport ─┐
ribbon button ─────────────────────────────────────────────────────┼──▶  command pipeline  ──▶  your IRevitTask
AI client  ──────────── MCP (HTTP, inside Revit) ──────────────────┘
```

The pipeline is **transport-neutral**: pages, ribbon buttons and AI clients (§9) all call it. Anything
you write as an `IRevitTask` is available to all of them — so **never** touch the WebView, the network,
or transport details from inside a command. Return a serializable result; the transport delivers it.

---

## 2. Where extensions live

**Your own buttons live in tabs**, laid out as the ribbon:

```
%LOCALAPPDATA%\AnalyseTool\extensions\
    Sheets.tab\                      a ribbon tab = a folder of the pane     (bundle.json: name, order of the panels)
        _shared\*.cs                 optional: C# every button of the tab compiles with
        Numbering.panel\             a panel = a section                     (bundle.json: name, order of the buttons)
            Renumber.pushbutton\     ONE button, all of it in one folder:
                bundle.json          its name, icon, tooltip
                Renumber.cs          its command
                index.html           its page (optional)
                .git\                its versions
            More.pulldown\           a pulldown: *.pushbutton folders inside
```

A button folder is an extension of its own, id `<tab>.<button>` (`sheets.renumber`); its commands are
`sheets.renumber.<Class>`. `bundle.json` of a button is the button entry of §3, with `command` as the class name only
and `page` for its page; where it sits is the folders. The rest of this section is about **extensions of
`plugin.json`**: DLL extensions (§4), packages, and folders made before tabs (see «Migrating» below).

One extension = one folder, sitting **directly** under an extensions root. Inside it, the Revit
year is a **subfolder** holding that year's binaries:

```
%LOCALAPPDATA%\AnalyseTool\extensions\<your-id>\
    plugin.json        (required)
    2025\<YourExt>.dll (C# commands — one folder per Revit year you ship)
    2027\<YourExt>.dll
    src\*.cs           (only in a command saved by an AI agent — the plugin builds it, §9.1)
    index.html         (UI page — version-independent, always in the root)
    icon.png           (ribbon button icon)
    ...any assets...
```

`<your-id>` is your `id` from `plugin.json`. This is exactly the layout of a published package,
which is the point: the folder you develop in is the folder you zip.

**How the host resolves the entry assembly** (running Revit year `Y`):

1. `<your-id>\<Y>\<entryAssembly>` — the normal case.
2. `<your-id>\<entryAssembly>` — fallback, so a hand-made single-year folder works without a year
   subfolder.
3. Neither → the extension is **listed but not loaded**, flagged in the manager. It never
   disappears silently.

`ui/` always comes from the root — it is version-independent.

Each extension is isolated: its C# DLL is loaded into its own collectible `AssemblyLoadContext`,
so two extensions can't collide and a reload can swap one out.

### Two zones

| | **Installed** | **Dev / Local** |
| --- | --- | --- |
| Where | `%LOCALAPPDATA%\AnalyseTool\extensions-dist\` | `%LOCALAPPDATA%\AnalyseTool\extensions\` + any folders you add in the pane › profile › *Folders of buttons* |
| Owned by | the extension manager — install / remove / update | you; nothing is ever rewritten behind your back |
| In AnalyseTool | profile › **Packages** | the folders and tabs of the pane |

As an author you work in the dev zone. The managed zone is what your users get when they install
your `.zip`.

### Migrating

**A `plugin.json` folder of your own buttons → tabs.** It keeps loading (a section of «Older buttons» in the pane),
but new buttons go to tabs. Move a button over in the pane: right-click it › ✎ › **Move to…** › a tab. Its C#, its
page (as `index.html`, with its `AT.invoke` names renamed) and its icon go into a new `<Name>.pushbutton\` with a
history of its own; code another button still uses is copied, not moved; the old folder saves a version and goes to
the trash once it is empty. Copy files the page loads itself (`.css`, `.js`, images) by hand, and put helper code
several buttons share into `<Tab>.tab\_shared\`. The full table — and how to do it by hand or over MCP — is §7.0 of
`LLM.md`. DLL extensions and installed packages stay `plugin.json` folders.

**The year above the extension** (`extensions\<year>\<id>\`, before packages) still loads, unchanged. To convert,
move the year inside and merge the copies:

```
BEFORE                                   AFTER
extensions\2025\acme.doors\              extensions\acme.doors\
    plugin.json                              plugin.json          <- keep ONE (they were identical)
    Acme.Doors.dll                           index.html           <- from either copy
    index.html                               icon.png
extensions\2026\acme.doors\                  2025\Acme.Doors.dll
    plugin.json                              2026\Acme.Doors.dll
    Acme.Doors.dll
    index.html
```

DLLs go into a `<year>\` subfolder, everything else in the root exactly once, `plugin.json` needs no edit; point the
build at `<OutDir>$(MSBuildProjectDirectory)\$(RevitVersion)\</OutDir>` as in §4.1.

---

## 3. The manifest — `plugin.json`

`plugin.json` is **required** and sits at the root of your extension folder. Full shape:

```json
{
  "id": "acme.sample",
  "version": "1.0.0",
  "entryAssembly": "Acme.Sample.dll",
  "ui": {
    "entryHtml": "index.html",
    "devUrl": "http://127.0.0.1:5173",
    "tab": "AnalyseTool",
    "panel": "Samples",
    "button": {
      "name": "Acme Sample",
      "tooltip": "Open the Acme Sample extension page",
      "icon": "icon.png"
    }
  }
}
```

| Field | Required | Notes |
| --- | --- | --- |
| `id` | ✔ | Unique, lowercase, dotted (`acme.sample`). Becomes the command prefix and the folder name. |
| `version` | ✔ | SemVer string. Shown in profile › Packages and appended to the window title (`Name - 1.0.0`). This is the single source of truth for the extension's version — the packaging pipeline reads it. |
| `entryAssembly` | — | DLL file name. **Omit for a UI-only extension.** Resolved in the Revit-year subfolder first (`2025\`), then the folder root — no `targetRevit` field needed, the year folders are the declaration. SDK compatibility is derived automatically from the DLL's `AnalyseTool.Sdk` reference — no `sdkVersion` field either. The current host SDK version is shown in the About window (ribbon → About). |
| `description` | — | One line, shown in the extension listing. |
| `publisher` | — | You or your company. Shown next to the extension name. |
| `website` / `supportUrl` | — | Links shown in the listing. Recommended when publishing. |
| `icon` | — | Extension-level PNG (relative path) for the listing; falls back to `ui.button.icon`. A path that leaves the extension folder (`..\`, an absolute path) is ignored. |
| `updateFeed` | — | Where the manager checks for newer versions: `github:owner/repo` (latest release, zip asset) or an HTTPS URL returning `{ "version": "...", "downloadUrl": "..." }`. Only meaningful for published extensions — see §10. |
| `ui` | — | **Omit for a command-only extension.** |
| `ui.entryHtml` | — | Page to open, relative to the folder. Default `index.html`. Sub-paths like `"app/index.html"` work. |
| `ui.devUrl` | — | Dev server URL (Vite/HMR). When set, the window loads this instead of the built files. **Remove for release.** |
| `ui.dockable` | — | `true` = the button shows the page inside AnalyseTool's shared **dockable pane** (docks like the Project Browser) instead of a separate window. Click again to hide; another dockable button switches the pane's content. Picked up live via Reload. |
| `schema` | — | Manifest FORMAT version, not the extension's. Absent = 1. Set `2` when using `ui.buttons`. The host keeps loading older schemas — a migration is an offer, never a requirement. |

#### Several buttons on one extension

`ui.button` describes one surface. An extension with two — a manager window and a dockable palette,
say — declares `ui.buttons` instead, because the page to open and whether it docks belong to the
SURFACE, not to the extension:

```json
"ui": {
  "tab": "AnalyseTool",
  "panel": "Acme",
  "buttons": [
    { "name": "Manager", "entryHtml": "dist/index.html" },
    { "name": "Palette", "entryHtml": "dist/palette.html", "dockable": true }
  ]
}
```

An icon is either a PNG beside `plugin.json` or `glyph:E8A9` — a Segoe MDL2 Assets code point, the
same source the host draws its own buttons from. The glyph form ships nothing and stays crisp at any
DPI; omit the icon entirely and the button gets a letter.

Each entry may also carry `kind` — `push` (default), `stacked` or `pulldown`. A `stacked` button is
small: the host lays every small button on a panel — yours and other extensions' alike — into columns
of three, the shape Revit's own stacked items make, ordered by `order`, then extension id, then
declaration, after the panel's large buttons; a `pulldown` lists its `items`
under one head and does not run the first of them on click. An unknown `kind` falls back to `push`,
so a manifest written against a later host still produces a usable ribbon.

Each entry may carry `entryHtml`, `dockable`, `tab`, `panel` and `order` (lower first; 0 = no preference, sorted after the numbered ones — for large and small buttons alike), each falling back to the
`ui.*` value when omitted — so a single-button manifest never repeats itself. `name`, `tooltip`,
`icon` and `command` work exactly as in the singular form.

The singular `ui.button` is not deprecated: for one surface it stays the clearer choice, and existing
manifests need no change. When both are present, `ui.buttons` wins.

| `ui.tab` | — | Ribbon tab to place the button on. Default `"AnalyseTool"`. |
| `ui.panel` | — | Ribbon panel within that tab. Default `"Extensions"`. |
| `ui.button.name` | — | Button label — also used as the extension's display name (Extensions list, window title). |
| `ui.button.tooltip` | — | Button tooltip. |
| `ui.button.icon` | — | Icon path relative to the folder (must sit beside `plugin.json`; a path that leaves the folder is ignored). If missing, a default icon (colored square with the extension's initial) is drawn automatically. |
| `ui.button.command` | — | Run this command when the button is clicked, instead of opening `entryHtml`. Use it for a one-shot action that needs no page. |

`ui.tab` / `ui.panel` are honored **live** — change them, save, and the button moves.
Empty custom tabs/panels are torn down automatically (the built-in "AnalyseTool" tab is never
touched).

---

## 4. Writing a C# command extension

### 4.1 Project setup

A DLL extension is for what a button folder cannot do: NuGet packages with dependencies, native libraries,
embedded resources, closed code. A big tool is not a reason by itself — sources take many classes, a page and
`lib\*.dll`. The full walk-through (folder, `plugin.json` with `"build": "author"`, project, loading rules) is §4
of `LLM.md`.

**The easy way — NuGet.** Install the SDK package for the contract, and declare the target
framework and the Revit API packages yourself (NuGet deliberately ignores build props shipped
inside packages during restore, so a package **cannot** add those references for you):

```
dotnet add package AnalyseTool.Sdk
```

A minimal extension `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- The Revit year drives everything below. Retarget by editing this one number. -->
    <RevitVersion>2025</RevitVersion>

    <!-- Not a free choice: the Nice3point package for a year is built for that year's runtime,
         so a net8 project referencing the 2027 package fails restore with NU1202. -->
    <TargetFramework Condition="'$(RevitVersion)' &lt; '2027'">net8.0-windows</TargetFramework>
    <TargetFramework Condition="'$(RevitVersion)' &gt;= '2027'">net10.0-windows</TargetFramework>

    <PlatformTarget>x64</PlatformTarget>
    <RootNamespace>Acme.Sample</RootNamespace>
    <AssemblyName>Acme.Sample</AssemblyName>

    <!-- Build straight into <extension>\<year>\ — the layout the host resolves and a package
         ships, so the project folder IS the deployable extension and years accumulate instead
         of overwriting each other. -->
    <!-- Required with NuGet packages: copies them to the output and writes the deps.json AnalyseTool loads them by -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <OutDir>$(MSBuildProjectDirectory)\$(RevitVersion)\</OutDir>
  </PropertyGroup>
  <ItemGroup>
    <!-- Compile-only on purpose (see the type-identity note below): the host owns these DLLs. -->
    <!-- Exact version, never a range: pinning is what keeps someone else's release from
         changing a build of yours that already works. -->
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

**There is no per-year build configuration here, and none is needed.** The `Debug R25`/`R27`
configurations belong to the AnalyseTool repository itself; a package-consuming project has plain
Debug/Release and one `RevitVersion` property. To build another year, pass it on the command line —
a command-line property overrides the one in the file:

```
dotnet build -c Release                        # the year in the csproj (2025 above)
dotnet build -c Release -p:RevitVersion=2026
dotnet build -c Release -p:RevitVersion=2027
```

Each build lands in its own `<year>\` folder, so run one command per Revit version you ship and
they accumulate side by side. (CI builds `samples/Acme.Sample` against the freshly packed SDK in
exactly this mode — `-p:UseSdkPackage=true` — so this path stays verified.)

> **Tip:** you don't have to write this by hand — **AnalyseTool tab → New** (every template is page + C#; delete the half you don't need)
> scaffolds a ready-to-build project, a `plugin.json`, and `AGENTS.md` / `CLAUDE.md` that point an AI agent to `LLM.md`, the authoring guide installed
> next to AnalyseTool (and updated with it).

With that `OutDir`, `dotnet build -c Release` already writes into `<project>\<year>\` — so if your
project folder *is* the extension folder (`plugin.json` beside the `.csproj`), there is nothing to
copy: build, done — the extension reloads by itself. Otherwise copy the whole folder — root files plus the `<year>\`
subfolders — to your extensions directory. (Don't worry about the SDK/Revit/Newtonsoft DLLs — the
host owns them and the extension's load context shares the host's copies, so type identity stays
intact even if a copy ends up beside your DLL.)

**The in-repo way (alternative).** If you build inside this repository — or next to a checkout of
it — reference the SDK by project and import the shared build props by path. File imports *are*
restore-visible, so the configurations, the TFM per year and the Revit API packages all arrive from
the props and there is no boilerplate at all. `samples/Acme.Sample/Acme.Sample.csproj` is the
working example; it additionally carries a `UseSdkPackage` switch so CI can build it both ways, and
imports `AnalyseTool.Sdk.targets` for the packaging pipeline (§10). Stripped to essentials:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="..\..\src\AnalyseTool.Sdk\build\AnalyseTool.Extension.props" />

  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>true</ImplicitUsings>
    <RootNamespace>Acme.Sample</RootNamespace>
    <AssemblyName>Acme.Sample</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- Private=false: compile against the SDK, but DON'T copy it to output.
         The host owns AnalyseTool.Sdk.dll; your ALC shares it (type identity). -->
    <ProjectReference Include="..\..\src\AnalyseTool.Sdk\AnalyseTool.Sdk.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>

  <ItemGroup>
    <None Include="plugin.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
    <None Include="index.html"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  </ItemGroup>

</Project>
```

The shared props (`src/AnalyseTool.Sdk/build/AnalyseTool.Extension.props`) give you:

- The configurations: **Debug/Release R25, R26 and R27**.
- `TargetFramework` per version — `net8.0-windows` for R25/R26, `net10.0-windows` for R27; `PlatformTarget = x64`.
- The Revit API packages referenced **compile-only** (`PrivateAssets=all` + `ExcludeAssets=runtime`).

> **Why "compile-only" / `Private=false` everywhere matters:** the host already loads the SDK,
> the Revit API, and Newtonsoft.Json. If your output folder *also* contained copies of those
> DLLs, your `AssemblyLoadContext` would load a *second* copy and `your is IRevitTask` would be
> **false** (different type identity → your command silently won't register). Keep your output to
> just **your** DLL + `plugin.json` + assets. Verify: a clean build of the sample produces only
> `Acme.Sample.dll`, `plugin.json`, `index.html` (+ pdb/xml/deps).

### 4.2 The command contract

Implement `AnalyseTool.Sdk.IRevitTask`:

```csharp
namespace AnalyseTool.Sdk
{
    public interface IRevitTask
    {
        Task<object?> ExecuteAsync(IRevitContext context, CancellationToken cancellationToken);
    }
}
```

`IRevitContext` is intentionally tiny — this is the **entire** surface you get:

```csharp
public interface IRevitContext
{
    // The JSON payload the caller passed to AT.invoke(command, payload).
    RevitPayload Payload { get; }

    // The ONLY place you may touch the Revit model. Runs on the Revit thread,
    // inside a valid API context (transactions allowed). Returns the result.
    Task<T> RunInRevitAsync<T>(Func<UIApplication, T> work);
    Task   RunInRevitAsync(Action<UIApplication> work);
}
```

`RevitPayload` deserializes the incoming JSON:

```csharp
var args = revitContext.Payload.As<MyArgs>();   // strongly-typed
string raw = revitContext.Payload.RawJson;      // or the raw JSON
```

### 4.3 The one rule: model access only inside `RunInRevitAsync`

`IRevitContext` deliberately does **not** expose `Document` / `UIApplication` directly. The Revit
API may only be touched on the Revit thread inside a valid API context — `RunInRevitAsync`
marshals onto it for you. This is enforced by the type so you can't accidentally start a
transaction off-thread.

```csharp
using AnalyseTool.Sdk;

namespace Acme.Sample
{
    [RevitCommand("Hello")]                 // wire name (see 4.4)
    public sealed class HelloRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
        {
            return revitContext.RunInRevitAsync<object?>(app =>
            {
                var uiDoc = app.ActiveUIDocument;
                int selectedCount = uiDoc.Selection.GetElementIds().Count;
                string activeView = uiDoc.Document.ActiveView.Name;

                return new { message = "Hello from Acme.Sample!", selectedCount, activeView };
            });
        }
    }
}
```

**Reads and writes both go inside `RunInRevitAsync`.** For a write, open a transaction *inside* it:

```csharp
await revitContext.RunInRevitAsync(app =>
{
    var doc = app.ActiveUIDocument.Document;
    using var t = new Transaction(doc, "Acme: do thing");
    t.Start();
    // ... mutate ...
    t.Commit();
});
```

**Long-running I/O (HTTP, AI, file reads) stays OUTSIDE `RunInRevitAsync`** — its body runs
synchronously on the Revit thread and will freeze the UI. Do the slow work first, then marshal
just the model touch:

```csharp
public async Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
{
    var data = await httpClient.GetStringAsync(url, cancellationToken);          // off the Revit thread
    return await revitContext.RunInRevitAsync(app => ApplyToModel(app, data)); // on it, briefly
}
```

The return value is serialized to JSON and resolves the caller's `AT.invoke(...)` promise. Throw
to reject it — the transport reports the exception message back to JS.

### 4.4 Command names

The wire name is what JS calls and what the dispatcher registers. By default it's the **class
name** — whether you have no attribute at all, or `[RevitCommand]` with only metadata
(`[RevitCommand(Description = "...", ReadOnly = true)]`). Pass a name only to *override* it,
`[RevitCommand("OtherName")]`, e.g. to rename the class without breaking callers. The dispatcher
namespaces every extension command with your `id`:

```
plugin.json id  +  command name   ──▶   wire name
   "acme.sample"      "Hello"      ──▶   "acme.sample.Hello"
```

So the sample is called as `AT.invoke("acme.sample.Hello")`.

---

### 4.5 Command metadata (powers MCP)

`[RevitCommand]` carries everything MCP needs to make your command usable by an AI. You still read
the payload yourself with `revitContext.Payload.As<T>()`; you just *declare the input type* so the host can
publish a JSON schema for it.

```csharp
using System.ComponentModel; // for [Description]

[RevitCommand("SetWallComment",
    Description = "Sets the Comments parameter on the given walls. Modifies the model.",
    Destructive = true,                          // -> MCP destructiveHint
    InputType = typeof(SetWallComment.Args))]    // -> generates the tool's input schema
public sealed class SetWallComment : IRevitTask
{
    public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
    {
        Args? args = revitContext.Payload.As<Args>();     // deserialize as usual
        return revitContext.RunInRevitAsync<object?>(app => { /* ...use args.ElementIds / args.Comment... */ return null; });
    }

    internal sealed record Args                  // must be at least `internal` (see note)
    {
        [Description("Element ids of the walls to update.")]   // -> per-field schema description
        public List<long> ElementIds { get; set; } = new();

        [Description("Text to write into the Comments parameter.")]
        public string Comment { get; set; } = "";
    }
}
```

| `[RevitCommand]` field | Effect |
| --- | --- |
| `Description` | MCP tool description + appears to JS callers. Be specific: what it does, when, what it returns. |
| `ReadOnly = true` | Marks the tool `readOnlyHint` — clients treat it as safe. Use for `Get*`/query commands. |
| `Destructive = true` | Marks the tool `destructiveHint` — clients may warn/confirm. Use for writes/deletes. |
| `InputType = typeof(T)` | The host generates the tool's JSON **input schema** from `T`, so an AI knows which arguments to send. Omit for no-argument commands. |
| `OutputType = typeof(T)` | **SDK 1.2+.** The counterpart: the host generates the **output schema** from `T`, so a caller knows what comes back instead of inferring it from the description — and two commands can be checked for compatibility before being chained. If `ExecuteAsync` returns an anonymous object, promote it to a named record first. |
| `HiddenFromMcp = true` | Keeps the command callable from JS but **hides it from the AI's tool list**. Use for plugin-management or UI-only commands. Default: exposed. |

Notes:
- The types passed to `InputType` / `OutputType` must be at least `internal` (so `typeof(...)` in the
  attribute can reference it) — a `private` nested type won't compile there. No-argument commands omit
  `InputType`.
- **Spell the JSON names out** on an output type: `[JsonProperty("id")]`. Results are serialized by
  Newtonsoft, which writes the declared property names, while the published schema is generated with
  camelCase — so a DTO without them goes out as `{"Id":…}` while its own schema promises `{"id":…}`.
  An output schema that misnames what it describes is worse than none.
- **Use a LEAN input type** — only the fields the caller actually sends. Don't reuse rich
  domain/output models (ones with Revit-type properties or deep nesting): the generated schema
  balloons (and gets truncated by a size cap). Define a small purpose-built record per command.
- Put a `[System.ComponentModel.Description("…")]` on each input field — it flows into the JSON
  schema as the field's description, so the AI gets per-argument guidance (curated quality, still
  auto-generated).

### 4.6 Progress reporting (SDK 1.1, optional)

A long-running command can report live progress by additionally implementing `IProgressAware`. The
host injects a `Progress` sink bound to the calling window before `ExecuteAsync` runs; from JS,
`AT.invoke(command, payload, { onProgress })` receives the updates while the promise stays pending.

```csharp
public sealed class BulkUpdate : IRevitTask, IProgressAware
{
    public IProgress<ProgressInfo>? Progress { get; set; }   // set by the host; null if nobody listens

    public async Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            await revitContext.RunInRevitAsync(app => ProcessChunk(app, chunks[i]));
            Progress?.Report(new ProgressInfo((i + 1) / (double)chunks.Count, "Updating…"));
        }
        return new { ok = true };
    }
}
```

Tip: for the progress bar to actually animate, do the work in **chunks** with one `RunInRevitAsync`
per chunk — a single long call blocks Revit's UI thread, and the updates only render at the end.
Commands that don't implement `IProgressAware` are completely unaffected; SDK 1.0 extensions keep
working unchanged.

## 5. Writing a JS / UI extension

The host opens your page in its own WebView2 window and injects a `window.AT` bridge. **Any
framework works** (React, Vue, Svelte, vanilla) — the host just loads HTML and gives you `AT`.

### 5.1 The bridge

```js
// Call any command (built-in or from any C# extension). Returns a Promise.
const result = await window.AT.invoke("acme.sample.Hello", /* optional payload */ {});
```

`invoke` is correlated by request id, so concurrent calls are fine. The promise **resolves** with
the command's return value and **rejects** with the error message if the command threw. Minimal
page:

```html
<button id="run">Call Hello</button>
<pre id="out"></pre>
<script>
  document.getElementById("run").addEventListener("click", async () => {
    try {
      const r = await window.AT.invoke("acme.sample.Hello");
      out.textContent = JSON.stringify(r, null, 2);
    } catch (e) {
      out.textContent = "Error: " + (e?.message ?? e);
    }
  });
</script>
```

### 5.2 Discovering what you can call

You don't have to guess command names or payloads. The host exposes a catalog command:

```js
const { commands } = await window.AT.invoke("Host.GetCommands");
// each: { name, source, description, readOnly, destructive, exposedToMcp, inputSchema }
console.table(commands.map((c) => ({ name: c.name, source: c.source })));
```

- `name` — what you pass to `AT.invoke(name, payload)`.
- `source` — `"core"` for built-ins, otherwise the extension `id` that added the command.
- `inputSchema` — the JSON schema of the payload, so you know which arguments to send.

Every **registered** command is callable from JS — `HiddenFromMcp` only hides a command from the
AI's tool list, not from `AT.invoke`. So `Host.GetCommands` lists everything you can call, including
other extensions' commands.

For a readable, searchable view, open **AnalyseTool tab → Settings → For developers — command reference**: a live table of
every command with its source, description, payload shape and flags (read-only / destructive /
MCP). That's the quickest way to browse what's available while you build.

### 5.2a Your page is a separate application

`window.AT` is the entire contract between the host and your page. The page loads in its own
WebView, from its own document, as its own bundle — the host injects the bridge and nothing else.
There is no shared component library, no shared theme, no shared stylesheet and no global component
registrations to inherit.

Practically that means three things:

- **Import every UI component in the file that renders it.** A component that is not registered does
  not raise an error; it renders nothing. The symptom is a page that looks half-built while the
  console stays clean — the hardest kind of bug to attribute.
- **Ship a stylesheet that sets the page background.** Without one the page inherits the WebView
  default rather than anything of the host's.
- **Choose your own theme.** Looking like part of the product is a good goal; reading the host's
  settings is not possible, and copying its setup file creates a dependency that breaks silently the
  day that file changes.

It is the same rule the C# side states, one layer up: a command sees only the SDK, a page sees only
`window.AT`.

### 5.3 Building a framework app (Vite)

Ship the built `dist` contents next to `plugin.json`. The one gotcha: the page loads from a
virtual host (`https://<host>/index.html`), so **assets must be relative**. In `vite.config`:

```js
export default {
  base: "./",              // relative asset paths — REQUIRED
  // ...
}
```

Then set `ui.entryHtml` to your built `index.html` (or a sub-path if you nest the dist).

### 5.4 Live dev with HMR

Set `ui.devUrl` in the manifest to your dev server and the window loads it instead of the built
files — full hot reload, `window.AT` injected the same way:

```json
"ui": { "devUrl": "http://127.0.0.1:5173" }
```

Dev loop: set `devUrl` → save → click the button → edit → HMR. **Remove `devUrl` before
release.**

> If you see `Unsafe attempt to load URL ... from frame with URL chrome-error://chromewebdata`,
> the dev server is unreachable. Pin it to IPv4 to avoid a localhost IPv6 mismatch:
> `server: { host: "127.0.0.1", port: 5173, strictPort: true }` and use
> `devUrl: "http://127.0.0.1:5173"`.

---

## 6. Build, reload, versions

**Source extensions** (the default) need no build: save a `.cs` under `src\` and AnalyseTool compiles it
and reloads the extension. The build lands in `bin\<year>\` (git-ignored) and is reused while the sources
do not change. A compile error keeps the last good build running and shows the error with file and line
on the extension's page. `lib\*.dll` next to `src\` are referenced too. For IntelliSense, open
`.editor\<id>.csproj` (written by «New button», git-ignored) in Visual Studio, VS Code or Rider — it only
feeds the editor; AnalyseTool still compiles the sources.

**DLL extensions** build as before, for the Revit year you want:
```
dotnet build -c Release                        # the year pinned in the csproj
dotnet build -c Release -p:RevitVersion=2027   # another year, no file edit
```
and deploy into the extension folder with the year subfolders inside it:
```
%LOCALAPPDATA%\AnalyseTool\extensions\acme.sample\
    plugin.json
    index.html
    2025\Acme.Sample.dll
    2027\Acme.Sample.dll
```
DLLs are **byte-loaded**, so the file on disk is never locked: overwrite it while Revit runs.

**Reload is automatic.** AnalyseTool watches the extension folders: a changed file reloads its extension,
and ribbon buttons appear, change and disappear without restarting Revit. In the pane a DLL project is a box
marked DLL with the years it has builds for; a button whose C# does not compile shows «!» (tap it: file, line,
message), an orange dot when its last run failed. Right-click › ✎ › **Code, files and versions…** opens the page of a
button or an extension: its files, its versions, turn it off, remove it (the folder moves to
`%LOCALAPPDATA%\AnalyseTool\trash\` with its history). `.zip` packages install in profile › **Packages**.

**Versions.** Each button folder (and each extension folder) is a git repository; its page shows:

| Action | What happens |
| --- | --- |
| **Save version** | Saves the current files with your message (a commit). |
| **History** | The versions, newest first, with author and time; per file too. |
| **Changes** | The diff of the unsaved files, or of any version against the one before it. |
| **Restore** | Brings back a version, or one file of it. Unsaved work is saved as a version first, and the restored state becomes a new version — nothing is lost. |
| **Run this version** | Pins a source extension to a version: the button runs that version's code whatever the files say, until you go back to the current files. The pin is yours (stored in the profile), not part of the folder. |

An installed package gets a version per install, so an update can be rolled back the same way.

---

## 7. Quick checklists

**Command-only extension**
- [ ] `plugin.json` with `id`, `entryAssembly`, **no** `ui`.
- [ ] One or more `IRevitTask` classes; model access only inside `RunInRevitAsync`.
- [ ] The DLL sits in `<extension>\<year>\` — one folder per Revit version you support.
- [ ] Output is just your DLL + `plugin.json` (SDK/Revit refs `Private=false`).
- [ ] Test: `await window.AT.invoke("<id>.<Command>")` from any extension page or the console.

**UI-only extension**
- [ ] `plugin.json` with `id`, `ui` (`entryHtml`, `tab`, `panel`, `button`), **no** `entryAssembly`.
- [ ] `index.html` calling `window.AT.invoke(...)`.
- [ ] If framework-built: `base: "./"` and ship `dist` next to `plugin.json`.

**Both** (like the sample): all of the above in one folder.

---

## 8. Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Command not found / `is IRevitTask` fails | Output carries its own SDK/Revit/Newtonsoft DLL copy — set those refs to `Private=false` / compile-only. |
| Button doesn't appear, is grey or has «!» | In the pane: grey — off the ribbon (−/+) or turned off; «!» — it does not compile (tap it); a DLL project without a build for this Revit year says so in its box. |
| Extension listed as **Not built** | No compiled DLL was found. It must be at `<extension>\<year>\<entryAssembly>` (or, as a fallback, in the extension root) — not in `bin\`. Set `OutDir` as in §4.1. |
| Extension listed as **Incompatible** | Builds exist but not for the running Revit; the tooltip names the years present. Build the missing one with `-p:RevitVersion=<year>`. |
| Worked before, broke after moving to the new layout | The year folder goes **inside** the extension (`<id>\2025\x.dll`), not above it. The old `extensions\<year>\<id>\` still loads — see the migration steps in §2. |
| Page is blank / assets 404 | Built SPA without `base: "./"` — assets resolve to absolute paths the virtual host can't serve. |
| Sub-path `entryHtml` won't load | The subfolder wasn't deployed to the extension folder, or (again) absolute asset base. |
| `chrome-error://chromewebdata` with `devUrl` | Dev server unreachable — pin to `127.0.0.1` + `strictPort`. |
| DLL "in use" when rebuilding | Shouldn't happen (byte-loading). If it does, you may be holding a handle elsewhere; the next reload re-reads fresh bytes. |
| UI freezes during a command | You did slow I/O *inside* `RunInRevitAsync`. Move it out; marshal only the model touch. |

---

## 9. Using your commands from AI (MCP)

Every command of an extension is also an MCP tool for AI clients (Claude Code, Claude Desktop, Cursor,
VS Code…), with **no extra work on your part** — unless it says `[RevitCommand(HiddenFromMcp = true)]`.
The built-in commands are tools when they are marked for it.

AnalyseTool 2.0 serves MCP **inside Revit** over Streamable HTTP — no separate exe:

- **Turn it on** in AnalyseTool → **Settings** (on by default in Debug builds). It takes effect at the next
  Revit start.
- **One address for every Revit version:** `http://localhost:48075/mcp`, one token in
  `%LOCALAPPDATA%\AnalyseTool\mcp.json`. The first Revit you open answers on it and hands a call to the version it
  names: every tool takes `"revit": "2026"`, `ListRevitVersions` lists the open ones, and a call without it goes to
  the Revit you used last.
- **Claude Code and Codex:** install the AnalyseTool plugin that ships with the add-in (`<add-in folder>\Plugin`) — no
  token to copy, plus skills for writing buttons (`new-button`). The pane › profile › AI clients shows the two commands:
  `claude plugin marketplace add "<add-in folder>\Plugin"` + `claude plugin install analysetool@analysetool`, or
  `codex plugin marketplace add "<add-in folder>\Plugin"` + `codex plugin add analysetool@analysetool`.
  Other clients get a snippet with the address and the token there.
- Only `localhost`, a bearer token on every request, no request from a web page (`Origin`) — a page in your
  browser cannot reach the model.
- **The tool list follows your extensions.** When an extension is added, changed or removed, AnalyseTool
  sends `tools/list_changed` and the client asks again.
- To make a command *useful* to an AI, give it a `Description`, mark it `ReadOnly`/`Destructive`, and declare
  `InputType = typeof(Args)` **and** `OutputType = typeof(Result)` (§4.5) — they become the tool's description,
  safety hints and schemas.

### 9.1 The other direction — letting the AI build with you

Everything above is an agent *calling* your commands. It can also build them with you: it reads the
authoring guide (`GetAuthoringGuide` serves the same
[`LLM.md`](https://github.com/Nikola1Davydov/AnalyzeTool/blob/main/source/AnalyseTool.Extensions/Authoring/LLM.md) that ships next to
the add-in), creates a button (`CreateExtension` — a `<Tab>.tab\<Panel>.panel\<Name>.pushbutton\` folder with its C#
and page), writes its code (`WriteExtensionFile`; compiler errors come back with file and line), checks it
(`BuildExtension`), runs it (`RunCommand` — also when its tool list does not show the new command yet) and saves a
version (`SaveExtensionVersion`). With the plugin for Claude Code or Codex, `/analysetool:new-button <what it should
do>` (Codex: `$new-button`) does that loop for you.

Two things worth knowing:

- **New buttons go to the authoring root** (`%LOCALAPPDATA%\AnalyseTool\extensions\`, or the folder named in profile ›
  Folders of buttons). A button that already lives in a shared team folder is changed where it lives, so a fix does
  not silently land in a different copy — and a fix there is everyone's fix.
- **Arbitrary C# (`ExecuteCode`) is off unless a person turns it on** (profile › AI clients › **Let AI clients run any C#**, i.e. `Scripting:Enabled`; on in Debug builds); an
  agent cannot flip it for itself. Buttons do not need it: their C# is compiled from their folder.

The mechanics — every command in the loop, the manifest it writes, the recovery paths — are in
[`LLM.md`](https://github.com/Nikola1Davydov/AnalyzeTool/blob/main/source/AnalyseTool.Extensions/Authoring/LLM.md), which is written to
be pasted into the agent rather than read end-to-end.

## 10. Publishing your extension

Everything above gets an extension running on **your** machine. To hand it to someone else you
need one zip that covers every Revit version — which is exactly the folder layout from §2, so
there is nothing new to learn.

**Build the package.** For C# extensions built against the SDK package, the SDK ships the
pipeline:

```
dotnet build -t:PackExtension
```

It builds the project for Revit 2025/2026/2027 (narrow it with `-p:AnalyseToolPackYears=2025;2026`),
lays out per-year DLLs in year subfolders with `plugin.json` / UI / assets at the root, and zips
it to `artifacts/<id>-<version>.zip` — the format your users install in the pane › profile › **Packages** ›
**Install** (a file or a repository). UI-only extensions need no build at all: zip the folder.

> **Not in 2.0 yet**: packages of a tab or a button folder (share those through a team folder in Folders of buttons),
> and update checks and the catalog below — the manifest fields are read, the pane does not act on them yet.

**`plugin.json` owns the version.** It travels inside the package and is what the installed
extension reports; a git tag lives only in your repository. Bump `version` there and let the tag
follow.

**Automatic updates, no server** (from the next version — see the note above). Put an update feed in the manifest
and the manager offers your users the new version by itself:

```json
"updateFeed": "github:you/your-repo"
```

That reads your repository's latest release and its zip asset. An HTTPS URL returning
`{ "version": "...", "downloadUrl": "..." }` works too, if you host elsewhere.

**Release from CI.** With a `.github/workflows/release.yml` that runs `PackExtension` and attaches
`artifacts/*.zip` to the release, publishing becomes `git tag v1.0.0 && git push --tags`. `LLM.md`
(next to the add-in, or `GetAuthoringGuide` over MCP) contains a ready workflow to copy (§7.1 there).


**Getting listed** (from the next version). The catalog rows of the **Packages** list is the "where do extensions come
from" list: repositories not installed yet, with their links, each with an **Install** button that downloads the package from the
publisher's own release. Two ways onto it:

- **The shipped list** — a `catalog.json` in the AnalyseTool repository. Open a PR with your entry.
- **A local catalog** — `%LOCALAPPDATA%\AnalyseTool\catalog.json`, same shape. This is the one to
  use for a company's internal extensions: point your people at your own repositories without
  waiting for a plugin release. An entry whose `id` matches a shipped one replaces it.

```json
{
  "entries": [
    {
      "id": "you.your-extension",
      "name": "Your Extension",
      "publisher": "You",
      "description": "One line about what it does.",
      "source": "github:you/your-repo",
      "website": "https://github.com/you/your-repo",
      "license": "MIT"
    }
  ]
}
```

`source` is the same value as `updateFeed`, and `website` is what a reader clicks — an entry with
only `website` still earns its place, it just says "download it yourself". Users who have a
repository that is on no list at all can paste it into **Install → From a repository**, which takes a
GitHub URL, `owner/repo`, `github:owner/repo` or an https feed.

Listing is a directory entry, not an endorsement: the package is always fetched from your release,
and the user accepts the third-party disclaimer before anything is installed.

Two traps worth knowing before your first release:

- Pass `-p:AnalyseToolExpectedVersion=<tag>` on tag builds. `PackExtension` then fails if the tag
  and `plugin.json` disagree, instead of shipping a package whose version nobody can explain.
- Publish **one** package per release. Re-running a workflow *edits* the existing release rather
  than replacing it, so a second zip piles up next to the first and the update feed refuses to
  guess which one is yours.

---

## Reference: the SDK surface

```csharp
// AnalyseTool.Sdk
public interface IRevitTask
{
    Task<object?> ExecuteAsync(IRevitContext context, CancellationToken cancellationToken);
}

public interface IRevitContext
{
    RevitPayload Payload { get; }
    Task<T> RunInRevitAsync<T>(Func<UIApplication, T> work);
    Task   RunInRevitAsync(Action<UIApplication> work);
}

public sealed class RevitPayload
{
    public T?     As<T>();    // deserialize the incoming JSON payload
    public string RawJson { get; }
}

// Optional metadata. Without a name argument the wire name is the class name.
[AttributeUsage(AttributeTargets.Class)]
public sealed class RevitCommandAttribute : Attribute
{
    public RevitCommandAttribute();
    public RevitCommandAttribute(string name);
    public string? Name { get; }
    public string? Description   { get; set; }  // shown to humans AND to the AI over MCP
    public bool    ReadOnly      { get; set; }  // the command only reads the model
    public bool    Destructive   { get; set; }  // the command may modify or delete
    public Type?   InputType     { get; set; }  // generates the JSON input schema
    public Type?   OutputType    { get; set; }  // SDK 1.2+: generates the JSON output schema
    public bool    HiddenFromMcp { get; set; }  // callable from JS, hidden from the AI tool list
}

// OPTIONAL (SDK 1.1+): implement alongside IRevitTask to report live progress (§4.6).
public sealed record ProgressInfo(double Fraction, string? Message = null);

public interface IProgressAware
{
    IProgress<ProgressInfo>? Progress { get; set; }
}
```

The working reference implementation is `samples/Acme.Sample/` — copy it as a starting point.
