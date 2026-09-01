using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Installs an extension straight from its publisher's release — the catalog's "Install" button
    /// and the "paste a repository" field both land here. Same shape as
    /// <see cref="InstallExtensionFromFile"/> (consent required, logged, applied via reload); the
    /// only difference is that the zip is fetched instead of picked.
    /// <para>
    /// AnalyseTool is the courier, not the host: the download always comes from the publisher's own
    /// URL, and nothing here vouches for what is inside (#48).
    /// </para>
    /// </summary>
    [RevitCommand(
        Description = "Downloads an extension package from a publisher's release (github:owner/repo, a " +
                      "repository URL, or an https feed) and installs it; applies via reload.",
        InputType = typeof(InstallExtensionFromSource.Request),
        Destructive = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class InstallExtensionFromSource : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(req?.Source))
                throw new InvalidOperationException("An install source is required.");
            if (!req.Consent)
                throw new InvalidOperationException(
                    "Installation requires the user to accept the third-party extension disclaimer.");

            string source = ExtensionUpdateFeed.Normalize(req.Source);
            string? expectedId = string.IsNullOrWhiteSpace(req.ExpectedId) ? null : req.ExpectedId!.Trim();

            // Nothing is known about the package yet, so the download needs a name of its own; the
            // catalog's id when there is one, otherwise the source, flattened into a file name.
            string cacheName = expectedId ?? SanitizeForFileName(source);

            ExtensionUpdateInfo latest = await ExtensionUpdateFeed.ResolveAsync(source, expectedId ?? string.Empty, ct);
            string zipPath = await ExtensionUpdateFeed.DownloadPackageAsync(latest.DownloadUrl, cacheName, ct);

            ExtensionPackageInfo probe = ExtensionPackage.Validate(zipPath);

            // The catalog says which extension lives at this source. A release that serves a different
            // id means the entry is stale or the repository changed hands — either way the user asked
            // for one thing and would get another, so it stops here rather than installing it.
            if (expectedId is not null &&
                !string.Equals(probe.Manifest.Id, expectedId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"The catalog lists '{expectedId}' at {source}, but its latest release contains " +
                    $"'{probe.Manifest.Id}'. Refusing to install it — the catalog entry is out of date.");

            ExtensionInstallResult result = ExtensionInstaller.InstallPackage(
                zipPath, req.Overwrite, CoreServices.RevitVersion);
            ExtensionPackageInfo info = result.Info;
            string id = info.Manifest.Id;

            if (result.AlreadyInstalled)
                return new
                {
                    installed = false,
                    alreadyInstalled = true,
                    id,
                    version = info.Manifest.Version,
                    source,
                };

            // The consent record, same as the from-file path, plus where it came from — for a
            // downloaded package that origin IS the interesting half of the audit trail.
            Log.Information(
                "Installed extension {Id} {Version} (publisher: {Publisher}) from {Source} " +
                "({DownloadUrl}); user accepted the third-party disclaimer. Binary years: {Years}",
                id, info.Manifest.Version, info.Manifest.Publisher ?? "<unknown>", source, latest.DownloadUrl,
                info.BinaryYears.Count == 0 ? "none (script/UI)" : string.Join(", ", info.BinaryYears));

            CoreServices.ReloadExtensions();

            return new
            {
                installed = true,
                id,
                version = info.Manifest.Version,
                publisher = info.Manifest.Publisher,
                directory = result.Directory,
                binaryYears = info.BinaryYears,
                replaced = result.Replaced,
                source,
                releaseUrl = latest.ReleaseUrl,
            };
        }

        /// <summary>A cache file name for a source whose extension id is not known yet.</summary>
        private static string SanitizeForFileName(string source)
        {
            char[] cleaned = source.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray();
            string name = new string(cleaned).Trim('-');
            return string.IsNullOrEmpty(name) ? "download" : name;
        }

        internal sealed record Request
        {
            [Description("Install source: 'github:owner/repo', a GitHub repository URL, 'owner/repo', " +
                         "or an https URL returning {version, downloadUrl}.")]
            public string Source { get; set; } = string.Empty;

            [Description("Extension id the source is expected to serve (from the catalog); the install " +
                         "is refused if the package carries a different id. Omit for a free-form source.")]
            public string? ExpectedId { get; set; }

            [Description("Must be true: the user has accepted the third-party extension disclaimer.")]
            public bool Consent { get; set; }

            [Description("Replace an already-installed extension with the same id.")]
            public bool Overwrite { get; set; }
        }
    }
}
