using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Policy;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Core.Features.Policy
{
    /// <summary>
    /// The per-user self-update (design §7): the policy names a newer installer (<c>update.downloadUrl</c>
    /// + <c>update.sha256</c>), this seat is a per-user install, so the plugin downloads the MSI,
    /// verifies the pin and hands the rest to the CLI — which waits for Revit to exit and runs
    /// <c>msiexec /qn</c>. Never on a per-machine install (that needs an administrator), never without
    /// a pin, never from a host the policy does not name.
    /// </summary>
    [RevitCommand(
        Description = "Downloads the installer the organization's policy names, verifies it, and schedules " +
                      "the install for when Revit closes (per-user installs only).",
        InputType = typeof(StartSelfUpdate.Request),
        Destructive = true,
        HiddenFromMcp = true)]
    internal sealed class StartSelfUpdate : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (req?.Consent != true)
                throw new InvalidOperationException("The update requires the user's confirmation.");

            PolicyDocument doc = PolicyStore.Current.Document;
            string? url = doc.Update?.DownloadUrl;
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("The policy names no installer (update.downloadUrl).");
            if (PackageHash.Normalize(doc.Update?.Sha256) is null)
                throw new InvalidOperationException("The policy pins no sha256 for the installer; refusing to install an unverified MSI.");
            if (!IsPerUserInstall)
                throw new InvalidOperationException("This is a per-machine installation; updates are deployed by your administrator.");

            string cli = Path.Combine(PathProvider.RootDirectory, "AnalyseTool.Cli.exe");
            if (!File.Exists(cli))
                throw new InvalidOperationException($"The update helper was not found ({cli}).");

            string msi = await PolicySourceReader.DownloadFileAsync(url!, "AnalyseTool-update.msi", ct);
            if (PackageHash.Verify(msi, doc.Update!.Sha256, "installer") is string bad)
                throw new InvalidOperationException(bad);

            ProcessStartInfo psi = new(cli)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(msi)!,
            };
            psi.ArgumentList.Add("update"); psi.ArgumentList.Add("wait-and-install");
            psi.ArgumentList.Add("--pid"); psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--msi"); psi.ArgumentList.Add(msi);
            psi.ArgumentList.Add("--sha256"); psi.ArgumentList.Add(PackageHash.Normalize(doc.Update.Sha256)!);
            Process.Start(psi);

            Log.Information("Self-update scheduled: {Msi} installs after Revit (pid {Pid}) exits", msi, Environment.ProcessId);
            return new { scheduled = true, msi, message = "Close Revit to finish the update." };
        }

        /// <summary>A SingleUser MSI lands under %AppData%; the MultiUser one under %ProgramData%.</summary>
        internal static bool IsPerUserInstall =>
            PathProvider.RootDirectory.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase);

        internal sealed record Request
        {
            [Description("Must be true: the user confirmed installing the organization's newer version when Revit closes.")]
            public bool Consent { get; set; }
        }
    }
}
