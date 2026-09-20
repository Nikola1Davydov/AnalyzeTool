using Newtonsoft.Json;

namespace AnalyseTool.Sdk
{
    /// <summary>
    /// Read-only access to the organization policy the host applies (SDK 1.3, optional). A company
    /// distributes one <c>policy.json</c> to its seats; an extension may read its own section of it —
    /// <c>{ "acme.standards": { "server": "…" } }</c> — instead of shipping a second configuration
    /// channel. Sections are named by the top-level key. Null when no policy is present or the
    /// section is absent; never throws. The host registers the reader; extensions only read.
    /// </summary>
    public static class HostPolicy
    {
        private static Func<string, string?>? _reader;

        /// <summary>Host only (internal; Core has InternalsVisibleTo): installs the function that
        /// answers <see cref="GetSectionJson"/>. An extension cannot replace it.</summary>
        internal static void RegisterReader(Func<string, string?> reader) => _reader = reader;

        /// <summary>The section's JSON text, or null when absent.</summary>
        public static string? GetSectionJson(string section)
        {
            try { return _reader?.Invoke(section); }
            catch { return null; }
        }

        /// <summary>The section deserialized into <typeparamref name="T"/>, or default when absent or unreadable.</summary>
        public static T? GetSection<T>(string section)
        {
            string? json = GetSectionJson(section);
            if (string.IsNullOrWhiteSpace(json)) return default;
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch { return default; }
        }
    }

    /// <summary>
    /// Emits a telemetry event to the sink the organization policy configured (SDK 1.3, optional).
    /// Off unless the host's policy turns telemetry on and lists the event kind; then the host
    /// forwards it to the company's own sink — never to the plugin's author. Never throws, never
    /// blocks. Extensions use it to report their own usage the same way the host reports commands;
    /// pass no model data, file names or element names.
    /// </summary>
    public static class HostTelemetry
    {
        private static Action<string, IReadOnlyDictionary<string, object?>>? _sink;

        /// <summary>Host only (internal; Core has InternalsVisibleTo).</summary>
        internal static void RegisterSink(Action<string, IReadOnlyDictionary<string, object?>> sink) => _sink = sink;

        public static void Emit(string eventKind, IReadOnlyDictionary<string, object?> properties)
        {
            try { _sink?.Invoke(eventKind, properties); }
            catch { /* telemetry must never break a command */ }
        }
    }
}
