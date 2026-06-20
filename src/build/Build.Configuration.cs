using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.Git;
using System;
using System.Collections.Generic;
using System.Linq;

sealed partial class Build
{
    /// <summary>
    ///     Patterns of solution configurations for compiling.
    /// </summary>
    readonly string[] Configurations =
    [
        "Release R*"
    ];

    /// <summary>
    ///     Projects packed in the Autodesk Bundle.
    /// </summary>
    Project[] Bundles =>
    [
        Solution.clientapp,
        Solution.AnalyseTool_Launcher,
    ];

    /// <summary>
    ///     Mapping between used installer project and the project containing the installation files.
    /// </summary>
    Dictionary<Project, Project> InstallersMap => new()
    {
        { Solution.Installer, Solution.AnalyseTool }
    };

    /// <summary>
    ///     Path to build output.
    /// </summary>
    readonly AbsolutePath ArtifactsDirectory = RootDirectory / "output";

    /// <summary>
    ///     Releases changelog path.
    /// </summary>
    readonly AbsolutePath ChangelogPath = RootDirectory / "Changelog.md";

    /// <summary>
    ///     Add-in release version, includes version number and release stage.
    /// </summary>
    /// <remarks>Supported version format: <c>version-environment.n.date</c>.</remarks>
    /// <example>
    ///     1.0.0-alpha.1.250101 <br/>
    ///     1.0.0-beta.2.250101 <br/>
    ///     1.0.0
    /// </example>
    [Parameter] string ReleaseVersion;

    /// <summary>
    ///     The previous release version.
    /// </summary>
    /// <remarks>
    ///     Can be used to compare versions or analyze changes between versions.
    /// </remarks>
    string PreviousReleaseVersion => GitTasks.Git("tag -l --sort=v:refname", logInvocation: false, logOutput: false).ToArray() switch
    {
        var tags when tags.Length >= 2 => tags[^2].Text,
        var tags when tags.Length == 0 => throw new InvalidOperationException("The pipeline must be triggered by pushing a new tag"),
        _ => GitTasks.Git("rev-list --max-parents=0 HEAD", logOutput: false, logInvocation: false).First().Text
    };

    /// <summary>
    ///     Numeric release version without a stage.
    /// </summary>
    string ReleaseVersionNumber => ReleaseVersion?.Split('-')[0];

    /// <summary>
    ///     Release stage.
    /// </summary>
    /// <example>
    ///     alpha for 1.0.0-alpha.1.250101<br/>
    ///     beta for 1.0.0-beta.2.250101 => beta <br/>
    ///     production for 1.0.0
    /// </example>
    string ReleaseStage => IsPrerelease ? ReleaseVersion.Split('-')[1].Split('.')[0] : "production";

    /// <summary>
    ///     Determines whether the Revit add-ins release is preview.
    /// </summary>
    bool IsPrerelease => ReleaseVersion != ReleaseVersionNumber;

    /// <summary>
    ///     Git repository metadata.
    /// </summary>
    [GitRepository] readonly GitRepository GitRepository;

    /// <summary>
    ///     Solution structure metadata.
    /// </summary>
    [Solution(GenerateProjects = true)] readonly Solution Solution;

    /// <summary>
    ///     Set not-defined properties.
    /// </summary>
    protected override void OnBuildInitialized()
    {
        ReleaseVersion ??= GitRepository.Tags.SingleOrDefault();
    }
}