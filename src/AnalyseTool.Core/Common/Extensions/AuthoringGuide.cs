using System.IO;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// The extension-authoring guide (the repository's own <c>src/LLM.md</c>), embedded into this
    /// assembly. One reader for the two places it is served from: the copy dropped into every new
    /// extension folder, and the command that hands it to an agent over MCP.
    /// </summary>
    internal static class AuthoringGuide
    {
        /// <summary>Pinned in AnalyseTool.Core.csproj via LogicalName rather than left to the default
        /// "&lt;RootNamespace&gt;.&lt;path&gt;" derivation, which a folder rename would silently change.</summary>
        private const string ResourceName = "AnalyseTool.Core.Templates.LLM.md";

        public static string Read()
        {
            using Stream? stream = typeof(AuthoringGuide).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                throw new InvalidOperationException(
                    $"Template resource '{ResourceName}' is missing from AnalyseTool.Core — check the " +
                    "EmbeddedResource entries in AnalyseTool.Core.csproj.");

            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
