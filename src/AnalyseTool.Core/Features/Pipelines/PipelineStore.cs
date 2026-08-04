using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Pipelines;
using Newtonsoft.Json;
using System.IO;

namespace AnalyseTool.Core.Features.Pipelines
{
    /// <summary>Reads <c>.atpipe</c> files out of the pipelines folder. Loading only — writing is the
    /// author's business (an editor, an agent, a text editor), and a file dropped in by hand has to work
    /// exactly as well as one this plugin wrote.</summary>
    internal static class PipelineStore
    {
        public const string Extension = ".atpipe";

        /// <summary>Resolves a name or a path to a document. A bare name is looked up in the pipelines
        /// folder, with the extension optional, so a caller can say "naming" and mean
        /// <c>%LOCALAPPDATA%\AnalyseTool\pipelines\naming.atpipe</c>.</summary>
        public static PipelineDocument Load(string nameOrPath)
        {
            string path = Resolve(nameOrPath);
            if (!File.Exists(path))
                throw new FileNotFoundException($"No pipeline '{nameOrPath}'. Looked at: {path}");

            return Parse(File.ReadAllText(path), path);
        }

        public static PipelineDocument Parse(string json, string origin)
        {
            PipelineDocument? doc;
            try
            {
                doc = JsonConvert.DeserializeObject<PipelineDocument>(json);
            }
            catch (JsonException ex)
            {
                // The message names the position, which is the one thing an author (or an AI correcting
                // its own file) actually needs.
                throw new InvalidOperationException($"{origin} is not valid JSON: {ex.Message}", ex);
            }

            if (doc is null) throw new InvalidOperationException($"{origin} is empty.");

            // Refused early and by number: a file from a LATER schema may use fields this build silently
            // ignores, and half-running someone's pipeline is worse than declining it.
            if (doc.Schema > 1)
                throw new InvalidOperationException(
                    $"{origin} uses pipeline schema {doc.Schema}; this build understands 1. Update AnalyseTool.");

            return doc;
        }

        public static IReadOnlyList<string> ListNames()
        {
            if (!Directory.Exists(PathProvider.PipelinesRoot)) return Array.Empty<string>();
            return Directory.GetFiles(PathProvider.PipelinesRoot, "*" + Extension)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Resolve(string nameOrPath)
        {
            if (Path.IsPathRooted(nameOrPath)) return nameOrPath;

            string name = nameOrPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                ? nameOrPath
                : nameOrPath + Extension;

            return Path.Combine(PathProvider.PipelinesRoot, name);
        }
    }
}
