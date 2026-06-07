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
                public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
                    ctx.RunInRevitAsync<object?>(app => new
                    {
                        documentName = app.ActiveUIDocument?.Document.Title ?? "(no active document)"
                    });
            }
            """;

        private static string BuildReadme(string title, string assemblyName) => $$"""
            # {{title}}

            A C# extension for AnalyseTool.

            ## Build

            ```
            dotnet build -c Release
            ```

            ## Deploy

            Copy `bin/Release/net8.0-windows/{{assemblyName}}.dll` next to this `plugin.json`,
            then click **Reload** in AnalyseTool Settings — the new commands appear without a Revit restart.

            ## Add another command

            Create a new class with `[RevitCommand]` implementing `IRevitTask`. See `Hello.cs`.
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
