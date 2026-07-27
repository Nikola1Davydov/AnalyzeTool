using Nuke.Common;
using Serilog;
using System;

sealed partial class Build
{
    /// <summary>
    ///     Refuse to package a release whose tag disagrees with the version compiled into the plugin.
    /// </summary>
    /// <remarks>
    ///     <see cref="SharedData.ToolData.PLUGIN_VERSION"/> is the single version source at runtime: the
    ///     MSI takes its version and file name from it (Installer/Program.cs), Settings displays it, and
    ///     CheckUpdate compares against it. The tag only names the GitHub release and the bundle's
    ///     AppVersion. Nothing used to connect the two, so tagging 1.4.5 while the constant still said
    ///     1.4.4.0 shipped "AnalyseTool-1.4.4.0-SingleUser.msi" inside a release called 1.4.5 — silently.
    ///     Rather than feed the tag into the installer (which would only move the drift to the DLL that
    ///     reports its own version), keep one source and fail loudly when the tag doesn't match it.
    /// </remarks>
    Target ValidateReleaseVersion => _ => _
        .Requires(() => ReleaseVersion)
        .Executes(() =>
        {
            string tagged = ReleaseVersionNumber;
            string compiled = SharedData.ToolData.PLUGIN_VERSION;

            Assert.True(Version.TryParse(tagged, out Version taggedVersion),
                $"Release version '{ReleaseVersion}' is not a version number (expected e.g. 1.4.5 or 1.4.5-beta.1.250101).");
            Assert.True(Version.TryParse(compiled, out Version compiledVersion),
                $"PLUGIN_VERSION '{compiled}' in src/SharedData/ToolData.cs is not a version number.");

            // Compare major.minor.build only: the tag is three-part ("1.4.5"), the constant four-part
            // ("1.4.5.0"), and Version treats an omitted component as -1 rather than 0.
            Assert.True(Normalize(taggedVersion) == Normalize(compiledVersion),
                $"Release tag '{tagged}' does not match PLUGIN_VERSION '{compiled}' in src/SharedData/ToolData.cs. " +
                "Bump the constant (and the CHANGELOG section) to the version you are tagging.");

            Log.Information("Release version {Version} matches PLUGIN_VERSION {Compiled}", tagged, compiled);
        });

    static (int Major, int Minor, int Build) Normalize(Version version) =>
        (version.Major, version.Minor, Math.Max(version.Build, 0));
}
