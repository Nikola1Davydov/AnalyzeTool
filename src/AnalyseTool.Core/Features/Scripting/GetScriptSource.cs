using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Scripting
{
    /// <summary>
    /// Reads back the C# of a script extension, so a generated command can be refined instead of only
    /// replaced. Without it an author had to keep the source in context: a session that ended took the
    /// code with it, and "add a filter to that button you made yesterday" meant writing it again from
    /// scratch and hoping the rewrite matched.
    ///
    /// SCRIPT extensions only. A prebuilt DLL has no source here to return, and saying "not a script"
    /// is a better answer than an empty one.
    ///
    /// Gated by the same C#-execution toggle as ExecuteRevitCode and SaveAsCommand — and for the same
    /// reason turned around: this hands the AI code from the user's machine. It belongs to the authoring
    /// loop, and the toggle is precisely the statement "I am authoring code here with AI".
    /// </summary>
    [RevitCommand(
        Description = "Returns the C# source of a script extension so a generated command can be " +
                      "refined rather than rewritten: read it, change it, save it back with " +
                      "SaveAsCommand and overwrite:true. Ids come from GetInstalledExtensions or " +
                      "GetExtensionDiagnostics. Script extensions only — a prebuilt DLL has no source " +
                      "to return. Read-only. Requires C# execution to be enabled in AnalyseTool Settings.",
        ReadOnly = true,
        InputType = typeof(GetScriptSource.Request),
        OutputType = typeof(ScriptSourceResult))]
    internal sealed class GetScriptSource : IRevitTask
    {
        /// <summary>Wire name, referenced by the MCP bridge to gate this tool's visibility.</summary>
        public const string CommandName = nameof(GetScriptSource);

        /// <summary>A generated command is a page or two of C#. A file past this is not one of ours, and
        /// pushing it through a tool response helps nobody.</summary>
        private const long MaxFileBytes = 512 * 1024;

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            // Checked here as well as at the transport: the bridge hides this tool while the toggle is
            // off, but the dispatcher is reachable from the frontend too, and a refusal belongs with the
            // thing being refused.
            if (!CodeExecutionSettings.Enabled)
                return Task.FromResult<object?>(ScriptSourceResult.Failed(
                    "C# code execution is disabled. Enable it in AnalyseTool Settings to read script sources."));

            string? id = ctx.Payload.As<Request>()?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return Task.FromResult<object?>(ScriptSourceResult.Failed("An extension id is required."));

            ExtensionDescriptor? descriptor = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                .FirstOrDefault(d => string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
                return Task.FromResult<object?>(ScriptSourceResult.Failed($"No extension with id '{id}'."));

            if (!descriptor.HasScript)
                return Task.FromResult<object?>(ScriptSourceResult.Failed(
                    $"'{id}' is not a script extension ({(descriptor.DeclaresDll ? "it ships a prebuilt DLL" : "it has no C# at all")}), so it has no source to read."));

            List<ScriptFile> files = new();
            foreach (string path in descriptor.ScriptFiles)
            {
                try
                {
                    FileInfo file = new(path);
                    if (!file.Exists || file.Length > MaxFileBytes) continue;
                    files.Add(new ScriptFile(Path.GetFileName(path), File.ReadAllText(path)));
                }
                catch (IOException)
                {
                    // One unreadable file does not make the others useless.
                }
            }

            return Task.FromResult<object?>(new ScriptSourceResult(
                true, descriptor.Manifest.Id, descriptor.Directory, files, null));
        }

        internal sealed class Request
        {
            [Description("Extension id, as listed by GetInstalledExtensions or GetExtensionDiagnostics.")]
            public string? Id { get; set; }
        }
    }

    /// <summary>One source file of a script extension.</summary>
    internal sealed record ScriptFile(
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("content")] string Content);

    internal sealed record ScriptSourceResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("id")] string? Id,
        [property: JsonProperty("directory")] string? Directory,
        [property: JsonProperty("files")] IReadOnlyList<ScriptFile> Files,
        [property: JsonProperty("error")] string? Error)
    {
        public static ScriptSourceResult Failed(string error) =>
            new(false, null, null, new List<ScriptFile>(), error);
    }
}
