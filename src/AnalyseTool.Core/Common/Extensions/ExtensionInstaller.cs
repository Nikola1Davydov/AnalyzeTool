using System.IO;

namespace AnalyseTool.Core.Common.Extensions
{
    internal sealed record ExtensionInstallResult(
        ExtensionPackageInfo Info,
        string Directory,
        bool Replaced,
        bool AlreadyInstalled);

    /// <summary>
    /// The one install path shared by "Install from file" and "Update": validate the package, refuse
    /// dev-zone id twins, then extract through a staging folder and swap with an <c>.old</c> backup —
    /// a replace can never lose the working version, and the scanner never sees a partial folder
    /// (it ignores <c>*.installing</c>/<c>*.old</c>). The caller handles consent and reload.
    /// </summary>
    internal static class ExtensionInstaller
    {
        public static ExtensionInstallResult InstallPackage(string zipPath, bool overwrite, string revitVersion)
        {
            ExtensionPackageInfo info = ExtensionPackage.Validate(zipPath);
            string id = info.Manifest.Id;

            // One id = one extension. A dev-zone twin would fight the managed copy for the ribbon
            // button, the dispatcher registration and the enable toggle — refuse with a way out.
            ExtensionDescriptor? devTwin = ExtensionCatalog.EnumerateAll(revitVersion)
                .FirstOrDefault(d => d.Zone == ExtensionZone.Dev &&
                                     string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (devTwin is not null)
                throw new InvalidOperationException(
                    $"'{id}' already exists as a dev extension in '{devTwin.Directory}'. " +
                    "Remove or rename that folder first — one id can only exist once.");

            string target = Path.Combine(ExtensionSources.DefaultManagedRoot, id);
            bool exists = Directory.Exists(target);
            if (exists && !overwrite)
                return new ExtensionInstallResult(info, target, Replaced: false, AlreadyInstalled: true);

            string staging = target + ".installing";
            string backup = target + ".old";
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            try
            {
                ExtensionPackage.ExtractTo(zipPath, info, staging);
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
                // Best-effort: the swap already succeeded, and a lingering *.old folder is ignored
                // by the scanner — a locked file here must not turn a successful install into an
                // error (which would also skip the caller's reload).
                if (Directory.Exists(backup))
                {
                    try { Directory.Delete(backup, recursive: true); }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Could not delete backup {Backup}; ignored by the scanner", backup);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }

            return new ExtensionInstallResult(info, target, Replaced: exists, AlreadyInstalled: false);
        }
    }
}
