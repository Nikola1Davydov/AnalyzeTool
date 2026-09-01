# Writing AnalyseTool Extensions

AnalyseTool lets you add your own functionality **without rebuilding the host**. You drop a
folder into your local extensions directory, restart Revit (or hit **Reload**), and your code
shows up — a new command callable from JavaScript, a ribbon button, a UI page, or all three.

This guide is for **extension authors**. It covers the three kinds of extension, the folder
layout, the manifest, the C# command contract, the JS UI contract, the build/deploy/reload loop,
and how to publish so your users get updates.

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
| Where | `%LOCALAPPDATA%\AnalyseTool\extensions-dist\` | `%LOCALAPPDATA%\AnalyseTool\extensions\` + any roots you add in Settings |
| Owned by | the extension manager — install / remove / update | you; nothing is ever rewritten behind your back |
| In Settings | **Installed** tab, with update badges | same list with a **Dev** badge |

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
| `version` | ✔ | SemVer string. Shown in Settings and appended to the window title (`Name - 1.0.0`). This is the single source of truth for the extension's version — the packaging pipeline reads it. |
| `entryAssembly` | — | DLL file name. **Omit for a UI-only or script extension.** Resolved in the Revit-year subfolder first (`2025\`), then the folder root — no `targetRevit` field needed, the year folders are the declaration. SDK compatibility is derived automatically from the DLL's `AnalyseTool.Sdk` reference — no `sdkVersion` field either. The current host SDK version is shown in Settings → Environment. |
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

Each entry may also carry `kind` — `push` (default), `stacked` or `pulldown`. Consecutive `stacked`
entries fill rows of three, the shape Revit's own stacked items make; a `pulldown` lists its `items`
under one head and does not run the first of them on click. An unknown `kind` falls back to `push`,
so a manifest written against a later host still produces a usable ribbon.

Each entry may carry `entryHtml`, `dockable`, `tab`, `panel` and `order`, each falling back to the
`ui.*` value when omitted — so a single-button manifest never repeats itself. `name`, `tooltip`,
`icon` and `command` work exactly as in the singular form.

The singular `ui.button` is not deprecated: for one surface it stays the clearer choice, and existing
manifests need no change. When both are present, `ui.buttons` wins.

| `ui.tab` | — | Ribbon tab to place the button on. Default `"AnalyseTool"`. |
| `ui.panel` | — | Ribbon panel within that tab. Default `"Extensions"`. |
| `ui.button.name` | — | Button label — also used as the extension's display name (Settings list, window title). |
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

> **Tip:** you don't have to write this by hand — **AnalyseTool tab → Settings → New template → C#**
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

For a readable, searchable view, open **AnalyseTool tab → Settings → Commands**: a live table of
every command with its source, description, payload shape and flags (read-only / destructive /
MCP). That's the quickest way to browse what's available while you build.

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
   - Already-known extension, changed code/manifest: open the **AnalyseTool tab → Settings →
     Reload** (or the **Reload** ribbon button). No restart needed.

**Reload** does a true live reload: it re-reads the manifests, unloads the old collectible
`AssemblyLoadContext`, and loads the new DLL bytes. DLLs are **byte-loaded** (read into memory),
so the file on disk is never locked — you can overwrite the DLL while Revit is running, then
Reload.

The **Settings** page (AnalyseTool tab → Settings) is the extension manager. It lists **Installed**
packages and your **Dev** folders separately, each row showing the version, whether it has C#
commands / UI, an enable/disable switch, **Open folder**, and — for installed packages with an
`updateFeed` — an update badge. There is also **Install from file…** for a `.zip`, a global
**Reload**, the host **Environment** (Revit / SDK / plugin version), the **Extension paths** it
scans, the **Commands** catalog (§5.2), and the **MCP server** controls.

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

**To turn it on:** open the **AnalyseTool tab → Settings → MCP server**, pick a port, click
**Start**, then copy the generated **Claude Desktop config** snippet into your client's MCP config.
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
- **Copy the snippet from Settings, don't retype it.** The `--token` value is a per-machine secret
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
- **Where the script lands is your choice.** Settings names the dev folder new scripts are saved
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
it to `artifacts/<id>-<version>.zip` — the format your users install via Settings →
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
