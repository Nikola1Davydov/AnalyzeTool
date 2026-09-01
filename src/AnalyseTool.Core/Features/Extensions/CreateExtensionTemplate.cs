using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core;
using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Scaffolds an extension on disk in one of three flavours:
    ///   • <c>UiOnly</c>  — plugin.json + index.html (plain HTML/CSS/JS, no build).
    ///   • <c>Csharp</c>  — plugin.json + csproj + Hello.cs (built with <c>dotnet build</c>).
    ///   • <c>Combo</c>   — both.
    /// Every flavour additionally gets <c>LLM.md</c>, the paste-into-AI authoring guide — it covers
    /// C#, script AND JS/UI authoring, so UI-only templates need it just as much.
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
            string version = CoreServices.RevitVersion; // drives the generated csproj: packages, TFM, output folder
            string root = ResolveTargetRoot(payload.TargetRoot);
            // The extension folder sits directly under the root and is laid out exactly like a published
            // package: plugin.json / scripts / ui at the top (version-independent), compiled binaries in
            // <year>\ (see the template's OutDir). Publishing is then zipping this folder, and what the
            // author tests is what ships.
            string extensionRoot = Path.Combine(root, safeFolderName);

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

                string csprojPath = Path.Combine(extensionRoot, $"{assemblyName}.csproj");
                File.WriteAllText(csprojPath, BuildCsproj(sdkDllPath, version, assemblyName));
                filesCreated.Add(csprojPath);

                string helloCsPath = Path.Combine(extensionRoot, "Hello.cs");
                File.WriteAllText(helloCsPath, BuildHelloCs(assemblyName));
                filesCreated.Add(helloCsPath);

                // bin\ and obj\ are the only things in this folder that are not part of the extension.
                // C# flavours only: a script or UI folder has no build output to ignore.
                string gitignorePath = Path.Combine(extensionRoot, ".gitignore");
                File.WriteAllText(gitignorePath, ReadTemplate(GitignoreResource));
                filesCreated.Add(gitignorePath);

                // A GitHub workflow that builds every Revit year and publishes the zip on a version
                // tag. C# flavours only, because PackExtension is an MSBuild target and a script or
                // UI-only folder has no project for it to run in. Without this an author has
                // PackExtension locally and nothing that calls it where releases are made — the gap
                // that turned up the first time this extension pipeline was walked end to end.
                string workflowDir = Path.Combine(extensionRoot, ".github", "workflows");
                Directory.CreateDirectory(workflowDir);
                string workflowPath = Path.Combine(workflowDir, "ci.yml");
                File.WriteAllText(workflowPath, ReadTemplate(WorkflowResource));
                filesCreated.Add(workflowPath);
            }

            // LLM.md — for EVERY flavour, not just C#: the guide covers C#, script and JS/UI authoring,
            // and a UI-only author needs the AT.invoke contract just as much as a C# author needs
            // IRevitTask.
            string llmInstructionsPath = Path.Combine(extensionRoot, "LLM.md");
            File.WriteAllText(llmInstructionsPath, BuildLLMInstructions());
            filesCreated.Add(llmInstructionsPath);

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

        // Resource names are pinned via LogicalName in AnalyseTool.Core.csproj rather than left to the
        // default "<RootNamespace>.<path>" derivation, which a folder rename would silently change.
        private const string CsprojResource = "AnalyseTool.Core.Templates.Extension.csproj.xml";
        private const string GitignoreResource = "AnalyseTool.Core.Templates.gitignore.txt";
        private const string WorkflowResource = "AnalyseTool.Core.Templates.workflow.yml.txt";

        /// <summary>
        /// Template texts are EMBEDDED RESOURCES, not C# string literals. The csproj is then real XML
        /// that an editor validates and a human can build — as a literal it carried a hard-coded
        /// net8.0-windows into Revit 2027 projects unnoticed. The author guide is literally
        /// <c>src/LLM.md</c> instead of a 300-line copy kept in step by hand. Resources travel inside
        /// AnalyseTool.Core.dll, so packaging and installation have no extra file to lose.
        /// </summary>
        private static string ReadTemplate(string resourceName)
        {
            using Stream? stream = typeof(CreateExtensionTemplate).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                throw new InvalidOperationException(
                    $"Template resource '{resourceName}' is missing from AnalyseTool.Core — check the EmbeddedResource entries in AnalyseTool.Core.csproj.");

            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        /// <summary>Substitutes <c>__Token__</c> placeholders, then refuses to return a half-filled
        /// template. While the text was an interpolated literal the compiler caught a renamed
        /// placeholder; in a file it is just a string, so without this the generator would hand the
        /// user a project with "__AssemblyName__" inside it. The double-underscore form is used in both
        /// templates: one convention, and in the csproj it cannot be mistaken for — or collide with —
        /// MSBuild's own <c>$(SolutionDir)</c>.</summary>
        private static string Fill(string template, params (string Token, string Value)[] values)
        {
            foreach ((string token, string value) in values)
                template = template.Replace($"__{token}__", value);

            string[] unresolved = Regex.Matches(template, @"__[A-Za-z][A-Za-z0-9]*__")
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (unresolved.Length > 0)
                throw new InvalidOperationException(
                    $"Template placeholders were not substituted: {string.Join(", ", unresolved)}");

            return template;
        }

        private static string BuildCsproj(string sdkDllPath, string revitVersion, string assemblyName) =>
            // The template derives its TFM from RevitVersion in MSBuild rather than taking it
            // pre-computed, so an author who retargets the project cannot end up with a year and a
            // runtime that disagree — the failure that reaches them as an opaque NU1202.
            Fill(ReadTemplate(CsprojResource),
                ("AssemblyName", assemblyName),
                ("SdkDllPath", sdkDllPath),
                ("RevitVersion", revitVersion));

        /// <summary>The starter command — inline, unlike the other templates, and deliberately so.
        /// This was the file whose NAME broke resource embedding: AssignCulture read the ".cs" of
        /// Hello.cs.txt as Czech and routed it into a satellite assembly (see the WithCulture note in
        /// AnalyseTool.Core.csproj). Nineteen lines gain little from a .txt file anyway, and as a
        /// literal the namespace hole is checked by the compiler again.
        /// The blank line before the closing delimiter is the generated file's trailing newline.</summary>
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

        /// <summary>The author guide, served verbatim from the embedded <c>src/LLM.md</c>. It takes no
        /// arguments on purpose: the document is the same for every extension — the two parameters the
        /// previous hand-written copy accepted were never used by it.</summary>
        private static string BuildLLMInstructions() => AuthoringGuide.Read();

        /// <summary>Returns one of the registered extension source roots, defaulting to the dev root
        /// when the caller didn't specify (templates are user-authored work-in-progress). Rejects
        /// anything else, so we can never scaffold into a folder the host wouldn't actually scan.</summary>
        private static string ResolveTargetRoot(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return ExtensionSources.DefaultDevRoot;

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
        [Description("Folder name to scaffold into, created under the target root.")]
        public string FolderName { get; set; } = string.Empty;
        /// <summary>Template flavour: "UiOnly", "Csharp" or "Combo".</summary>
        [Description("Template flavour: \"UiOnly\", \"Csharp\" or \"Combo\".")]
        public string Kind { get; set; } = "UiOnly";
        [Description("Contents of the extension's plugin.json manifest.")]
        public ExtensionTemplateManifest PluginJson { get; set; } = new();
        /// <summary>HTML content for UI-flavoured templates. Ignored for "Csharp".</summary>
        [Description("HTML content for UI-flavoured templates. Ignored for \"Csharp\".")]
        public string IndexHtml { get; set; } = string.Empty;
        /// <summary>Optional: which registered extension source root to scaffold into. Empty = default root.</summary>
        [Description("Optional: which registered extension source root to scaffold " +
                                           "into. Empty = the default root.")]
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
