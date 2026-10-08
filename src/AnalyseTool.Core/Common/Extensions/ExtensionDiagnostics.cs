using System.Collections.Concurrent;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// Per-extension load diagnostics (compile errors of host-built sources, DLL load failures),
    /// populated by <see cref="ExtensionLoader"/> on each load and read by the Settings listing so a
    /// broken extension shows its error instead of silently vanishing. Keyed by extension id.
    /// </summary>
    internal static class ExtensionDiagnostics
    {
        private static readonly ConcurrentDictionary<string, string> _errors =
            new(StringComparer.OrdinalIgnoreCase);

        public static void SetError(string extensionId, string error) => _errors[extensionId] = error;

        public static void Clear(string extensionId) => _errors.TryRemove(extensionId, out _);

        public static void ClearAll() => _errors.Clear();

        public static string? GetError(string extensionId) =>
            _errors.TryGetValue(extensionId, out string? error) ? error : null;
    }
}
