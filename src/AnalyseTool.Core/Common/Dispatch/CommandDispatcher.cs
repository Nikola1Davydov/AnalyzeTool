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
        // The registry is READ from every transport thread (a WebView2 window on the UI thread, an MCP
        // connection on a thread-pool thread) while a reload REPLACES its extension half. It is an
        // immutable snapshot swapped in one assignment: a reader sees the whole old set or the whole new
        // one. It used to be one dictionary cleared and refilled in place, and every call in between was
        // answered "command is not registered" while a reload compiled.
        private readonly Dictionary<string, CommandRegistration> _builtIns = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyDictionary<string, CommandRegistration> _extensions = new Dictionary<string, CommandRegistration>();
        private volatile IReadOnlyDictionary<string, CommandRegistration> _commands = new Dictionary<string, CommandRegistration>();
        private readonly object _publishGate = new();
        private readonly IRevitTaskHub _hub;

        public CommandDispatcher(IRevitTaskHub hub) => _hub = hub;

        /// <summary>Point-in-time snapshot — callers enumerate it (MCP tools/list, the Settings command
        /// table) while a reload may be replacing the registry underneath them.</summary>
        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _commands.Values.ToArray();

        public bool IsRegistered(string command) => _commands.ContainsKey(command);

        /// <summary>Resolved registration for a command name, or null. Used by the CommandQueue to
        /// show a pre-execution gate the command's metadata (ReadOnly/Destructive/…).</summary>
        public CommandRegistration? GetRegistration(string command) =>
            _commands.TryGetValue(command, out CommandRegistration? reg) ? reg : null;

        /// <summary>Registers the built-in commands of the given platform assemblies (Core, Tools, App).
        /// All of them share the source "core" — they ship with the plugin, unlike extensions.</summary>
        public void RegisterBuiltIns(params Assembly[] assemblies)
        {
            lock (_publishGate)
            {
                foreach (Assembly assembly in assemblies)
                foreach (Type type in assembly.GetTypes())
                {
                    if (!IsRegistrable(type)) continue;
                    TryAdd(_builtIns, Describe(type, source: "core", prefix: null), reserved: null);
                }
                Publish();
            }
        }

        /// <summary>An empty set of extension registrations for a reload to fill before it replaces the
        /// current one (<see cref="ReplaceExtensions"/>).</summary>
        public ExtensionRegistrations NewExtensionSet() => new();

        /// <summary>Adds an extension assembly's commands to a set that is not live yet.</summary>
        public void RegisterExtension(Assembly extensionAssembly, string extensionId, ExtensionRegistrations into)
        {
            foreach (Type type in extensionAssembly.GetTypes())
            {
                if (!IsRegistrable(type)) continue;
                // Extension commands are namespaced as "<id>.<name>" to avoid collisions with
                // core commands and between extensions.
                TryAdd(into.Items, Describe(type, source: extensionId, prefix: extensionId), reserved: _builtIns);
            }
        }

        /// <summary>Makes <paramref name="set"/> the extension commands, in one step.</summary>
        public void ReplaceExtensions(ExtensionRegistrations set)
        {
            lock (_publishGate)
            {
                _extensions = set.Items;
                Publish();
            }
        }

        private void Publish()
        {
            Dictionary<string, CommandRegistration> all = new(_builtIns, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, CommandRegistration> extension in _extensions)
                all.TryAdd(extension.Key, extension.Value);
            _commands = all;
        }

        /// <summary>First registration wins; a later one with the same name is logged and skipped — the
        /// author notices in the Settings command list / extension diagnostics. No dialog from Core.</summary>
        private static void TryAdd(Dictionary<string, CommandRegistration> target, CommandRegistration registration,
            IReadOnlyDictionary<string, CommandRegistration>? reserved)
        {
            CommandRegistration? existing = null;
            if (reserved?.TryGetValue(registration.Name, out existing) != true
                && !target.TryGetValue(registration.Name, out existing))
            {
                target.Add(registration.Name, registration);
                return;
            }
            Log.Error("Command name conflict: {Name} is already registered from {Existing}; " +
                      "skipping registration from {Source}", registration.Name, existing?.Source ?? "<unknown>", registration.Source);
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

        /// <summary>The registration a command type produces — name, metadata, schemas. Pure, so the
        /// Revit-free tests can check what a command will register as.</summary>
        internal static CommandRegistration Describe(Type type, string source, string? prefix)
        {
            RevitCommandAttribute? attr = type.GetCustomAttribute<RevitCommandAttribute>();
            // No [RevitCommand] at all, or [RevitCommand] without an explicit name -> use the class name.
            string baseName = string.IsNullOrEmpty(attr?.Name) ? type.Name : attr!.Name!;
            string name = string.IsNullOrEmpty(prefix) ? baseName : $"{prefix}.{baseName}";

            return new CommandRegistration(
                name, type, source,
                attr?.Description,
                attr?.ReadOnly ?? false,
                attr?.Destructive ?? false,
                BuildSchema(attr?.InputType),
                ExposeToMcp: !(attr?.HiddenFromMcp ?? false),
                OutputSchemaJson: BuildSchema(attr?.OutputType),
                IsCodeAuthoringTool: type.IsDefined(typeof(Extensions.Scripting.CodeAuthoringToolAttribute), inherit: false));
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
            catch (Exception ex)
            {
                // Published as an empty object, which tells a caller "no arguments" — wrong for a type
                // that declares some. Said in the log, so an author can find out why.
                Log.Warning(ex, "No JSON schema could be built for {Type}; it is published as an empty object", type?.FullName);
            }
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

    /// <summary>Extension commands being collected by a reload, not yet visible to any caller.</summary>
    internal sealed class ExtensionRegistrations
    {
        internal Dictionary<string, CommandRegistration> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
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
        string OutputSchemaJson = SchemaListing.EmptyObject,
        bool IsCodeAuthoringTool = false);
}
