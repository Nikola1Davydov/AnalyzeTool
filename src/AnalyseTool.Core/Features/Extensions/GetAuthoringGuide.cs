using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Newtonsoft.Json;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Hands the extension-authoring guide to a caller that cannot open a file.
    ///
    /// The guide has always existed and an agent over MCP could not reach it: it is embedded in this
    /// assembly and copied into new extension folders, neither of which an MCP client can read. So an
    /// agent asked to build a command with a form had to ask the USER how manifests, ribbon buttons and
    /// AT.invoke work — questions the guide answers in full, and questions the user should never have to
    /// answer about their own tool.
    ///
    /// A tool rather than an MCP resource, for now. Resources are the better fit — the client caches
    /// them and they cost no tool call — but they need a verb the bridge's wire protocol does not have,
    /// while a tool works with every client today. If resources arrive, this stays as the fallback.
    /// </summary>
    [RevitCommand(
        Description = "Returns the full AnalyseTool extension-authoring guide as Markdown: the IRevitTask " +
                      "contract, the plugin.json manifest, how a ribbon button chooses between running a " +
                      "command and opening a page, the AT.invoke contract for web UIs, and the commands " +
                      "for saving all of it. READ THIS BEFORE writing or saving an extension — it answers " +
                      "the architecture questions rather than leaving them to be guessed or asked of the " +
                      "user. Read-only and cheap: the text is embedded in the host, not read from disk.",
        ReadOnly = true,
        OutputType = typeof(AuthoringGuideResult))]
    internal sealed class GetAuthoringGuide : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string markdown = AuthoringGuide.Read();
            return Task.FromResult<object?>(new AuthoringGuideResult("LLM.md", markdown.Length, markdown));
        }
    }

    /// <summary><see cref="Length"/> up front so a caller can decide what to do with the text before it
    /// has read all of it.</summary>
    internal sealed record AuthoringGuideResult(
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("length")] int Length,
        [property: JsonProperty("markdown")] string Markdown);
}
