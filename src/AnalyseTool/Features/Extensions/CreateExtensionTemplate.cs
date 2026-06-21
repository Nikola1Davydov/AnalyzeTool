using AnalyseTool.Common;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.IO;

namespace AnalyseTool.Features.Extensions
{
    /// <summary>
    /// Scaffolds an extension on disk in one of three flavours:
    ///   • <c>UiOnly</c>  — plugin.json + index.html (plain HTML/CSS/JS, no build).
    ///   • <c>Csharp</c>  — plugin.json + csproj + Hello.cs + README (built with <c>dotnet build</c>).
    ///   • <c>Combo</c>   — both.
    /// C# files reference the SDK by absolute HintPath to the currently installed
    /// <c>AnalyseTool.Sdk.dll</c>, so authors always build against the running host version.
    /// </summary>
    [RevitCommand(
        Description = "Creates an extension template (UI only, C# commands, or both).",
        InputType = typeof(CreateExtensionTemplatePayload),
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal class CreateExtensionTemplate : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext context, CancellationToken cancellationToken)
        {
            CreateExtensionTemplatePayload? payload = context.Payload.As<CreateExtensionTemplatePayload>();
            if (payload is null)
                throw new InvalidOperationException("Payload is missing.");

            if (string.IsNullOrWhiteSpace(payload.FolderName))
                throw new InvalidOperationException("Folder name is required.");

            if (payload.PluginJson is null)
                throw new InvalidOperationException("plugin.json payload is required.");

            bool hasUi = payload.Kind is "UiOnly" or "Combo";
            bool hasCsharp = payload.Kind is "Csharp" or "Combo";
            if (!hasUi && !hasCsharp)
                throw new InvalidOperationException($"Unknown template kind: '{payload.Kind}'.");

            if (hasUi)
            {
                if (payload.PluginJson.Ui is null || string.IsNullOrWhiteSpace(payload.PluginJson.Ui.EntryHtml))
                    throw new InvalidOperationException("ui.entryHtml is required for UI templates.");
            }
            if (hasCsharp && string.IsNullOrWhiteSpace(payload.PluginJson.Id))
                throw new InvalidOperationException("Plugin id is required for C# templates.");

            string safeFolderName = SanitizeFolderName(payload.FolderName);
            string version = Context.UiApplication.Application.VersionNumber;
            string root = ResolveTargetRoot(payload.TargetRoot);
            string extensionRoot = Path.Combine(root, version, safeFolderName);

            if (Directory.Exists(extensionRoot))
                throw new InvalidOperationException($"Extension folder already exists: {safeFolderName}");

            Directory.CreateDirectory(extensionRoot);

            List<string> filesCreated = new();

            // plugin.json — always.
            string manifestPath = Path.Combine(extensionRoot, "plugin.json");
            File.WriteAllText(
                manifestPath,
                JsonConvert.SerializeObject(
                    payload.PluginJson,
                    Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    }));
            filesCreated.Add(manifestPath);

            // UI flavour: write the index.html sent by the client (plain HTML/CSS/JS — no build step).
            if (hasUi)
            {
                string entryHtmlRelative = NormalizeRelativePath(payload.PluginJson.Ui!.EntryHtml);
                string entryHtmlPath = Path.Combine(extensionRoot, entryHtmlRelative);
                string entryHtmlDirectory = Path.GetDirectoryName(entryHtmlPath) ?? extensionRoot;
                Directory.CreateDirectory(entryHtmlDirectory);
                File.WriteAllText(entryHtmlPath, payload.IndexHtml ?? string.Empty);
                filesCreated.Add(entryHtmlPath);
            }

            // C# flavour: generate csproj/Hello.cs/README on the host side, because the csproj needs
            // an absolute path to AnalyseTool.Sdk.dll that only the host knows.
            if (hasCsharp)
            {
                string assemblyName = DeriveAssemblyName(payload.PluginJson.Id);
                string sdkDllPath = Path.Combine(PathProvider.RootDirectory, "AnalyseTool.Sdk.dll");
                string displayTitle = payload.PluginJson.Ui?.Button?.Name is { Length: > 0 } btn ? btn : payload.PluginJson.Id;

                string csprojPath = Path.Combine(extensionRoot, $"{assemblyName}.csproj");
                File.WriteAllText(csprojPath, BuildCsproj(sdkDllPath, version, assemblyName));
                filesCreated.Add(csprojPath);

                string helloCsPath = Path.Combine(extensionRoot, "Hello.cs");
                File.WriteAllText(helloCsPath, BuildHelloCs(assemblyName));
                filesCreated.Add(helloCsPath);

                string readmePath = Path.Combine(extensionRoot, "README.md");
                File.WriteAllText(readmePath, BuildReadme(displayTitle, assemblyName));
                filesCreated.Add(readmePath);
            }

            return Task.FromResult<object?>(new
            {
                created = true,
                directory = extensionRoot,
                files = filesCreated,
            });
        }

        /// <summary>"acme.sample.extension" → "Acme.Sample.Extension". Used as the assembly name and the
        /// root namespace of the generated C# project.</summary>
        private static string DeriveAssemblyName(string id)
        {
            string[] parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(".", parts.Select(seg =>
                char.ToUpperInvariant(seg[0]) + seg.Substring(1).ToLowerInvariant()));
        }

        private static string BuildCsproj(string sdkDllPath, string revitVersion, string assemblyName) => $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0-windows</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                <RootNamespace>{{assemblyName}}</RootNamespace>
                <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
                <OutDir>$(SolutionDir)</OutDir>
              </PropertyGroup>
              <ItemGroup>
                <!-- AnalyseTool SDK — loaded from the installed plugin. Don't ship a copy. -->
                <Reference Include="AnalyseTool.Sdk">
                  <HintPath>{{sdkDllPath}}</HintPath>
                  <Private>false</Private>
                </Reference>
                <!-- Revit API & Newtonsoft are provided by the host at runtime. -->
                <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="{{revitVersion}}.*">
                  <PrivateAssets>all</PrivateAssets>
                  <ExcludeAssets>runtime</ExcludeAssets>
                </PackageReference>
                <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="{{revitVersion}}.*">
                  <PrivateAssets>all</PrivateAssets>
                  <ExcludeAssets>runtime</ExcludeAssets>
                </PackageReference>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4">
                  <PrivateAssets>all</PrivateAssets>
                  <ExcludeAssets>runtime</ExcludeAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        private static string BuildHelloCs(string ns) => $$"""
            using AnalyseTool.Sdk;

            namespace {{ns}};

            [RevitCommand(
                Description = "Returns the active document's title.",
                ReadOnly = true)]
            internal sealed class Hello : IRevitTask
            {
                public async Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
                {
                    var documentName = await revitContext.RunInRevitAsync<string?>(app =>
                    {
                        var name = app.ActiveUIDocument?.Document.Title ?? "(no active document)";
                        return name;
                    });
                    return documentName;
                }
            }
            """;

        private static string BuildReadme(string title, string assemblyName) => $$"""
            # AnalyseTool — AI instructions for writing extensions

            > **How to use this file:** paste it into Claude / ChatGPT as context, then ask it to build an
            > AnalyseTool extension (e.g. "write a command that renumbers selected doors"). It contains the full
            > contract, the manifest schema, the rules, and worked examples — everything the model needs to
            > generate a correct extension in one shot.

            You are helping write **extensions for AnalyseTool**, a Revit 2025/2026 add-in. Extensions add
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
                    Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct);
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
                    public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
                        ctx.RunInRevitAsync<object?>(app =>
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
                public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
                {
                    var args = ctx.Payload.As<Args>()!;                      // read the payload
                    return ctx.RunInRevitAsync<object?>(app =>
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

            ---

            ## 4. C# project setup (NuGet — the easy way)

            ```
            dotnet add package AnalyseTool.Sdk --prerelease
            ```

            Minimal `.csproj`:
            ```xml
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <RootNamespace>Acme.Doors</RootNamespace>
                <AssemblyName>Acme.Doors</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="AnalyseTool.Sdk" Version="1.0.*-*" />
              </ItemGroup>
            </Project>
            ```
            The package brings the `Debug/Release R25` + `R26` build configurations, the `net8.0-windows` target,
            and the Revit API (compile-only). Build with a year config: `dotnet build -c "Release R25"` (or `R26`).

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
            - `<RevitYear>` = `2025` or `2026`.
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

            ## 9. Checklist for a generated extension

            - [ ] `plugin.json` with `id` (+ `entryAssembly` for C#, or none for script/UI).
            - [ ] C#: one or more `IRevitTask` classes; model access only in `RunInRevitAsync`.
            - [ ] `[RevitCommand]` with a clear `Description`; `ReadOnly`/`Destructive` set correctly; `InputType`
                  for commands that take arguments.
            - [ ] UI: `index.html` calling `window.AT.invoke(...)`; `base: "./"` if framework-built.
            - [ ] Tell the user the deploy path and that they click **Reload** (or restart for a new button).
            """;

        /// <summary>Returns one of the registered extension source roots, defaulting to the built-in one
        /// when the caller didn't specify (or specified the default itself). Rejects anything else, so we
        /// can never scaffold into a folder the host wouldn't actually scan.</summary>
        private static string ResolveTargetRoot(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return ExtensionSources.DefaultRoot;

            string full = Path.GetFullPath(requested.Trim());
            bool registered = ExtensionSources.Roots()
                .Any(r => string.Equals(Path.GetFullPath(r), full, StringComparison.OrdinalIgnoreCase));

            if (!registered)
                throw new InvalidOperationException($"Target root is not a registered extension source: {requested}");

            return full;
        }

        private static string SanitizeFolderName(string value)
        {
            string trimmed = value.Trim();

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                trimmed = trimmed.Replace(invalidChar, '-');

            if (string.IsNullOrWhiteSpace(trimmed))
                throw new InvalidOperationException("Folder name is invalid.");

            return trimmed;
        }

        private static string NormalizeRelativePath(string value)
        {
            string normalized = value.Replace('\\', '/').TrimStart('/');

            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException("ui.entryHtml must be a relative path.");

            if (Path.IsPathRooted(normalized))
                throw new InvalidOperationException("ui.entryHtml must be a relative path.");

            if (normalized.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("ui.entryHtml cannot contain '..'.");

            return normalized.Replace('/', Path.DirectorySeparatorChar);
        }
    }

    internal sealed class CreateExtensionTemplatePayload
    {
        public string FolderName { get; set; } = string.Empty;
        /// <summary>Template flavour: "UiOnly", "Csharp" or "Combo".</summary>
        public string Kind { get; set; } = "UiOnly";
        public ExtensionTemplateManifest PluginJson { get; set; } = new();
        /// <summary>HTML content for UI-flavoured templates. Ignored for "Csharp".</summary>
        public string IndexHtml { get; set; } = string.Empty;
        /// <summary>Optional: which registered extension source root to scaffold into. Empty = default root.</summary>
        public string TargetRoot { get; set; } = string.Empty;
    }

    internal sealed class ExtensionTemplateManifest
    {
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string EntryAssembly { get; set; } = string.Empty;
        /// <summary>Omitted for "Csharp"-only templates.</summary>
        public ExtensionTemplateUi? Ui { get; set; }
    }

    internal sealed class ExtensionTemplateUi
    {
        public string EntryHtml { get; set; } = "index.html";
        public string Tab { get; set; } = string.Empty;
        public string Panel { get; set; } = string.Empty;
        public ExtensionTemplateButton Button { get; set; } = new();
    }

    internal sealed class ExtensionTemplateButton
    {
        public string Name { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
    }
}
