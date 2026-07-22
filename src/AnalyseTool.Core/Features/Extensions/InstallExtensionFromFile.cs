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

            ExtensionPackageInfo info = ExtensionPackage.Validate(req.Path);
            string id = info.Manifest.Id;

            string target = Path.Combine(ExtensionSources.DefaultManagedRoot, id);
            bool exists = Directory.Exists(target);
            if (exists && !req.Overwrite)
                throw new InvalidOperationException(
                    $"Extension '{id}' is already installed. Pass overwrite=true to replace it.");

            // Stage next to the target so the final step is a same-volume move; the scanner never
            // sees a partial folder. On replace, the old install is parked as '.old' and restored
            // if the swap fails — an update can never lose the working version.
            string staging = target + ".installing";
            string backup = target + ".old";
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            try
            {
                ExtensionPackage.ExtractTo(req.Path, info, staging);
                if (exists) Directory.Move(target, backup);
                try
                {
                    Directory.Move(staging, target);
                }
                catch
                {
                    if (exists && !Directory.Exists(target)) Directory.Move(backup, target);
                    throw;
                }
                if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
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
                directory = target,
                binaryYears = info.BinaryYears,
                replaced = exists,
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
