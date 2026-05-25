# Writing AnalyseTool Extensions

AnalyseTool lets you add your own functionality **without rebuilding the host**. You drop a
folder into your local extensions directory, restart Revit (or hit **Reload**), and your code
shows up — a new command callable from JavaScript, a ribbon button, a UI page, or all three.

This guide is for **extension authors**. It covers the two kinds of extension, the manifest, the
C# command contract, the JS UI contract, and the build/deploy/reload loop.

---

## 1. The mental model

There are two kinds of extension, and they play different roles:

| Kind | What it ships | What it does |
| --- | --- | --- |
| **C# extension** | a `.dll` of command classes | **ADDS** commands to the host's shared command dispatcher |
| **JS / UI extension** | an HTML page (any framework) | **CONSUMES** commands by calling `window.AT.invoke(...)` |

> **The one principle:** C# extensions *add* commands to the Core; JS extensions *consume* them.

A single extension folder can be C#-only, UI-only, or both. The sample (`samples/Acme.Sample`)
is both: a `Hello` command in C# plus an `index.html` page with a button that calls it.

Every command — built-in or from any C# extension — is reachable through the same channels:

```
your page  ──AT.invoke("acme.sample.Hello")──▶  WebView2 transport
                                                      │
                                                      ▼
                                              CommandDispatcher  ──▶  your IRevitTask
                                                      ▲
(future) MCP / AI  ────────────────────────────────-─┘
```

The dispatcher is **transport-neutral**: today the WebView2 bridge calls it; an MCP server is
planned. Anything you write as an `IRevitTask` is automatically available to both — so **never**
touch the WebView, the network, or transport details from inside a command. Return a
serializable result; the transport delivers it.

---

## 2. Where extensions live

Extensions are organised **per Revit version** — the folder right under `extensions\` is the Revit
version year (`2025`, `2026`, …). The plugin only loads the folder matching the running Revit, so the
same machine can host builds for several versions side by side. The version folder replaces the old
`targetRevit` manifest field.

```
%LOCALAPPDATA%\AnalyseTool\extensions\2025\<your-id>\
    plugin.json        (required)
    <YourExt>.dll      (optional — C# commands)
    index.html         (optional — UI page)
    icon.png           (optional — ribbon button icon)
    ...any assets...
```

`<your-id>` is your `id` from `plugin.json`. Each extension is isolated: its C# DLL is loaded
into its own collectible `AssemblyLoadContext`, so two extensions can't collide and a single
**Reload** can swap one out.

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
| `version` | ✔ | SemVer string. Shown in Settings and appended to the window title (`Name - 1.0.0`). |
| `entryAssembly` | — | DLL file name. **Omit for a UI-only extension.** SDK compatibility is derived automatically from the DLL's `AnalyseTool.Sdk` reference — no `sdkVersion` field needed. The current host SDK version is shown in Settings → Environment. The target Revit version is the `extensions\<year>\` folder the extension sits in — no `targetRevit` field needed. |
| `ui` | — | **Omit for a command-only extension.** |
| `ui.entryHtml` | — | Page to open, relative to the folder. Default `index.html`. Sub-paths like `"app/index.html"` work. |
| `ui.devUrl` | — | Dev server URL (Vite/HMR). When set, the window loads this instead of the built files. **Remove for release.** |
| `ui.tab` | — | Ribbon tab to place the button on. Default `"AnalyseTool"`. |
| `ui.panel` | — | Ribbon panel within that tab. Default `"Extensions"`. |
| `ui.button.name` | — | Button label — also used as the extension's display name (Settings list, window title). |
| `ui.button.tooltip` | — | Button tooltip. |
| `ui.button.icon` | — | Icon path relative to the folder (must sit beside `plugin.json`). If missing, a default icon (colored square with the extension's initial) is drawn automatically. |

`ui.tab` / `ui.panel` are honored **live** — change them, hit Reload, and the button moves.
Empty custom tabs/panels are torn down automatically (the built-in "AnalyseTool" tab is never
touched).

---

## 4. Writing a C# command extension

### 4.1 Project setup

Reference the SDK and import the shared build props. The simplest path is to copy
`samples/Acme.Sample/Acme.Sample.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="..\..\AnalyseTool.Sdk\build\AnalyseTool.Extension.props" />

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
    <ProjectReference Include="..\..\AnalyseTool.Sdk\AnalyseTool.Sdk.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>

  <ItemGroup>
    <None Include="plugin.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
    <None Include="index.html"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  </ItemGroup>

</Project>
```

The shared props (`AnalyseTool.Sdk/build/AnalyseTool.Extension.props`) give you:

- The four configurations: **Debug R25 / Debug R26 / Release R25 / Release R26**.
- `TargetFramework = net8.0-windows`, `PlatformTarget = x64`.
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
var args = ctx.Payload.As<MyArgs>();   // strongly-typed
string raw = ctx.Payload.RawJson;      // or the raw JSON
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
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            return ctx.RunInRevitAsync<object?>(app =>
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
await ctx.RunInRevitAsync(app =>
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
public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
{
    var data = await httpClient.GetStringAsync(url, ct);          // off the Revit thread
    return await ctx.RunInRevitAsync(app => ApplyToModel(app, data)); // on it, briefly
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
the payload yourself with `ctx.Payload.As<T>()`; you just *declare the input type* so the host can
publish a JSON schema for it.

```csharp
using System.ComponentModel; // for [Description]

[RevitCommand("SetWallComment",
    Description = "Sets the Comments parameter on the given walls. Modifies the model.",
    Destructive = true,                          // -> MCP destructiveHint
    InputType = typeof(SetWallComment.Args))]    // -> generates the tool's input schema
public sealed class SetWallComment : IRevitTask
{
    public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
    {
        Args? args = ctx.Payload.As<Args>();     // deserialize as usual
        return ctx.RunInRevitAsync<object?>(app => { /* ...use args.ElementIds / args.Comment... */ return null; });
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
| `HiddenFromMcp = true` | Keeps the command callable from JS but **hides it from the AI's tool list**. Use for plugin-management or UI-only commands. Default: exposed. |

Notes:
- The type passed to `InputType` must be at least `internal` (so `typeof(...)` in the attribute can
  reference it) — a `private` nested type won't compile there. No-argument commands omit `InputType`.
- **Use a LEAN input type** — only the fields the caller actually sends. Don't reuse rich
  domain/output models (ones with Revit-type properties or deep nesting): the generated schema
  balloons (and gets truncated by a size cap). Define a small purpose-built record per command.
- Put a `[System.ComponentModel.Description("…")]` on each input field — it flows into the JSON
  schema as the field's description, so the AI gets per-argument guidance (curated quality, still
  auto-generated).

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

### 5.2 Building a framework app (Vite)

Ship the built `dist` contents next to `plugin.json`. The one gotcha: the page loads from a
virtual host (`https://<host>/index.html`), so **assets must be relative**. In `vite.config`:

```js
export default {
  base: "./",              // relative asset paths — REQUIRED
  // ...
}
```

Then set `ui.entryHtml` to your built `index.html` (or a sub-path if you nest the dist).

### 5.3 Live dev with HMR

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

1. **Build** your extension (pick the config matching your Revit, e.g. `Debug R25`):
   ```
   dotnet build Acme.Sample.csproj -c "Debug R25"
   ```
2. **Copy** the output into your extensions folder:
   ```
   %LOCALAPPDATA%\AnalyseTool\extensions\2025\acme.sample\
       Acme.Sample.dll
       plugin.json
       index.html
       (icon, assets, ...)
   ```
3. **Load it:**
   - First time / new button: **restart Revit** (the static ribbon hook runs at startup).
   - Already-known extension, changed code/manifest: open the **AnalyseTool tab → Settings →
     Reload** (or the **Reload** ribbon button). No restart needed.

**Reload** does a true live reload: it re-reads the manifests, unloads the old collectible
`AssemblyLoadContext`, and loads the new DLL bytes. DLLs are **byte-loaded** (read into memory),
so the file on disk is never locked — you can overwrite the DLL while Revit is running, then
Reload.

The **Settings** page (AnalyseTool tab → Settings) lists every installed extension with its
version, target Revit, compatibility, and whether it has C# commands / UI — plus **Open folder**
and **Reload** buttons.

---

## 7. Quick checklists

**Command-only extension**
- [ ] `plugin.json` with `id`, `entryAssembly`, **no** `ui`.
- [ ] One or more `IRevitTask` classes; model access only inside `RunInRevitAsync`.
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
| Button doesn't appear | New button needs a **Revit restart** (not just Reload) the first time. Check the extension sits in the `extensions\<year>\` folder matching the running Revit. |
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
AI client  ──stdio(MCP)──▶  AnalyseTool.Mcp.exe  ──localhost WebSocket──▶  in-Revit bridge
                                                                                  │
                                                                                  ▼
                                                                          CommandDispatcher
```

- `AnalyseTool.Mcp.exe` is a tiny stdio server that ships with the plugin (at
  `<plugin>\mcp\AnalyseTool.Mcp.exe`). The AI client spawns it.
- It forwards each tool call over a localhost WebSocket to a bridge **inside Revit**, which calls
  the same `CommandDispatcher` your commands are registered in.
- It **discovers commands live**: when the AI lists tools, the bridge returns the current command
  set, so your extension's commands appear as tools automatically (`acme.sample.Hello` →
  a tool named `acme_sample_Hello`). Tool arguments are passed straight through as your command's
  JSON payload (the same thing `ctx.Payload` deserializes).

**To turn it on:** open the **AnalyseTool tab → Settings → MCP server**, pick a port, click
**Start**, then copy the generated **Claude Desktop config** snippet into your client's MCP config.
The snippet looks like:

```json
{
  "mcpServers": {
    "analysetool-revit": {
      "command": "C:\\...\\AnalyseTool\\mcp\\AnalyseTool.Mcp.exe",
      "args": ["--port", "17890"]
    }
  }
}
```

Notes:
- Start Revit (with the MCP server enabled) **before** the AI client lists tools — if Revit is down
  at that moment the tool list comes back empty until the client refetches.
- Nothing extra is required in your extension. To make a command *useful* to an AI, give it a
  `Description`, mark it `ReadOnly`/`Destructive`, and declare `InputType = typeof(Args)` (see §4.5)
  — that becomes the tool's description, safety hints, and input schema automatically.

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

[AttributeUsage(AttributeTargets.Class)]
public sealed class RevitCommandAttribute : Attribute   // [RevitCommand("WireName")]
{
    public RevitCommandAttribute(string name);
    public string Name { get; }
}
```

The working reference implementation is `samples/Acme.Sample/` — copy it as a starting point.
