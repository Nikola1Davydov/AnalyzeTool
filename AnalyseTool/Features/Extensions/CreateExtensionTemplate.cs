using AnalyseTool.Common;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.IO;

namespace AnalyseTool.Features.Extensions
{
    [RevitCommand(
        Description = "Creates a UI-only extension template with plugin.json, index.html, and main.ts.",
        InputType = typeof(CreateExtensionTemplatePayload))]
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

            if (payload.PluginJson.Ui is null)
                throw new InvalidOperationException("plugin.json ui section is required.");

            if (string.IsNullOrWhiteSpace(payload.PluginJson.Ui.EntryHtml))
                throw new InvalidOperationException("ui.entryHtml is required.");

            string safeFolderName = SanitizeFolderName(payload.FolderName);
            string extensionRoot = Path.Combine(PathProvider.ExtensionsDirectory, safeFolderName);

            if (Directory.Exists(extensionRoot))
                throw new InvalidOperationException($"Extension folder already exists: {safeFolderName}");

            Directory.CreateDirectory(extensionRoot);

            string manifestPath = Path.Combine(extensionRoot, "plugin.json");
            string entryHtmlRelativePath = NormalizeRelativePath(payload.PluginJson.Ui.EntryHtml);
            string entryHtmlPath = Path.Combine(extensionRoot, entryHtmlRelativePath);

            string entryHtmlDirectory = Path.GetDirectoryName(entryHtmlPath) ?? extensionRoot;
            Directory.CreateDirectory(entryHtmlDirectory);

            string mainTsPath = Path.Combine(entryHtmlDirectory, "main.ts");

            File.WriteAllText(
                manifestPath,
                JsonConvert.SerializeObject(
                    payload.PluginJson,
                    Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    }));

            File.WriteAllText(entryHtmlPath, payload.IndexHtml ?? string.Empty);
            File.WriteAllText(mainTsPath, payload.MainTs ?? string.Empty);

            return Task.FromResult<object?>(new
            {
                created = true,
                directory = extensionRoot,
                manifestPath,
                entryHtmlPath,
                mainTsPath
            });
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
        public ExtensionTemplateManifest PluginJson { get; set; } = new();
        public string IndexHtml { get; set; } = string.Empty;
        public string MainTs { get; set; } = string.Empty;
    }

    internal sealed class ExtensionTemplateManifest
    {
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string TargetRevit { get; set; } = string.Empty;
        public string EntryAssembly { get; set; } = string.Empty;
        public ExtensionTemplateUi Ui { get; set; } = new();
    }

    internal sealed class ExtensionTemplateUi
    {
        public string EntryHtml { get; set; } = "index.html";
        public string DevUrl { get; set; } = string.Empty;
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
