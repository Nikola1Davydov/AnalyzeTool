using System.IO;
using System.Security.Cryptography;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>SHA-256 pinning of downloaded packages (design §Security notes): the cheap half of
    /// package signing. A pin in the policy turns "trust the hosting" into "trust the reviewed policy".</summary>
    internal static class PackageHash
    {
        public static string Sha256Of(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        /// <summary>Null when the file matches the expected hash (or no hash is expected); otherwise the
        /// refusal text. Hex, case-insensitive, whitespace and a <c>sha256:</c> prefix tolerated.</summary>
        public static string? Verify(string filePath, string? expectedSha256, string what)
        {
            string? expected = Normalize(expectedSha256);
            if (expected is null) return null;

            string actual = Sha256Of(filePath);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{what}: the downloaded package does not match the sha256 the policy pins " +
                  $"(expected {expected[..12]}…, got {actual[..12]}…). Refusing to install it.";
        }

        internal static string? Normalize(string? sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256)) return null;
            string s = sha256.Trim();
            if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) s = s["sha256:".Length..];
            return s.Length == 64 ? s : null; // anything else is not a SHA-256 and cannot pin anything
        }
    }
}
