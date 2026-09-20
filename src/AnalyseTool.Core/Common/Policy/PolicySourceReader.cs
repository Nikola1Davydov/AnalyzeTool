using Serilog;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>What a read returned: the text, whether it is the cached copy, and where it came from.</summary>
    internal sealed record PolicySourceContent(string Text, bool FromCache, string Location);

    /// <summary>
    /// Reads one policy-declared text resource — the company catalog today, the pointer target and
    /// feeds later (design §9) — from an <c>https://</c> URL or a file-system path with <c>%ENV%</c>
    /// expanded. Plain <c>http://</c> is refused everywhere.
    /// <para>
    /// URL reads are cached under the user profile with the server's ETag, so a seat that starts
    /// offline still has the last copy, and one that starts online costs a single conditional
    /// request. The startup rule (design §2) is enforced by the caller: nothing here runs on the
    /// Revit startup path — <see cref="ReadCached"/> is the only synchronous entry, and it touches
    /// the local cache only.
    /// </para>
    /// </summary>
    internal static class PolicySourceReader
    {
        private static readonly HttpClient Http = new(new HttpClientHandler
        {
            // Corporate proxies are the norm: use the system proxy WITH the user's Windows credentials,
            // otherwise every https source fails behind an authenticating (NTLM/Kerberos) proxy.
            UseProxy = true,
            DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        private static string CacheDir => Path.Combine(PathProvider.ProfilePath, "cache", "policy");

        public static bool IsUrl(string source) =>
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        /// <summary>Null when the source form is acceptable, otherwise why not.</summary>
        public static string? Validate(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "A source is required.";
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return $"'{source}' is plain http. A policy source must be https or a file-system path.";
            if (PolicySourceResolver.IsReference(source) && PolicySourceResolver.Parse(source)!.Value.Name.Length == 0)
                return $"'{source}' names no source.";
            return null;
        }

        /// <summary>Expands <c>%ENV%</c> in a path source.</summary>
        public static string ResolvePath(string source) =>
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(source.Trim()));

        /// <summary>The cached copy of a URL source, or the file itself for a path source. Local
        /// disk only — safe on the startup path.</summary>
        public static PolicySourceContent? ReadCached(string source)
        {
            if (Validate(source) is not null) return null;
            string? resolved = PolicySourceResolver.Resolve(source, out _);
            if (resolved is null) return null;
            source = resolved;

            if (!IsUrl(source))
            {
                string path = ResolvePath(source);
                return File.Exists(path) ? new PolicySourceContent(File.ReadAllText(path), FromCache: false, path) : null;
            }

            string body = CacheBodyPath(source);
            return File.Exists(body) ? new PolicySourceContent(File.ReadAllText(body), FromCache: true, source) : null;
        }

        /// <summary>Fetches the source (conditional GET with the cached ETag for URLs, a plain read
        /// for paths). On any failure returns the cached copy when there is one — and reports the
        /// failure through <paramref name="problem"/> so the status page can show it.</summary>
        public static async Task<(PolicySourceContent? Content, string? Problem)> ReadAsync(string source, CancellationToken ct)
        {
            string? invalid = Validate(source);
            if (invalid is not null) return (null, invalid);
            string? resolved = PolicySourceResolver.Resolve(source, out string? unresolved);
            if (resolved is null) return (null, unresolved);
            source = resolved;

            if (!IsUrl(source))
            {
                try
                {
                    string path = ResolvePath(source);
                    if (!File.Exists(path)) return (null, $"'{path}' was not found.");
                    // Files On-Demand: a placeholder read blocks until OneDrive fetched it; the caller
                    // runs us off the UI thread with a token, so a slow sync only delays this task.
                    string text = await File.ReadAllTextAsync(path, ct);
                    return (new PolicySourceContent(text, FromCache: false, path), null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (null, $"Could not read '{source}': {ex.Message}");
                }
            }

            string bodyPath = CacheBodyPath(source);
            string etagPath = bodyPath + ".etag";
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, source);
                request.Headers.UserAgent.Add(
                    new ProductInfoHeaderValue(SharedData.ToolData.PLUGIN_NAME, SharedData.ToolData.PLUGIN_VERSION));
                if (File.Exists(bodyPath) && File.Exists(etagPath))
                {
                    string etag = File.ReadAllText(etagPath).Trim();
                    if (etag.Length > 0 && EntityTagHeaderValue.TryParse(etag, out EntityTagHeaderValue? parsed))
                        request.Headers.IfNoneMatch.Add(parsed);
                }

                using HttpResponseMessage response = await Http.SendAsync(request, ct);
                // Where the bytes actually came from — after redirects. A policy that moved to another
                // host is a decision for the user, not something to follow quietly.
                string location = response.RequestMessage?.RequestUri?.ToString() ?? source;
                if (response.StatusCode == System.Net.HttpStatusCode.NotModified && File.Exists(bodyPath))
                    return (new PolicySourceContent(File.ReadAllText(bodyPath), FromCache: true, location), null);

                response.EnsureSuccessStatusCode();
                string text = await response.Content.ReadAsStringAsync(ct);

                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(bodyPath, text);
                File.WriteAllText(etagPath, response.Headers.ETag?.ToString() ?? string.Empty);
                return (new PolicySourceContent(text, FromCache: false, location), null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Could not fetch policy source {Source}; using the cached copy if any", source);
                PolicySourceContent? cached = File.Exists(bodyPath)
                    ? new PolicySourceContent(File.ReadAllText(bodyPath), FromCache: true, source)
                    : null;
                return (cached, $"Could not fetch '{source}': {ex.Message}" + (cached is null ? "" : " Using the last copy."));
            }
        }

        private static string CacheBodyPath(string url)
        {
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant())))[..24];
            return Path.Combine(CacheDir, hash + ".json");
        }
    }
}
