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

        /// <summary>
        /// Parses a document handed over inline, as the <c>pipeline</c> property of a payload.
        ///
        /// <para>The property is declared as <see cref="object"/> rather than as a Newtonsoft
        /// <c>JObject</c>, because a declared JObject publishes a JSON Schema that refers to itself —
        /// JObject enumerates as JTokens, so the generator reads it as an array of arrays of itself. That
        /// schema tells an agent nothing true about the shape it should send. The cost is that this has to
        /// accept whatever arrived, including the JSON-as-a-string that agents send about as often as they
        /// send an object; both reach the same parser one line later.</para>
        /// </summary>
        public static PipelineDocument ParseInline(object value, string origin) =>
            Parse(ToJsonText(value), origin);

        /// <summary>The JSON exactly as it arrived. Whoever WRITES a file must keep this rather than
        /// re-serializing the parsed document — see <see cref="Indent"/>.</summary>
        public static string ToJsonText(object value) =>
            value as string ?? value.ToString() ?? string.Empty;

        /// <summary>
        /// Re-indents JSON without going through <see cref="PipelineDocument"/>, so a file stays readable
        /// while keeping every key.
        ///
        /// <para>Saving used to write <c>SerializeObject(doc)</c>, which quietly erased everything the
        /// model does not declare. An agent's conditions written one level too high did not merely fail to
        /// take effect — they stopped existing, so the file on disk no longer contained the mistake, and
        /// nothing downstream could name it. A pipeline this plugin writes has to be the author's file,
        /// not our idea of it.</para>
        /// </summary>
        public static string Indent(string json) =>
            Newtonsoft.Json.Linq.JToken.Parse(json).ToString(Formatting.Indented);

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

        /// <summary>Where a name or path resolves to on disk. Public because reading a pipeline back for
        /// the editor must open the SAME file the runner would.</summary>
        public static string ResolvePath(string nameOrPath) => Resolve(nameOrPath);

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
