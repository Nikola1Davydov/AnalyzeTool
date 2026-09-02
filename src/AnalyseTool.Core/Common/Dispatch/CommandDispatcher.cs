using AnalyseTool.Sdk;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;
using System.Reflection;

namespace AnalyseTool.Core.Common.Dispatch
{
    internal sealed class CommandDispatcher
    {
        // Concurrent because the registry is READ from every transport thread (a WebView2 window on the
        // UI thread, an MCP connection on a thread-pool thread) while a reload REWRITES it: extension
        // install/remove/enable and the ribbon's Reload all rebuild the extension half in place. A plain
        // Dictionary resizing under a concurrent TryGetValue surfaces as a phantom "command is not
        // registered" — or worse, a spin inside the bucket walk.
        private readonly ConcurrentDictionary<string, CommandRegistration> _commands = new(StringComparer.OrdinalIgnoreCase);
        private readonly RevitTaskHub _hub;

        public CommandDispatcher(RevitTaskHub hub) => _hub = hub;

        /// <summary>Point-in-time snapshot — callers enumerate it (MCP tools/list, the Settings command
        /// table) while a reload may be replacing the registry underneath them.</summary>
        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _commands.Values.ToArray();

        public bool IsRegistered(string command) => _commands.ContainsKey(command);

        /// <summary>Resolved registration for a command name, or null. Used by the CommandQueue to
        /// show a pre-execution gate the command's metadata (ReadOnly/Destructive/…).</summary>
        public CommandRegistration? GetRegistration(string command) =>
            _commands.TryGetValue(command, out CommandRegistration? reg) ? reg : null;

        /// <summary>Removes all extension-provided commands (keeps built-ins) so they can be reloaded.</summary>
        public void ClearExtensions()
        {
            List<string> toRemove = _commands
                .Where(kv => !string.Equals(kv.Value.Source, "core", StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToList();

            foreach (string key in toRemove)
                _commands.TryRemove(key, out _);
        }

        /// <summary>Registers the built-in commands of the given platform assemblies (Core, Tools, App).
        /// All of them share the source "core" — they ship with the plugin, unlike extensions.</summary>
        public void RegisterBuiltIns(params Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies)
            foreach (Type type in assembly.GetTypes())
            {
                if (!IsRegistrable(type)) continue;
                TryRegister(type, source: "core", prefix: null);
            }
        }

        public void RegisterExtension(Assembly extensionAssembly, string extensionId)
        {
            foreach (Type type in extensionAssembly.GetTypes())
            {
                if (!IsRegistrable(type)) continue;
                // Extension commands are namespaced as "<id>.<name>" to avoid collisions with
                // core commands and between extensions.
                TryRegister(type, source: extensionId, prefix: extensionId);
            }
        }

        public Task<object?> DispatchAsync(string command, JToken payload, CancellationToken ct) =>
            DispatchAsync(command, payload, ct, progress: null);

        /// <summary>
        /// Dispatches a command, optionally wiring a <paramref name="progress"/> sink. A fresh command
        /// instance is created per call, so injecting the sink into an <see cref="IProgressAware"/> command
        /// is race-free. The sink is bound by the caller (the transport) to the originating window.
        /// </summary>
        public async Task<object?> DispatchAsync(
            string command, JToken payload, CancellationToken ct, IProgress<ProgressInfo>? progress)
        {
            if (!_commands.TryGetValue(command, out CommandRegistration? reg))
                throw new InvalidOperationException($"The command '{command}' is not registered.");

            IRevitTask instance = (IRevitTask)Activator.CreateInstance(reg.CommandType)!;
            if (progress is not null && instance is IProgressAware aware) aware.Progress = progress;

            RevitContext context = new RevitContext(_hub, payload);
            return await instance.ExecuteAsync(context, ct);
        }

        private static bool IsRegistrable(Type type)
        {
            if (type.IsAbstract || type.IsInterface) return false;
            return typeof(IRevitTask).IsAssignableFrom(type);
        }

        private void TryRegister(Type type, string source, string? prefix)
        {
            RevitCommandAttribute? attr = type.GetCustomAttribute<RevitCommandAttribute>();
            // No [RevitCommand] at all, or [RevitCommand] without an explicit name -> use the class name.
            string baseName = string.IsNullOrEmpty(attr?.Name) ? type.Name : attr!.Name!;
            string name = string.IsNullOrEmpty(prefix) ? baseName : $"{prefix}.{baseName}";

            CommandRegistration registration = new(
                name, type, source,
                attr?.Description,
                attr?.ReadOnly ?? false,
                attr?.Destructive ?? false,
                BuildSchema(attr?.InputType),
                ExposeToMcp: !(attr?.HiddenFromMcp ?? false),
                OutputSchemaJson: BuildSchema(attr?.OutputType));

            // TryAdd, not check-then-set: first registration wins, and the winner is decided atomically.
            if (!_commands.TryAdd(name, registration))
            {
                // Log-only (no dialog from Core): a conflict means the later registration is skipped,
                // which the author notices in the Settings command list / extension diagnostics.
                string existingSource = _commands.TryGetValue(name, out CommandRegistration? existing)
                    ? existing.Source
                    : "<unknown>";
                Log.Error("Command name conflict: {Name} is already registered from {Existing}; " +
                          "skipping registration from {Source}", name, existingSource, source);
            }
        }

        /// <summary>
        /// Generates a JSON Schema for a declared input or output type (via Microsoft.Extensions.AI,
        /// already referenced). No declared type → empty object.
        /// <para>The schema is stored WHOLE. It used to be capped at 4096 chars here, which suited the
        /// one consumer that existed — an MCP tool listing, where a deeply nested DTO bloats every
        /// response. It does not suit the second: a graph validator comparing one command's output
        /// against the next one's input needs the real thing, and output types (lists of elements with
        /// their parameters) blow past that cap almost always, so capping at registration would leave
        /// every interesting edge uncheckable. The cap now lives with the consumer that wants it —
        /// see <see cref="SchemaListing"/>.</para>
        /// </summary>
        internal static string BuildSchema(Type? type)
        {
            try
            {
                if (type != null) return RelaxNullableRequired(AIJsonUtilities.CreateJsonSchema(type).GetRawText());
            }
            catch { /* fall through to the empty-object schema */ }
            return SchemaListing.EmptyObject;
        }

        /// <summary>
        /// Drops every nullable property from the schema's <c>required</c> lists, recursively.
        ///
        /// The generator marks ALL properties required, including a <c>long?</c> or a <c>string?</c>.
        /// Our results are written by Newtonsoft with <c>NullValueHandling.Ignore</c> on exactly those
        /// properties, so a null is not "null" on the wire — it is absent. An MCP client that validates
        /// <c>structuredContent</c> against the advertised outputSchema (the spec says it must) then
        /// rejects the whole answer: <c>GetElements</c> failed on every input for three weeks with a bare
        /// "Tool execution failed" (#98), while the very same command ran fine from the WebView, which
        /// validates nothing. A property that may be null is, by our own serialization rule, a property
        /// that may be missing — and the schema has to say so.
        /// </summary>
        internal static string RelaxNullableRequired(string schemaJson)
        {
            JToken root = JToken.Parse(schemaJson);
            Relax(root);
            return root.ToString(Newtonsoft.Json.Formatting.None);

            static void Relax(JToken node)
            {
                if (node is JObject obj)
                {
                    if (obj["required"] is JArray required && obj["properties"] is JObject properties)
                    {
                        for (int i = required.Count - 1; i >= 0; i--)
                        {
                            string? name = (string?)required[i];
                            if (name is not null && properties[name] is JObject property && AllowsNull(property))
                                required.RemoveAt(i);
                        }
                        if (required.Count == 0) obj.Remove("required");
                    }
                    foreach (JProperty p in obj.Properties()) Relax(p.Value);
                }
                else if (node is JArray arr)
                {
                    foreach (JToken t in arr) Relax(t);
                }
            }

            static bool AllowsNull(JObject property) =>
                property["type"] is JArray types && types.Any(t => (string?)t == "null");
        }
    }

    /// <summary>Trims a schema down for a LISTING — a response that carries every command at once and is
    /// re-fetched on every reconnect, where a full nested DTO helps nobody. Callers that reason about the
    /// schema (graph validation, connection checks) take the stored one instead.</summary>
    internal static class SchemaListing
    {
        public const string EmptyObject = "{\"type\":\"object\",\"properties\":{}}";
        public const string FreeFormObject = "{\"type\":\"object\",\"additionalProperties\":true}";

        private const int MaxChars = 4096;

        /// <summary>Over the cap, falls back to a permissive object schema; the command's Description
        /// carries the shape instead. This is exactly the behaviour registration used to bake in.</summary>
        public static string Compact(string schemaJson) =>
            schemaJson.Length <= MaxChars ? schemaJson : FreeFormObject;
    }

    internal sealed record CommandRegistration(
        string Name,
        Type CommandType,
        string Source,
        string? Description = null,
        bool ReadOnly = false,
        bool Destructive = false,
        string InputSchemaJson = SchemaListing.EmptyObject,
        bool ExposeToMcp = true,
        string OutputSchemaJson = SchemaListing.EmptyObject);
}
