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
    //   2. command schema contract: every command describes itself, declares the input it reads and
    //      the output it returns (Check-Schemas.ps1)
    //   3. the full plugin chain compiles for every Revit year (R25/R26 = net8, R27 = net10)
    //   4. the Sdk NUPKG works for an external extension author (pack -> build sample against it)
    //   5. the extension template the plugin generates actually builds, and Core's embedded
    //      template resources are really in the assembly (Build.Ci.Template.cs)

    AbsolutePath CiArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath SdkNupkgDirectory => CiArtifactsDirectory / "sdk-nupkg";

    AbsolutePath LauncherProject => RootDirectory / "src" / "AnalyseTool.Launcher" / "AnalyseTool.Launcher.csproj";
    AbsolutePath SdkProject => RootDirectory / "src" / "AnalyseTool.Sdk" / "AnalyseTool.Sdk.csproj";
    AbsolutePath SampleProject => RootDirectory / "samples" / "Acme.Sample" / "Acme.Sample.csproj";

    /// <summary>The Revit-free test projects — the ones a CI runner without Revit can execute. The
    /// in-Revit project (AnalyseTool.RevitTests) is deliberately not here; it needs a licensed Revit.</summary>
    AbsolutePath[] TestProjects =>
    [
        RootDirectory / "src" / "AnalyseTool.Tests" / "AnalyseTool.Tests.csproj",
    ];

    /// <summary>Dependency contract + headless invariant — same script devs run locally.</summary>
    Target CheckBoundaries => _ => _
        .Executes(() =>
        {
            AbsolutePath script = RootDirectory / "src" / "build" / "Check-Boundaries.ps1";
            ProcessTasks
                .StartProcess("pwsh", $"-NoProfile -ExecutionPolicy Bypass -File {script}", RootDirectory)
                .AssertZeroExitCode();
        });

    /// <summary>
    /// Command schema contract — what a caller can know about a command without reading its source.
    /// A source scan like CheckBoundaries, so it runs before any build: a command that describes
    /// itself badly is cheaper to catch here than after three Revit years have compiled.
    /// </summary>
    Target CheckSchemas => _ => _
        .Executes(() =>
        {
            AbsolutePath script = RootDirectory / "src" / "build" / "Check-Schemas.ps1";
            ProcessTasks
                .StartProcess("pwsh", $"-NoProfile -ExecutionPolicy Bypass -File {script}", RootDirectory)
                .AssertZeroExitCode();
        });

    /// <summary>
    /// The full plugin chain for every supported Revit year. R25/R26 are net8 and R27 is net10, but
    /// the years are not interchangeable within a TFM: each pins its own Revit API package set, so
    /// R26 has to compile here too — covering only one year per TFM let R26-only breakage through.
    /// </summary>
    /// <summary>Runs the Revit-free tests. Before this target existed, "the two Test targets" checked
    /// the SDK package and the extension template — not one line of platform code (#128).</summary>
    Target RunTests => _ => _
        .DependsOn(CheckBoundaries)
        .Executes(() =>
        {
            // MTP mode of `dotnet test` (global.json: "test.runner"): the project goes in as --project,
            // and the old VSTest-style positional path is refused by the .NET 10 SDK. Plain invocation
            // rather than DotNetTest(), whose settings model is the VSTest one. No quotes by hand: the
            // argument handler quotes values with spaces itself (a repo path with a space taught us).
            foreach (AbsolutePath project in TestProjects)
                DotNet($"test --project {project} -c {"Debug R25"}", workingDirectory: RootDirectory);
        });

    Target CompileCi => _ => _
        .DependsOn(CheckBoundaries, CheckSchemas)
        .Executes(() =>
        {
            foreach (string configuration in new[] { "Debug R25", "Debug R26", "Debug R27" })
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

            // The publishing pipeline the nupkg ships (build/AnalyseTool.Sdk.targets): pack the
            // sample for every Revit year into the distribution zip, exactly like a vendor's CI
            // would. A broken PackExtension target or bundle layout fails THIS step. The per-year
            // builds run as child `dotnet build` processes, so the package-mode properties must be
            // forwarded explicitly via AnalyseToolPackExtraArgs (child processes don't inherit -p).
            AbsolutePath packOutput = CiArtifactsDirectory / "sample-pack";
            string packExtraArgs =
                $"-p:UseSdkPackage=true -p:SdkPackageVersion={version} " +
                $"-p:RestoreConfigFile={configFile} " +
                $"-p:RestorePackagesPath={CiArtifactsDirectory / "sample-packages"}";
            DotNetMSBuild(settings => settings
                .SetTargetPath(SampleProject)
                .SetTargets("PackExtension")
                .AddProperty("UseSdkPackage", "true")
                .AddProperty("SdkPackageVersion", version)
                .AddProperty("RestoreConfigFile", configFile)
                .AddProperty("RestorePackagesPath", CiArtifactsDirectory / "sample-packages")
                .AddProperty("AnalyseToolPackOutput", packOutput)
                .AddProperty("AnalyseToolPackExtraArgs", packExtraArgs));

            AbsolutePath zip = packOutput.GlobFiles("acme.sample-*.zip").FirstOrDefault()
                ?? throw new System.Exception($"PackExtension produced no zip in {packOutput}");
            Serilog.Log.Information("PackExtension produced {Zip}", zip.Name);
        });

    /// <summary>Everything CI checks, in one target — runnable locally: <c>src\build.cmd Ci</c>.</summary>
    Target Ci => _ => _
        .DependsOn(CompileCi, RunTests, TestSdkPackage, TestExtensionTemplate, CheckCoreResources)
        .Executes(() => Serilog.Log.Information("CI guardrails passed"));
}
