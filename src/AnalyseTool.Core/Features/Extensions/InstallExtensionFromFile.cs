using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Installs an extension package (zip) into the managed zone (<c>extensions-dist\&lt;id&gt;</c>).
    /// Validation is shared with the vendor-side pack (see <see cref="ExtensionPackage"/>). The
    /// caller (Settings UI) must show the third-party disclaimer FIRST and pass <c>consent=true</c>;
    /// the consent is logged (#48 — third-party code runs at the user's own risk, the publisher is
    /// responsible, AnalyseTool does not review it). Extraction goes through a staging folder so a
    /// half-written install never becomes visible to the scanner.
    /// </summary>
    [RevitCommand(
        Description = "Installs an extension from a zip package into the managed extensions folder; applies via reload.",
        InputType = typeof(InstallExtensionFromFile.Request),
        Destructive = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class InstallExtensionFromFile : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(req?.Path))
                throw new InvalidOperationException("Package path is required.");
            if (!req.Consent)
                throw new InvalidOperationException(
                    "Installation requires the user to accept the third-party extension disclaimer.");

            ExtensionInstallResult result = ExtensionInstaller.InstallPackage(
                req.Path, req.Overwrite, CoreServices.RevitVersion);
            ExtensionPackageInfo info = result.Info;
            string id = info.Manifest.Id;

            if (result.AlreadyInstalled)
            {
                // Structured (not an exception): the UI branches on this to offer the replace flow,
                // and prose wording can then change without silently breaking that path.
                return Task.FromResult<object?>(new
                {
                    installed = false,
                    alreadyInstalled = true,
                    id,
                    version = info.Manifest.Version,
                });
            }

            // The consent record: who published it, what was installed, from which file. Serilog
            // writes to the persistent log folder, which doubles as the audit trail.
            Log.Information(
                "Installed extension {Id} {Version} (publisher: {Publisher}) from {Package}; " +
                "user accepted the third-party disclaimer. Binary years: {Years}",
                id, info.Manifest.Version, info.Manifest.Publisher ?? "<unknown>", req.Path,
                info.BinaryYears.Count == 0 ? "none (script/UI)" : string.Join(", ", info.BinaryYears));

            CoreServices.ReloadExtensions();

            return Task.FromResult<object?>(new
            {
                installed = true,
                id,
                version = info.Manifest.Version,
                publisher = info.Manifest.Publisher,
                directory = result.Directory,
                binaryYears = info.BinaryYears,
                replaced = result.Replaced,
            });
        }

        internal sealed record Request
        {
            [Description("Absolute path to the extension package (.zip).")]
            public string Path { get; set; } = string.Empty;

            [Description("Must be true: the user has accepted the third-party extension disclaimer.")]
            public bool Consent { get; set; }

            [Description("Replace an already-installed extension with the same id (update flow).")]
            public bool Overwrite { get; set; }
        }
    }
}
