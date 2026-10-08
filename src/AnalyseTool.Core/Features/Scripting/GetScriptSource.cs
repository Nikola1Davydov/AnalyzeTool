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
    /// Reads back the C# of an extension the host builds (its <c>src\</c> folder), so a saved command
    /// can be refined instead of only replaced. Without it an author had to keep the source in context:
    /// a session that ended took the code with it, and "add a filter to that button you made yesterday"
    /// meant writing it again from scratch and hoping the rewrite matched.
    ///
    /// Host-built extensions only. A DLL the author builds with <c>dotnet build</c> keeps its source in
    /// the author's project, and saying so is a better answer than an empty one. The name is kept from
    /// the time these were called scripts: agents and the authoring guide call it by this name.
    ///
    /// Gated by the same C#-execution toggle as ExecuteRevitCode and SaveAsCommand — and for the same
    /// reason turned around: this hands the AI code from the user's machine. It belongs to the authoring
    /// loop, and the toggle is precisely the statement "I am authoring code here with AI".
    /// </summary>
    [RevitCommand(
        Description = "Returns the C# source of a command extension saved with SaveAsCommand (its src " +
                      "folder) so a saved command can be refined rather than rewritten: read it, change " +
                      "it, save it back with SaveAsCommand and overwrite:true. Ids come from " +
                      "GetInstalledExtensions or GetExtensionDiagnostics. Only extensions the host builds " +
                      "— a DLL built from the author's own project has its source there. Read-only. " +
                      "Requires C# execution to be enabled in AnalyseTool Settings.",
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
                    "C# code execution is disabled. Enable it in AnalyseTool Settings to read command sources."));

            string? id = ctx.Payload.As<Request>()?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return Task.FromResult<object?>(ScriptSourceResult.Failed("An extension id is required."));

            ExtensionDescriptor? descriptor = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                .FirstOrDefault(d => string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
                return Task.FromResult<object?>(ScriptSourceResult.Failed($"No extension with id '{id}'."));

            // A folder that has not been loaded since the switch to host builds still keeps its
            // sources in the root; they move into src\ on the next load.
            IReadOnlyList<string> sources = HostBuild.IsHostBuilt(descriptor.Directory)
                ? HostBuild.SourceFiles(descriptor.Directory)
                : HostBuild.IsScriptFolder(descriptor.Directory, descriptor.Manifest)
                    ? Directory.GetFiles(descriptor.Directory, "*.cs")
                    : Array.Empty<string>();
            if (sources.Count == 0)
                return Task.FromResult<object?>(ScriptSourceResult.Failed(
                    $"'{id}' has no source here ({(descriptor.DeclaresDll ? "its DLL is built from the author's own project" : "it has no C# at all")}), so there is nothing to read."));

            List<ScriptFile> files = new();
            foreach (string path in sources)
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

    /// <summary>One source file of a host-built extension (a file in its src folder).</summary>
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
