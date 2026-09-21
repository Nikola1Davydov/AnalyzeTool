# Writing AnalyseTool Extensions

AnalyseTool lets you add your own functionality **without rebuilding the host**. You drop a
folder into your local extensions directory, restart Revit (or hit **Reload**), and your code
shows up — a new command callable from JavaScript, a ribbon button, a UI page, or all three.

This guide is for **extension authors**. It covers the three kinds of extension, the folder
layout, the manifest, the C# command contract, the JS UI contract, the build/deploy/reload loop,
and how to publish so your users get updates. §11 is for the people who roll the tool out in a
company — BIM coordinators, IT, and users joining a company's configuration.

---

## 1. The mental model

There are three kinds of extension, and they play different roles:

| Kind | What it ships | What it does | Build needed? |
| --- | --- | --- | --- |
| **C# extension** | a `.dll` of command classes | **ADDS** commands to the host's shared command dispatcher | yes |
| **JS / UI extension** | an HTML page (any framework) | **CONSUMES** commands by calling `window.AT.invoke(...)` | no |
| **Script extension** | a plain `.cs` file | ADDS commands too — compiled at load time by Roslyn | no — see the caveat |

> **The one principle:** C# and script extensions *add* commands to the Core; JS extensions
> *consume* them.

A single extension folder can be any of these, or a combination. The sample
(`samples/Acme.Sample`) is C# + UI: a `Hello` command plus an `index.html` page with a button
that calls it.

**If you are writing an extension for other people, write a C# one.** A script skips the build
step, but "no build" does not mean "no compiler" — it means the compiler runs on your user's
machine, at load time, inside Revit, where a syntax error is a red banner rather than something
you saw and fixed. And because a script has no per-year folders (§2), it cannot declare which
Revit versions it supports: code that is valid on 2025 and invalid on 2027 stays invisible until
someone on 2027 opens Revit, and the manager cannot flag it as incompatible either. Everything
below about packaging and updates assumes per-year DLLs.

Scripts earn their place elsewhere: one-off automation you keep to yourself, and the AI path —
an agent trying something over MCP, or **Save as command** promoting a snippet that worked into a
permanent one.

Every command — built-in or from any C# extension — is reachable through the same channels:

```
your page  ──AT.invoke("acme.sample.Hello")──▶  WebView2 transport
                                                      │
                                                      ▼
                                              CommandDispatcher  ──▶  your IRevitTask
                                                      ▲
AI client  ─────────────── MCP server ───────────────┘
```

The dispatcher is **transport-neutral**: the WebView2 bridge and the MCP server (AI clients such
as Claude Desktop — see §9) both call it. Anything you write as an `IRevitTask` is automatically
available to both — so **never** touch the WebView, the network, or transport details from inside
a command. Return a serializable result; the transport delivers it.

---

## 2. Where extensions live

One extension = one folder, sitting **directly** under an extensions root. Inside it, the Revit
year is a **subfolder** holding that year's binaries:

```
%LOCALAPPDATA%\AnalyseTool\extensions\<your-id>\
    plugin.json        (required)
    2025\<YourExt>.dll (C# commands — one folder per Revit year you ship)
    2027\<YourExt>.dll
    *.cs               (script commands — version-independent, always in the root)
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

Scripts and `ui/` always come from the root — they are version-independent.

Each extension is isolated: its C# DLL is loaded into its own collectible `AssemblyLoadContext`,
so two extensions can't collide and a single **Reload** can swap one out.

### Two zones

| | **Installed** | **Dev / Local** |
| --- | --- | --- |
| Where | `%LOCALAPPDATA%\AnalyseTool\extensions-dist\` | `%LOCALAPPDATA%\AnalyseTool\extensions\` + any folders you add in Extensions → *Folders scanned* |
| Owned by | the extension manager — install / remove / update | you; nothing is ever rewritten behind your back |
| In the Extensions window | **Installed** section, with update badges | **Your own** section, below it |

As an author you work in the dev zone. The managed zone is what your users get when they install
your `.zip`.

### Migrating from the old layout

Before this format, extensions lived in `extensions\<year>\<id>\` — the year **above** the
extension. Those folders **still load**, unchanged; nothing you have deployed breaks. But the
year-above layout is deprecated: it cannot express one extension supporting several Revit
versions, which is the whole point of the package format.

To convert, move the year inside and merge the copies:

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

Three rules cover every case:

- **DLLs** go into a `<year>\` subfolder — one per Revit version you built for.
- **Everything else** — `plugin.json`, `*.cs` scripts, `index.html`, `ui/`, `icon.png`, assets —
  goes in the root, exactly once. These files were duplicated per year before; they are
  version-independent, so keep a single copy.
- **`plugin.json` needs no edit.** There was never a `targetRevit` field; the year folders are the
  declaration. If you have an old manifest that still carries one, delete the line.

Then point your build at the new location — set `<OutDir>$(MSBuildProjectDirectory)\$(RevitVersion)\</OutDir>`
as in §4.1, and the DLL lands in the right subfolder by itself. Hit **Reload**; if the extension
shows **"Not built"**, the DLL is not in `<year>\` or in the root (see §8).

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
| `version` | ✔ | SemVer string. Shown in the Extensions window and appended to the window title (`Name - 1.0.0`). This is the single source of truth for the extension's version — the packaging pipeline reads it. |
| `entryAssembly` | — | DLL file name. **Omit for a UI-only or script extension.** Resolved in the Revit-year subfolder first (`2025\`), then the folder root — no `targetRevit` field needed, the year folders are the declaration. SDK compatibility is derived automatically from the DLL's `AnalyseTool.Sdk` reference — no `sdkVersion` field either. The current host SDK version is shown in Settings → About. |
| `description` | — | One line, shown in the extension listing. |
| `publisher` | — | You or your company. Shown next to the extension name. |
| `website` / `supportUrl` | — | Links shown in the listing. Recommended when publishing. |
| `icon` | — | Extension-level PNG (relative path) for the listing; falls back to `ui.button.icon`. |
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
| `ui.button.icon` | — | Icon path relative to the folder (must sit beside `plugin.json`). If missing, a default icon (colored square with the extension's initial) is drawn automatically. |
| `ui.button.command` | — | Run this command when the button is clicked, instead of opening `entryHtml`. Use it for a one-shot action that needs no page. |

`ui.tab` / `ui.panel` are honored **live** — change them, hit Reload, and the button moves.
Empty custom tabs/panels are torn down automatically (the built-in "AnalyseTool" tab is never
touched).

---

## 4. Writing a C# command extension

### 4.1 Project setup

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
> scaffolds a ready-to-build project, a `plugin.json`, and an `LLM.md` (paste it into an AI to have it
> write commands for you).

With that `OutDir`, `dotnet build -c Release` already writes into `<project>\<year>\` — so if your
project folder *is* the extension folder (`plugin.json` beside the `.csproj`), there is nothing to
copy: build, hit Reload, done. Otherwise copy the whole folder — root files plus the `<year>\`
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
const { commands } = await window.AT.invoke("GetCommands");
// each: { name, source, description, readOnly, destructive, exposedToMcp, inputSchema }
console.table(commands.map((c) => ({ name: c.name, source: c.source })));
```

- `name` — what you pass to `AT.invoke(name, payload)`.
- `source` — `"core"` for built-ins, otherwise the extension `id` that added the command.
- `inputSchema` — the JSON schema of the payload, so you know which arguments to send.

Every **registered** command is callable from JS — `HiddenFromMcp` only hides a command from the
AI's tool list, not from `AT.invoke`. So `GetCommands` lists everything you can call, including
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

Dev loop: set `devUrl` → **Reload** → click the button → edit → HMR. **Remove `devUrl` before
release.**

> If you see `Unsafe attempt to load URL ... from frame with URL chrome-error://chromewebdata`,
> the dev server is unreachable. Pin it to IPv4 to avoid a localhost IPv6 mismatch:
> `server: { host: "127.0.0.1", port: 5173, strictPort: true }` and use
> `devUrl: "http://127.0.0.1:5173"`.

---

## 6. Build, deploy, reload

1. **Build** for the Revit year you want. With the NuGet setup from §4.1 the year is a property:
   ```
   dotnet build -c Release                        # the year pinned in the csproj
   dotnet build -c Release -p:RevitVersion=2027   # another year, no file edit
   ```
   In-repo (props imported by path) the year is the configuration instead:
   ```
   dotnet build Acme.Sample.csproj -c "Debug R25"
   ```
2. **Deploy** — the extension folder, with the year subfolders inside it:
   ```
   %LOCALAPPDATA%\AnalyseTool\extensions\acme.sample\
       plugin.json
       index.html
       icon.png
       2025\Acme.Sample.dll
       2027\Acme.Sample.dll        (if you built it)
   ```
   With `OutDir` set as in §4.1 and `plugin.json` beside the `.csproj`, the build already produced
   this — there is nothing to copy.
3. **Load it:**
   - First time / new button: **restart Revit** (the static ribbon hook runs at startup).
   - Already-known extension, changed code/manifest: press the **Reload** ribbon button (also
     inside the Extensions window). No restart needed.

**Reload** does a true live reload: it re-reads the manifests, unloads the old collectible
`AssemblyLoadContext`, and loads the new DLL bytes. DLLs are **byte-loaded** (read into memory),
so the file on disk is never locked — you can overwrite the DLL while Revit is running, then
Reload.

The **Extensions** window (AnalyseTool tab → Extensions) is the extension manager. It lists **Installed**
packages and **Your own** folders separately, each row showing the version, whether it has C#
commands / UI, an enable/disable switch, **Open folder**, and — for installed packages with an
`updateFeed` — an update badge. There is also **Install from file…** for a `.zip`, a global
**Reload**, the host **Environment** (Revit / SDK / plugin version), the **Extension paths** it
scans, the **Commands** catalog (§5.2), and the **MCP server** controls. The **Catalog** tab is the
other direction — the repositories extensions can be installed *from* (§10).

Every row also has **Edit** (the pencil): the ribbon button's name, tooltip, tab, panel, shape and
dock setting, plus description, publisher, links and update feed — written back into `plugin.json`
by merging, so fields the form does not show (`entryAssembly`, `devUrl`, `icon`, a second button)
stay as they are. The **id is never editable** there: it is the folder, the command namespace and the
key the enabled state is stored under. Installed packages open read-only — their manifest belongs to
the publisher.

Two red tags mean different things, and the difference is the fix:

| Tag | Meaning | What to do |
| --- | --- | --- |
| **Not built** | `entryAssembly` is declared but there is no compiled DLL anywhere — no `<year>\` folders, nothing in the root. | Build the project (§4.1), then **Reload**. A freshly scaffolded template shows this until its first build. |
| **Incompatible** | Builds exist, but not for the Revit you are running. The tooltip names the years it does ship. | Build that year too: `dotnet build -p:RevitVersion=<year>`. |
| **Error** | The extension loaded but a command threw while registering. | The tooltip carries the message; check the log in `%LOCALAPPDATA%\AnalyseTool\logs`. |

---

## 7. Quick checklists

**Command-only extension**
- [ ] `plugin.json` with `id`, `entryAssembly`, **no** `ui`.
- [ ] One or more `IRevitTask` classes; model access only inside `RunInRevitAsync`.
- [ ] The DLL sits in `<extension>\<year>\` — one folder per Revit version you support.
- [ ] Output is just your DLL + `plugin.json` (SDK/Revit refs `Private=false`).
- [ ] Test: `await window.AT.invoke("<id>.<Command>")` from any extension page or the console.

**Script extension** (personal or AI-authored only — not for distribution, see §1)
- [ ] `plugin.json` with `id` and **no** `entryAssembly`.
- [ ] One or more `.cs` files in the folder **root**, each with `IRevitTask` classes.
- [ ] Reload — Roslyn compiles them at load; errors show as the extension's diagnostics.
- [ ] Shipping this to someone? Make it a C# project instead.

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
| Button doesn't appear | New button needs a **Revit restart** (not just Reload) the first time. |
| Extension listed as **Not built** | No compiled DLL was found. It must be at `<extension>\<year>\<entryAssembly>` (or, as a fallback, in the extension root) — not in `bin\`. Set `OutDir` as in §4.1. |
| Extension listed as **Incompatible** | Builds exist but not for the running Revit; the tooltip names the years present. Build the missing one with `-p:RevitVersion=<year>`. |
| Worked before, broke after moving to the new layout | The year folder goes **inside** the extension (`<id>\2025\x.dll`), not above it. The old `extensions\<year>\<id>\` still loads — see the migration steps in §2. |
| Page is blank / assets 404 | Built SPA without `base: "./"` — assets resolve to absolute paths the virtual host can't serve. |
| Sub-path `entryHtml` won't load | The subfolder wasn't deployed to the extension folder, or (again) absolute asset base. |
| `chrome-error://chromewebdata` with `devUrl` | Dev server unreachable — pin to `127.0.0.1` + `strictPort`. |
| DLL "in use" when rebuilding | Shouldn't happen (byte-loading). If it does, you may be holding a handle elsewhere; Reload re-reads fresh bytes. |
| UI freezes during a command | You did slow I/O *inside* `RunInRevitAsync`. Move it out; marshal only the model touch. |

---

## 9. Using your commands from AI (MCP)

Every command — built-in **and** from any C# extension — is also exposed to AI clients (Claude
Desktop, etc.) over the **Model Context Protocol**, with **no extra work on your part**. The moment
your command is registered, it shows up as an MCP tool.

How it fits together:

```
AI client  ──stdio(MCP)──▶  AnalyseTool.Mcp.exe  ──localhost TCP──▶  in-Revit bridge
                                                                                  │
                                                                                  ▼
                                                                          CommandDispatcher
```

- `AnalyseTool.Mcp.exe` is a tiny stdio server that ships with the plugin (at
  `<plugin>\mcp\AnalyseTool.Mcp.exe`). The AI client spawns it.
- It forwards each tool call over a localhost TCP connection to a bridge **inside Revit**, which
  calls the same `CommandDispatcher` your commands are registered in.
- It **discovers commands live**: when the AI lists tools, the bridge returns the current command
  set, so your extension's commands appear as tools automatically (`acme.sample.Hello` →
  a tool named `acme_sample_Hello`). Tool arguments are passed straight through as your command's
  JSON payload (the same thing `revitContext.Payload` deserializes).

**To turn it on:** open the **AnalyseTool tab → Settings → Artificial intelligence**, switch on
**External assistant**, then open **Connection details** and copy the generated **Claude Desktop
config** snippet into your client's MCP config (the port lives there too). This is the *external*
assistant — the model picked under **Built-in assistant** does not apply to it; the client brings its own.
The snippet looks like:

```json
{
  "mcpServers": {
    "analysetool-revit": {
      "command": "C:\\...\\AnalyseTool\\mcp\\AnalyseTool.Mcp.exe",
      "args": ["--port", "17890", "--token", "<generated per machine>"]
    }
  }
}
```

Notes:
- **Copy the snippet from Settings → Connection details, don't retype it.** The `--token` value is a per-machine secret
  that authorizes the client against Revit: the bridge listens on 127.0.0.1, which keeps the network
  out but not other processes running as you, so every request must carry the token. Calls without it
  are refused.
- Start Revit (with the MCP server enabled) **before** the AI client lists tools — if Revit is down
  at that moment the tool list comes back empty until the client refetches.
- **Not every command is an AI tool.** Commands declared `HiddenFromMcp` (plugin management, the C#
  code-execution switch) are neither listed nor callable over MCP — the bridge enforces that on the
  invoke path, not just when building the tool list.
- Nothing extra is required in your extension. To make a command *useful* to an AI, give it a
  `Description`, mark it `ReadOnly`/`Destructive`, and declare `InputType = typeof(Args)` **and**
  `OutputType = typeof(Result)` (see §4.5) — that becomes the tool's description, safety hints, and
  both schemas automatically. A command that declares neither type is callable but opaque: free-form
  arguments, free-form answer, and nothing that can be chained or validated.

### 9.1 The other direction — letting the AI write the command

Everything above is an agent *calling* your commands. It can also write them, without you copying
files around: it reads the authoring guide over MCP (`GetAuthoringGuide` serves the same
[`LLM.md`](https://github.com/Nikola1Davydov/AnalyzeTool/blob/main/src/LLM.md) this repo ships),
saves a C# command, and — when the command needs a form —
saves the HTML/CSS/JS page and the ribbon button that opens it. If the script does not compile it
reads the error back and tries again, and it can read its own earlier source to refine rather than
replace it.

Two things worth knowing before you use it:

- **It is off unless a person turns it on.** Writing and running C# is behind the code-execution
  switch in Settings, which is deliberately not something an agent can flip for itself — the command
  that sets it is hidden from MCP entirely.
- **Where the script lands is your choice.** Extensions → *Folders scanned* names the dev folder new scripts are saved
  into, and refining a script that already exists writes it back to the folder it lives in — so a
  shared team folder registered as a source root keeps working, and a fix does not silently land in
  a different copy.

The mechanics — every command in the loop, the manifest it writes, the recovery paths — are in
[`LLM.md`](https://github.com/Nikola1Davydov/AnalyzeTool/blob/main/src/LLM.md), which is written to
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
it to `artifacts/<id>-<version>.zip` — the format your users install via Extensions →
**Install from file…**. Script- and UI-only extensions need no build at all: zip the folder.

**`plugin.json` owns the version.** It travels inside the package and is what the installed
extension reports; a git tag lives only in your repository. Bump `version` there and let the tag
follow.

**Automatic updates, no server.** Put an update feed in the manifest and the manager offers your
users the new version by itself:

```json
"updateFeed": "github:you/your-repo"
```

That reads your repository's latest release and its zip asset. An HTTPS URL returning
`{ "version": "...", "downloadUrl": "..." }` works too, if you host elsewhere.

**Release from CI.** With a `.github/workflows/release.yml` that runs `PackExtension` and attaches
`artifacts/*.zip` to the release, publishing becomes `git tag v1.0.0 && git push --tags`. The
generated `LLM.md` in every scaffolded extension contains a ready workflow to copy (§7.1 there).


**Getting listed.** Extensions → **Find extensions** is the "where do extensions come from" page: a list of
repositories with their links, each with an **Install** button that downloads the package from the
publisher's own release. Two ways onto it:

- **The shipped list** — `src/AnalyseTool.Core/Features/Extensions/Catalog/catalog.json` in the
  AnalyseTool repository. Open a PR with your entry.
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
repository that is on no list at all can paste it into **Install from repository…**, which takes a
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

## 11. Deploying in a company

Everything above is one seat. This section is about hundreds of them: how a company rolls
AnalyseTool out, keeps it configured, and keeps the configuration in the hands of the person who
actually changes it. It is written for three readers — the BIM coordinator (§11.2), the IT
administrator (§11.3) and everyone else (§11.4) — with the file they all share in §11.5.

### 11.1 The idea

**One file.** A company's configuration is a single `policy.json`: what is locked, where
extensions come from, which extensions every seat must have, where AI requests go, where logs go.
Every section of it is optional, and a file with only `{ "version": 1 }` is valid and changes
nothing. The schema is `docs/policy.schema.json` — point your editor at it and you get completion.

**Two places it can sit.** The plugin reads the policy from up to two layers, on top of the
user's own settings:

| Layer | Where | Put there by | Needs admin | The user can leave |
| --- | --- | --- | --- | --- |
| **Machine** | `%ProgramData%\AnalyseTool\policy.json` | IT, via GPO / Intune / SCCM | yes | no |
| **Organization** | `%LOCALAPPDATA%\AnalyseTool\org.json` (a policy the user **joined** by URL) | the user, from Settings — or the installer, or the machine file pointing at it | no | yes, unless the machine file says `enforced` |
| User | `%LOCALAPPDATA%\AnalyseTool\*.json` | the plugin | no | these *are* the user's settings |

The machine file usually holds nothing but a **pointer** to the real policy at a URL (§11.3).
That split is the whole point: IT touches a seat once; everything that changes afterwards lives at
an address the BIM coordinator owns. When both layers carry a section, the machine layer supplies it.

**Mandatory vs. default.** A policy value is a *default* unless its setting is listed under
`locked`. A default applies until the user makes their own choice, which then wins. A locked value
is *mandatory*: it wins over the user's choice, Settings shows it read-only with the reason, and
the command that would change it refuses with a message naming the organization and the file.
Three settings can be locked today: `codeExecution.enabled`, `extensions.roots`, `mcp.enabled`.
Everything else the policy does — required extensions, the source whitelist, managed AI
providers, sinks — is not a setting the user has, so it needs no lock.

**Nothing changes without a policy.** A seat with no machine file and no membership is exactly
the single-seat product described in §1–§10: no fetch, no telemetry, no lock. The plugin never
writes the machine file.

### 11.2 For BIM coordinators

**Read this paragraph first: whoever can write the policy can run code on every seat.** Through
`extensions.required` and its feed, a policy installs DLLs that load into Revit with each user's
rights. The pointer form moves that power from an admin-only folder to a URL or a folder you own
without admin rights — that is the feature, and it is the attack surface: a compromised account, a
Git branch anyone can push to, a SharePoint folder with too many editors. So:

- Keep `policy.json` in a Git repository with a **protected branch and review**, or in a folder
  with a deliberately **short write ACL**. Validate it in CI with `AnalyseTool.Cli policy validate`
  before it reaches anyone.
- Pin every required package with `sha256`. A feed that serves a different file is refused.
- **Sign** the policy (below) and hand the key's fingerprint to IT and to staff. Without a key,
  HTTPS plus the join preview plus the two points above are the whole defense.

#### Your first `policy.json`, step by step

The shortest path from nothing to a working company configuration, with a SharePoint library as
the home for everything. Every step is a plain file or one CLI call; nothing needs IT until the
optional last step.

**1. Make the folder.** In the BIM library create a folder — say `AnalyseTool` — and put two files
in it:

```
AnalyseTool/
  policy.json                 ← the policy (below)
  analysetool-source.json     ← { "id": "contoso.bimtools", "policy": "policy.json" }
```

The marker is what lets a seat *find* this folder: the plugin looks for it across every OneDrive
mount point and offers to join, and it also resolves the `bimtools` source when OneDrive's own
mapping is not available. Later add `catalog.json` and a `packages\` folder here as well.

**2. Write the policy.** Start minimal; every section is optional and can be added later:

```json
{
  "$schema": "https://raw.githubusercontent.com/Nikola1Davydov/AnalyzeTool/main/docs/policy.schema.json",
  "version": 1,
  "revision": 1,
  "organization": { "name": "Contoso BIM", "contact": "bim@contoso.com" },

  "sources": {
    "bimtools": {
      "sharepoint": "https://contoso.sharepoint.com/sites/BIM/Shared Documents/AnalyseTool",
      "markerId": "contoso.bimtools"
    }
  },

  "codeExecution": { "enabled": false },
  "locked": ["codeExecution.enabled"],

  "extensions": {
    "catalogUrl": "source:bimtools/catalog.json",
    "allowedFeeds": ["source:bimtools/", "github:contoso-bim/"],
    "allowInstallFromRepository": false
  }
}
```

What each line buys you: `organization` is what the join preview and the Settings panel show (a
policy without it cannot be joined); `revision` is what you bump on every change so a seat never
accepts an older copy; `sources.bimtools` names the library once so nothing below carries a path
that differs per seat; the `codeExecution` lock keeps arbitrary C# off; the `extensions` block
limits installs to your library and your GitHub organization. The `$schema` line gives you
completion and validation in VS Code.

**3. Validate it** with the CLI that ships inside the plugin folder (any seat that has the plugin):

```
"%AppData%\Autodesk\Revit\Addins\2025\AnalyseTool\AnalyseTool.Cli.exe" policy validate policy.json
```

Exit code 0 prints `OK`; 2 prints one line per problem (an unknown lock, a whitelist entry in
the wrong form, a source declaring two kinds, a required package whose source the whitelist
does not cover). Put the same call into the CI of the repository that holds the file.

**4. Add what people should have.** A required extension is a package zip plus a two-line feed in
`packages\`, published with `dotnet build -t:PackExtension` (§10):

```
packages/
  contoso.standards-1.0.0.zip
  contoso.standards.feed.json   ← { "version": "1.0.0", "downloadUrl": "contoso.standards-1.0.0.zip" }
```

and one entry in the policy — pin it (`AnalyseTool.Cli ext validate <zip>` prints the hash):

```json
"required": [
  { "id": "contoso.standards", "source": "source:bimtools/packages/contoso.standards.feed.json",
    "sha256": "3f9c…" }
]
```

Publishing a new version is: drop the new zip, edit the feed's `version`, update the `sha256`,
bump `revision`, re-sign. Every seat picks it up on its next Revit start.

**5. Sign it** (recommended as soon as more than a handful of people join, mandatory if IT rolls
out a pointer):

```
AnalyseTool.Cli policy keygen --out C:\keys                      # once; keep the .key out of SharePoint
AnalyseTool.Cli policy sign policy.json --key C:\keys\policy-signing.key   # after every change
```

Hand out the **fingerprint** it prints (or `policy fingerprint policy-signing.pub`) so people can
compare it in the join preview, and give IT the `.pub` for the pointer's `signingKey`.

**6. Roll it out.** Three ways, any mix:

- *Self-service:* people sync the library (SharePoint → **Sync**), open Revit, and accept the
  "Your organization publishes AnalyseTool settings — join?" banner. Or Settings → Organization →
  Join with the folder path.
- *At install:* `msiexec /i AnalyseTool-<ver>-SingleUser.msi POLICYURL="source:bimtools/policy.json" /qn`
  — but a `source:` pointer needs the library synced on that seat first, so for installs prefer an
  https location (a GitLab raw URL of the same file).
- *Enforced by IT:* the machine pointer in `%ProgramData%\AnalyseTool\policy.json` (§11.3) with an
  **https** `policyUrl` and the `signingKey`. A `source:` reference in the machine pointer resolves
  through the current user's OneDrive, which is fine for shared workstations only when everyone
  syncs the library.

**Changing it later.** Edit, bump `revision`, validate, sign, save. There is no second step: seats
re-read the file on their next start (ETag / timestamp), and the Organization panel shows the
new fetch time. Removing a required extension from the list stops enforcing it; it does not
uninstall it.

#### Owning `policy.json`

A complete, annotated policy. Delete what you do not need — every section is optional.

```jsonc
{
  "version": 1,
  // Required for a policy people JOIN: shown as "Managed by Contoso BIM" and in the join preview.
  "organization": { "name": "Contoso BIM", "contact": "bim-support@contoso.com" },

  // Seats below this plugin version see it in Settings → Organization, with the download link.
  "minimumVersion": "1.6.0",
  "update": { "downloadUrl": "https://git.contoso.com/bim/analysetool/raw/main/AnalyseTool-1.6.0-SingleUser.msi",
              "sha256": "3f1a…64 hex characters…" },

  // Mandatory settings. Anything set below but NOT listed here is only a default.
  "locked": ["codeExecution.enabled", "mcp.enabled"],
  "codeExecution": { "enabled": false },   // ad-hoc C# in the Revit process: the recommended enterprise default
  "mcp": { "enabled": false },             // the in-Revit MCP bridge for AI clients

  // Named locations. Everything below refers to them as source:<name>/<relative>, so one policy
  // serves every seat although the local path of a synced library differs per machine.
  "sources": {
    "bimtools": {
      "sharepoint": "https://contoso.sharepoint.com/sites/BIM/Shared Documents/AnalyseTool",
      "markerId": "contoso.bimtools",
      "syncUrl": "odopen://sync/?siteId=…"
    },
    "share": { "path": "\\\\fileserver\\revit\\analysetool" }
  },

  "extensions": {
    // Extra folders every seat scans (dev zone, not removable). Synced folders are fine.
    "roots": ["source:bimtools/extensions"],
    // Your catalog, in the same shape as catalog.json (§10). Tagged "organization" in Find extensions.
    "catalogUrl": "source:bimtools/catalog.json",
    // Prefix whitelist for update feeds and install sources. Absent = everything allowed.
    "allowedFeeds": ["https://git.contoso.com/bim/", "github:contoso-bim/"],
    // Installed when missing, updated when the feed is newer, never disabled or removed by the user.
    "required": [
      { "id": "contoso.standards",
        "source": "source:bimtools/packages/contoso.standards/feed.json",
        "sha256": "9c4e…64 hex characters…" }
    ],
    // false hides the free-form "Install from repository…" paste. Catalog entries still work.
    "allowInstallFromRepository": false
  },

  "ai": {
    "providers": [
      { "id": "contoso-gateway", "name": "Contoso AI Gateway",
        "baseUrl": "https://ai.contoso.com/v1", "apiKeyEnv": "ANALYSETOOL_AI_KEY" }
    ],
    "allowUserProviders": false
  },

  "logging": { "sink": "\\\\fileserver\\logs\\analysetool\\{user}\\", "level": "Information" },

  // Absent = nothing is sent. Read the telemetry paragraph before adding "command" or "ai".
  "telemetry": { "sink": "https://seq.contoso.com/api/events/raw", "identity": "hashed",
                 "events": ["inventory"] }
}
```

Unknown keys are ignored and listed as a problem in Settings → Organization, so a newer policy
still works on an older plugin. A joined seat re-fetches the policy on every Revit start (in the
background, with the cached ETag); a change you commit reaches a seat the next time Revit starts,
and an offline seat keeps the last copy.

#### Where to host it

A policy source is anything the plugin can read **without an interactive login**: an `https://`
URL or a file-system path (`%ENV%` is expanded). Plain `http://` is refused everywhere — a policy
installs code and redirects AI traffic, so it must not be tamperable in transit.

| Hosting | Form | Notes |
| --- | --- | --- |
| Git hosting (GitHub / GitLab raw, an internal GitLab) | `https://…/raw/main/policy.json` | the best choice: history, review, `policy validate` in CI |
| Azure Blob Storage with a SAS token | `https://…?sv=…` | cheap static hosting, no Azure AD on the client |
| Internal web server (IIS, nginx) | `https://…` | classic |
| File share | `\\server\bim\analysetool\policy.json` | a path, not a URL — same loader, protected by the share's ACL |
| **Synced SharePoint / OneDrive library** | a named source (below) | **the SharePoint answer** |
| SharePoint https link | — | **does not work.** The link needs an Azure AD sign-in; the plugin gets a 401 or a login page. "Anyone" links are tenant-disabled more often than not. |

Corporate proxies are handled: every fetch uses the system proxy with the user's Windows
credentials.

#### Named sources and the SharePoint library

Users have the BIM library on disk through the OneDrive client, but *where* differs by how each
person added it (**Sync** on the library, **Add shortcut to My files**, a synced sub-folder). A
path in the policy therefore cannot work. Instead the policy names **what** the folder is, and the
plugin finds **where** it is on each seat:

```json
"sources": {
  "bimtools": {
    "sharepoint": "https://contoso.sharepoint.com/sites/BIM/Shared Documents/AnalyseTool",
    "markerId": "contoso.bimtools",
    "syncUrl": "odopen://sync/?siteId=…&webId=…&listId=…&webUrl=…&listTitle=Shared%20Documents"
  }
}
```

`sharepoint` is the library or folder URL as you see it in the browser (the plugin matches its
site and library against what the OneDrive client recorded on the machine, then appends the rest
of the path). `syncUrl` is the `odopen://` link SharePoint's **Sync** button produces; it is shown
to a user whose seat has not synced the library. A `path` source (UNC share, local folder) and a
`url` source (an https base) exist so the same `source:` syntax covers every hosting kind.

Resolution is three fallbacks: the OneDrive client's own library-to-folder mapping; a **marker
file**; and a folder the user picks once when Settings → Organization asks "Where is *bimtools*
on this computer?" (kept per seat in `%LOCALAPPDATA%\AnalyseTool\sources.json`, never in the
policy; the same card offers **Connect the library** when the source carries a `syncUrl`). For the
marker, drop
`analysetool-source.json` into the folder root:

```json
{ "id": "contoso.bimtools", "policy": "policy.json" }
```

`id` must equal the source's `markerId`; the plugin scans every OneDrive mount point three levels
deep for it. `policy` is optional and serves **discovery**: a seat that joins with an empty input
(§11.4) looks for a marker across its synced folders and offers the policy the marker names
(relative to the folder, or a `policy.json` next to the marker when the key is absent).

Two rules of thumb for a synced library:

1. **Files On-Demand.** Files may be placeholders that download on first read. Right-click the
   AnalyseTool folder in Explorer and choose **Always keep on this device**. The plugin reads
   policy files off the UI thread with a timeout, so a slow sync delays the background pass, not
   Revit.
2. **Half-synced states.** A DLL and its `plugin.json` may arrive seconds apart. Bump `version` in
   the manifest *last*, after the binaries; for anything that matters, use a packaged feed with
   `sha256` (next paragraph) instead of loose folders.

Why a synced folder is a fine extension root: assemblies are loaded from a byte copy (§6), so
Revit holds no handle on the DLL and the sync client can replace it under a running Revit; the
next Reload picks it up.

#### Feeds and packages on a share

An update feed on a share or a synced library is the same JSON the https form uses (§10), read
from disk, and its `downloadUrl` may be **relative to the feed's own folder**:

```
source:bimtools/packages/contoso.standards/
    feed.json          { "version": "1.4.0", "downloadUrl": "contoso.standards-1.4.0.zip" }
    contoso.standards-1.4.0.zip
```

The zip comes out of `dotnet build -t:PackExtension` (§10). A feed on disk must serve a package on
disk — a file feed whose `downloadUrl` points at the internet is refused. Publishing a new
version is replacing two files.

#### Required extensions

```json
"required": [
  { "id": "contoso.standards",
    "source": "source:bimtools/packages/contoso.standards/feed.json",
    "sha256": "9c4e…" }
]
```

After Revit has started (a few seconds later, in the background, never on the startup path), the
plugin walks the list: an id that is missing is installed from `source`; one that is installed is
updated when the feed's version is newer; the package must carry the declared `id`; and when
`sha256` is present the downloaded zip must match it or is refused. The user cannot disable or
uninstall a required extension (the Extensions window shows a **required** badge), and an id the
user had disabled before the policy arrived is re-enabled. A copy of the same id in the dev zone
or in the machine zone satisfies the requirement as it is. `source` may be omitted when the
installed extension already declares an `updateFeed`. What happened to each entry is listed in
Settings → Organization.

#### Catalog, whitelist, and the download-host rule

- `catalogUrl` — your `catalog.json` (§10), an https URL (cached with its ETag) or a path. It is
  merged after the shipped catalog and before the user's own; user entries cannot override yours,
  and yours show an **organization** tag in Find extensions.
- `allowedFeeds` — a prefix whitelist over every update feed and install source:
  `"github:contoso-bim/"` (an owner; a bare owner is read as `owner/`) or `"https://host/path/"`.
  Absent means everything is allowed, as today. A `source:` reference is always allowed: the
  policy that declares the whitelist declares the source. With a whitelist in place, a catalog
  entry that is not on it shows as **not approved**.
- **The download-host rule.** The whitelist governs the feed; the package URL is whatever the feed
  serves. When a whitelist exists, a download is accepted only from the feed's own host, from
  GitHub's asset hosts for a `github:` feed, or from a host that itself matches the whitelist.
- `allowInstallFromRepository: false` hides the free-form **Install from repository…** paste and
  refuses the command behind it.

#### `minimumVersion` and `update`

A seat below `minimumVersion` shows an amber banner in Settings with a **Download** link.
`update.downloadUrl` and `update.sha256` tell the seat where the installer is and what it must
hash to. On a **per-user** install (SingleUser MSI) the plugin can finish the job itself: it
downloads the MSI, verifies the hash (the pin is required — no hash, no self-update), and hands
it to `AnalyseTool.Cli update wait-and-install`, which waits for Revit to exit and runs
`msiexec /qn`. A per-machine install only links: updating it is IT's step (§11.3).

#### Signing

Signing turns "trust the hosting" into "trust the key". A signature is a detached
`policy.json.sig` (ECDSA P-256 over the exact bytes of `policy.json`, base64) next to the policy;
the public key travels out of band as base64 `SubjectPublicKeyInfo`.

```
AnalyseTool.Cli policy keygen [--out <folder>]                 # writes policy-signing.key + .pub, prints the fingerprint
AnalyseTool.Cli policy sign policy.json --key policy-signing.key   # writes policy.json.sig — run it on every change
AnalyseTool.Cli policy verify policy.json --pub policy-signing.pub # exit code 0 when the signature holds
```

Keep `policy-signing.key` where the policy's write ACL is (keygen refuses to overwrite an
existing one); hand the **public key** (`.pub`, base64) to IT for the machine pointer's
`signingKey` (§11.3) and read the **fingerprint** (16 hex characters in groups of four,
`policy fingerprint policy-signing.pub`) out to staff who join by hand, so they can compare it in
the preview.

What happens on a seat: when it knows a key — from the pointer or typed once at Join — an
unsigned policy is refused, a policy whose signature does not match is refused, and a refresh that
fails verification keeps the last accepted copy. The cached copy in the seat's own `org.json` is
re-verified against the key on every start, so editing that file changes nothing. When no key is
known, nothing is checked and the preview says so — then the organization layer is exactly as
trustworthy as the user's profile folder, which is why a machine pointer should carry
`signingKey`. An inline machine file is never signed; it is trusted because only an
administrator can write `%ProgramData%`.

**Rollback.** A signature proves who wrote a file, not that it is the newest one: whoever can
write the hosting could serve an older, still validly signed policy. Put `"revision": <n>` in the
policy and bump it on every change; a seat refuses a fetched policy whose revision is lower than
the one it applied.

#### Telemetry

There is no telemetry in the single-seat product, and AnalyseTool the project has no endpoint.
**A seat without a policy, or with a policy that omits `telemetry`, sends nothing.** With a
`telemetry` section, data goes to the sink you name and nowhere else:

```json
"telemetry": {
  "sink": "https://seq.contoso.com/api/events/raw",   // or a folder: one .jsonl file per seat per day
  "identity": "hashed",                                // "hashed" (default) | "user"
  "events": ["inventory"]                              // absent = inventory only
}
```

- `sink` — an https endpoint that accepts newline-delimited JSON (Seq's raw ingestion, any
  collector with an NDJSON input), or a folder (a share, the synced library). Events are buffered,
  flushed every ten seconds, and **dropped** when the sink is down; they never delay a command.
- `events` — `inventory` (plugin and Revit version, installed extensions with version, zone and
  enabled state, join state; sent after every background pass) is the default and the harmless
  one. `command` (command name, transport, duration, outcome) and `ai` (provider, model, duration,
  outcome, token counts) are **per-seat usage** and must be listed deliberately.
- `identity` — `hashed` sends a stable per-seat hash so you can count seats without naming them;
  `user` sends the user and machine name.
- No event ever carries a payload, a file name, an element name or a parameter value; the event
  builders take names, numbers and outcomes only, and a test asserts it.

**Pseudonymous is not anonymous.** A stable per-seat hash is still personal data under the GDPR,
and per-employee usage records (`command`, `ai`) in Germany usually need a works-council
(Betriebsrat) agreement. The plugin cannot make that decision for you; it only makes the default
the harmless one. The join preview shows the telemetry section verbatim, so a user always sees
what a policy would send before accepting it.

#### `logging.sink`

`"logging": { "sink": "\\\\fileserver\\logs\\analysetool\\{user}\\", "level": "Information" }`
adds a second Serilog file sink; the local rolling file under `%LOCALAPPDATA%\AnalyseTool\logs`
stays. A folder gets `analysetool-<date>.log` inside it; `{user}`, `{machine}` and `%ENV%` are
expanded; one file per day, 31 kept. `level` defaults to `Information`. This is diagnostics —
exceptions and stack traces for support — not telemetry.

#### `ai.providers` and `apiKeyEnv`

```json
"ai": {
  "providers": [
    { "id": "contoso-gateway", "name": "Contoso AI Gateway", "type": "openaiCompatible",
      "baseUrl": "https://ai.contoso.com/v1", "apiKeyEnv": "ANALYSETOOL_AI_KEY", "timeoutSeconds": 120 }
  ],
  "allowUserProviders": false
}
```

Providers listed here appear on every seat as **managed**: usable, not editable, not deletable.
`type` is `openaiCompatible` (default) or `ollama`. The API key is never stored on the seat: it is
read from the environment variable named in `apiKeyEnv` (set by GPO or a login script, §11.3), or
omitted entirely when `baseUrl` is a gateway that holds the real key. `allowUserProviders: false`
hides the user's own providers and refuses new ones — it deletes nothing, and leaving the
organization brings them back.

### 11.3 For IT administrators

Your part is the MSI and, optionally, one small file. Everything that changes later lives at a
URL the BIM coordinator owns (§11.2); nothing here is AnalyseTool-specific machinery.

**1. The plugin.** Put `AnalyseTool-<version>-MultiUser.msi` on a share readable by domain
computers. In the GPO linked to the BIM workstations OU: *Computer Configuration → Policies →
Software Settings → Software Installation → New Package*, deployment **Assigned**. The MSI installs
as SYSTEM at the next boot into `%ProgramData%\Autodesk\Revit\Addins\<year>\`; new versions go in
through the package's *Upgrades* tab (the MSI carries a major upgrade). This is the one step that
legitimately needs admin rights: code loaded into Revit must arrive through a trusted path. It is
silent-install ready for other tools:

```
msiexec /i AnalyseTool-<version>-MultiUser.msi /qn
```

**2. The pointer.** Same GPO: *Computer Configuration → Preferences → Windows Settings → Files →
New File*, action **Replace**, destination `%ProgramData%\AnalyseTool\policy.json`, content:

```json
{ "version": 1, "policyUrl": "https://git.contoso.com/bim/analysetool/raw/main/policy.json",
  "enforced": true, "signingKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…" }
```

Written once; it changes only if the URL moves. The plugin follows `policyUrl` (an https URL, a
UNC path, or a `source:` reference) and applies that policy to every user of the machine as their
organization layer. `enforced: true` means users cannot leave it or join another. `signingKey` is
the coordinator's public key (§11.2, Signing): with it present, an unsigned or tampered policy is
refused. A pointer may not point at another pointer.

`enforced` only means something when the target is not user-writable — an https host the user
does not control, or a share with read-only ACLs. Pointing an enforced policy at a folder under
the user's own profile is a contradiction the plugin does not detect: the user can edit the file.

*Alternative:* put the full policy inline into the same file instead of a pointer. Then every
change travels through GPO, and the file is trusted by location (no signature involved).

**Intune / Azure AD only.** Same shape: the MSI as a Win32 app (`msiexec /i … /qn`), the pointer
written by an Intune PowerShell script.

**3. Optional: the AI key.** *Preferences → Environment* (or the login script) for the variable
named in the policy's `ai.providers[].apiKeyEnv`, for example `ANALYSETOOL_AI_KEY`. The plugin
reads it at call time and never stores it.

**4. Optional: pre-installed packages.** *Preferences → Folders/Files* can drop ready extension
packages into `%ProgramData%\AnalyseTool\extensions-dist\<id>\` — the unpacked package layout
from §2 (`plugin.json` at the root, DLLs in `<year>\`). That root is scanned read-only for every
user of the machine (a **machine** badge; no install, update or uninstall there) and is first in
load order, so a machine copy wins over a user-installed one with the same id. Use it only when an
extension must not be user-writable; otherwise `extensions.required` plus a feed keeps updates in
the coordinator's hands without an IT ticket.

**Verify a seat** without opening Revit, with the CLI that ships inside the plugin folder
(`…\Addins\<year>\AnalyseTool\AnalyseTool.Cli.exe`, next to `AnalyseTool.Core.dll`):

```
AnalyseTool.Cli policy show     # the effective configuration with the origin of every setting
AnalyseTool.Cli org status      # joined where, enforced, signed, last fetch
AnalyseTool.Cli ext list        # installed extensions per zone
AnalyseTool.Cli diag collect    # a zip of logs, versions, policy and extension list for support
```

### 11.4 For everyone else: joining your company's configuration

If you installed the plugin yourself — the SingleUser MSI, no admin rights, a contractor's
laptop — there is no machine file. You adopt the company's configuration from Settings, and you
can drop it again.

**Join.** *AnalyseTool tab → Settings → Organization → Join organization…* and enter one of:

| You type | The plugin tries |
| --- | --- |
| a full `https://` URL | that URL as is |
| a domain, `contoso.com` | `https://contoso.com/.well-known/analysetool/policy.json` |
| a folder, `\\server\bim\analysetool` or a synced library path | `policy.json` in that folder |
| a `source:` reference | the named source |
| nothing | the well-known URL of the domain this computer is joined to, then a marker file (`analysetool-source.json`) across your synced OneDrive folders |

**The preview is the consent.** Before anything is applied you see the organization's name and
contact, whether the policy is signed (and the key's fingerprint, to compare with what your
coordinator told you), which settings become locked, which folders and catalog are added, which
extensions will be installed and from where, and the AI, logging and telemetry sections as they
are. Nothing is applied until you confirm. Then the policy takes effect, required extensions
install in the background, and the panel reads *Managed by <name>*. A policy that does not name an
organization and a contact cannot be joined.

**Leave.** *Leave organization* forgets the membership, releases the locks and gives your own
settings back — they were never overwritten. Extensions that were installed because the policy
required them are listed so you can uninstall them; nothing is removed for you. If the computer's
administrator set the organization (§11.3, `enforced`), Join and Leave are both refused.

**Joining at install time.** A coordinator can hand you a command line that joins before Revit
ever starts:

```
msiexec /i AnalyseTool-<version>-SingleUser.msi POLICYURL=https://git.contoso.com/bim/analysetool/raw/main/policy.json /qn
```

**One MSI per machine.** The SingleUser and MultiUser installers must not coexist on one
machine — Revit would load both `.addin` files. If IT deployed the MultiUser MSI, do not install
the SingleUser one on top.

**What a required extension means.** It cannot be disabled or uninstalled while you are joined
(the Extensions window shows a **required** badge instead of those actions), it updates itself
when the company publishes a new version, and if you had disabled it before joining it is
switched back on. Everything else in the Extensions window works as before.

### 11.5 The `policy.json` reference

The full schema is `docs/policy.schema.json`; this is the short form. Every key is optional.

| Key | Meaning |
| --- | --- |
| `version` | Format version; always `1`. |
| `policyUrl` | Pointer form (machine file only): the real policy lives at this https URL, path or `source:` reference. Nothing else in the file matters then; pointers do not chain. |
| `enforced` | Pointer form: users cannot leave or join another organization. |
| `signingKey` | Pointer form: base64 SubjectPublicKeyInfo (ECDSA P-256). With it, the policy at `policyUrl` must carry a valid `policy.json.sig`. |
| `organization.name`, `organization.contact` | Shown as "Managed by …"; both required for a policy people join. |
| `minimumVersion` | Seats below it see the minimum in Settings and via the CLI. |
| `update.downloadUrl`, `update.sha256` | Where the installer is and its hash. |
| `locked[]` | Settings the user may not change: `codeExecution.enabled`, `extensions.roots`, `mcp.enabled`. Anything set but not listed is a default. |
| `codeExecution.enabled` | Ad-hoc C# execution in the Revit process. Recommended: `false` and locked. |
| `mcp.enabled` | Whether the in-Revit MCP bridge may run. |
| `sources.<name>` | A named location: exactly one of `sharepoint` (library URL), `path` (UNC or local, `%ENV%`), `url` (https base); plus `markerId` and `syncUrl` for a SharePoint library. Referenced as `source:<name>/<relative>`. |
| `extensions.roots[]` | Extra folders every seat scans, as dev zone, not removable. `%ENV%` and `source:` resolved. |
| `extensions.catalogUrl` | Your catalog (`catalog.json` shape), https or path; merged after the shipped one and before the user's. |
| `extensions.allowedFeeds[]` | Prefix whitelist for feeds and install sources (`github:owner/`, `https://host/path/`). Absent = all. Also constrains the package host. |
| `extensions.required[]` | `{ id, source?, sha256? }`: installed when missing, updated when newer, never disabled or removed. |
| `extensions.allowInstallFromRepository` | `false` hides the free-form paste; the catalog still works. |
| `ai.providers[]` | `{ id, name?, type?, baseUrl, apiKeyEnv?, timeoutSeconds? }` — managed providers; key from the environment or a gateway. |
| `ai.allowUserProviders` | `false` hides the user's own providers and refuses new ones; deletes nothing. |
| `logging.sink`, `logging.level` | A second daily log file; `{user}`, `{machine}`, `%ENV%` expanded. |
| `telemetry.sink` | https NDJSON endpoint or a folder. Absent section = nothing is sent. |
| `telemetry.identity` | `hashed` (default, per-seat hash) or `user` (names). |
| `telemetry.events[]` | `inventory`, `command`, `ai`. Absent = `inventory` only. |

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

// OPTIONAL (SDK 1.3+): read-only access to the company policy (§11) and the company's telemetry sink.
// Both are static, registered by the host at startup; RegisterReader / RegisterSink are host-only.
public static class HostPolicy
{
    public static string? GetSectionJson(string section);   // the top-level section as JSON text, or null
    public static T?      GetSection<T>(string section);    // deserialized, or default when absent/unreadable
}

public static class HostTelemetry
{
    // No-op unless the policy names a telemetry sink AND lists eventKind in telemetry.events.
    // Never throws, never blocks. Pass names, numbers and outcomes — never model data.
    public static void Emit(string eventKind, IReadOnlyDictionary<string, object?> properties);
}
```

An extension reads its own section of `policy.json` — a top-level key named after the extension,
which the host neither knows nor validates — instead of shipping a second configuration channel.
`null` means no policy, or no such section; it never throws:

```csharp
internal sealed record AcmeStandards(string? Server, bool Strict);

// In policy.json: { "acme.standards": { "server": "https://std.acme.local", "strict": true } }
AcmeStandards? standards = HostPolicy.GetSection<AcmeStandards>("acme.standards");
string server = standards?.Server ?? "https://std.acme.local";   // your default when no policy applies
HostTelemetry.Emit("acme.standards.check", new Dictionary<string, object?> { ["outcome"] = "ok", ["durationMs"] = 42 });
```

The working reference implementation is `samples/Acme.Sample/` — copy it as a starting point.
