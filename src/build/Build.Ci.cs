using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    // CI guardrails live HERE (not in the workflow YAML) so the exact same pipeline runs locally
    // (`src/build.cmd Ci`) and on GitHub — ci.yml is reduced to "checkout, setup .NET, run Ci".
    //   1. dependency contract + headless Core/Tools invariant (Check-Boundaries.ps1)
    //   2. the full plugin chain compiles for both TFM worlds (R25 = net8, R27 = net10)
    //   3. the Sdk NUPKG works for an external extension author (pack -> build sample against it)

    AbsolutePath CiArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath SdkNupkgDirectory => CiArtifactsDirectory / "sdk-nupkg";

    AbsolutePath LauncherProject => RootDirectory / "src" / "AnalyseTool.Launcher" / "AnalyseTool.Launcher.csproj";
    AbsolutePath SdkProject => RootDirectory / "src" / "AnalyseTool.Sdk" / "AnalyseTool.Sdk.csproj";
    AbsolutePath SampleProject => RootDirectory / "samples" / "Acme.Sample" / "Acme.Sample.csproj";

    /// <summary>Dependency contract + headless invariant — same script devs run locally.</summary>
    Target CheckBoundaries => _ => _
        .Executes(() =>
        {
            AbsolutePath script = RootDirectory / "src" / "build" / "Check-Boundaries.ps1";
            ProcessTasks
                .StartProcess("pwsh", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"", RootDirectory)
                .AssertZeroExitCode();
        });

    /// <summary>The full plugin chain for both TFM worlds: Debug R25 (net8) and Debug R27 (net10).</summary>
    Target CompileCi => _ => _
        .DependsOn(CheckBoundaries)
        .Executes(() =>
        {
            foreach (string configuration in new[] { "Debug R25", "Debug R27" })
            {
                DotNetBuild(settings => settings
                    .SetProjectFile(LauncherProject)
                    .SetConfiguration(configuration)
                    .SetVerbosity(DotNetVerbosity.minimal));
            }
        });

    /// <summary>Packs the Sdk NUPKG into artifacts/sdk-nupkg (uploaded by the workflow).</summary>
    Target PackSdk => _ => _
        .Executes(() =>
        {
            SdkNupkgDirectory.CreateOrCleanDirectory();
            DotNetPack(settings => settings
                .SetProject(SdkProject)
                .SetConfiguration("Release R25")
                .SetOutputDirectory(SdkNupkgDirectory)
                .SetVerbosity(DotNetVerbosity.minimal));
        });

    /// <summary>
    /// External-author simulation: builds samples/Acme.Sample against the PACKED nupkg (not the
    /// ProjectReference world) from an isolated feed + isolated package folder, exactly like a
    /// third-party extension author consuming AnalyseTool.Sdk from NuGet.
    /// </summary>
    Target TestSdkPackage => _ => _
        .DependsOn(PackSdk)
        .Executes(() =>
        {
            AbsolutePath package = SdkNupkgDirectory.GlobFiles("AnalyseTool.Sdk.*.nupkg").First();
            string version = Regex.Match(package.Name, @"^AnalyseTool\.Sdk\.(.+)\.nupkg$").Groups[1].Value;
            Serilog.Log.Information("Packed SDK version: {Version}", version);

            // Isolated feed: ONLY the freshly packed nupkg + nuget.org — no machine-level sources.
            AbsolutePath configFile = CiArtifactsDirectory / "nuget.ci.config";
            File.WriteAllText(configFile,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />" +
                $"<add key=\"local-sdk\" value=\"{SdkNupkgDirectory}\" />" +
                "<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />" +
                "</packageSources></configuration>");

            DotNetBuild(settings => settings
                .SetProjectFile(SampleProject)
                .SetConfiguration("Release R25")
                .SetVerbosity(DotNetVerbosity.minimal)
                .AddProperty("UseSdkPackage", "true")
                .AddProperty("SdkPackageVersion", version)
                .AddProperty("RestoreConfigFile", configFile)
                .AddProperty("RestorePackagesPath", CiArtifactsDirectory / "sample-packages"));
        });

    /// <summary>Everything CI checks, in one target — runnable locally: <c>src\build.cmd Ci</c>.</summary>
    Target Ci => _ => _
        .DependsOn(CompileCi, TestSdkPackage)
        .Executes(() => Serilog.Log.Information("CI guardrails passed"));
}
