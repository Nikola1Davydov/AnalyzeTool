using Autodesk.PackageBuilder;
using Namotion.Reflection;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Linq;

sealed partial class Build
{
    /// <summary>
    ///     Create the Autodesk .bundle package.
    /// </summary>
    Target CreateBundle => _ => _
        .DependsOn(ValidateReleaseVersion, BuildLauncher)
        .Executes(() =>
        {
            Project project = Solution.Host.AnalyseTool_Launcher;
            Log.Information("Project: {Name}", project.Name);

            // ONLY bin\, and only its top level. Searching the whole project directory recursively
            // also matched obj\Release R25 — which shipped reference assemblies (ref\, refint\),
            // generated sources and *.FileListAbsolute.txt (absolute build paths) into the public
            // download. Skip any folder whose Revit version we don't recognize, otherwise
            // GetVersionYear("") throws on a stale bin left over from a dev build.
            string binDirectory = Path.Combine(project.Directory, "bin");
            string[] targetDirectories = Directory.Exists(binDirectory)
                ? Directory
                    .GetDirectories(binDirectory, "Release *", SearchOption.TopDirectoryOnly)
                    .Where(dir => GetRevitVersion(dir).Length > 0)
                    .ToArray()
                : [];
            Assert.NotEmpty(targetDirectories, "No files were found to create a bundle");

            string bundleName = $"{project.Name}.bundle";
            AbsolutePath bundleRoot = ArtifactsDirectory / bundleName;
            AbsolutePath bundlePath = bundleRoot / bundleName;
            AbsolutePath manifestPath = bundlePath / "PackageContents.xml";
            AbsolutePath contentsDirectory = bundlePath / "Contents";
            foreach (string contentDirectory in targetDirectories)
            {
                string version = GetRevitVersion(contentDirectory);

                Log.Information("Bundle files for version {Version}:", version);
                CopyAssemblies(contentDirectory, contentsDirectory / version);
            }

            GenerateManifest(project, targetDirectories, contentsDirectory, manifestPath);
            CompressFolder(bundleRoot);
        });

    /// <summary>
    ///     Generate the Autodesk manifest for the bundle.
    /// </summary>
    void GenerateManifest(Project project, string[] directories, AbsolutePath contentsDirectory, AbsolutePath manifestDirectory)
    {
        BuilderUtils.Build<PackageContentsBuilder>(builder =>
        {
            string company = SharedData.ToolData.PLUGIN_AUTHOR;
            IEnumerable<string> versions = directories.Select(path => GetRevitVersion(path)).Distinct();

            Log.Information("Bundle files for version {Version}:", versions);

            builder.ApplicationPackage.Create().ProductType(ProductTypes.Application)
                                               .AutodeskProduct(AutodeskProducts.Revit)
                                               .Name(Solution.Name)
                                               .AppVersion(ReleaseVersionNumber);

            builder.CompanyDetails.Create(company);

            foreach (string version in versions)
            {
                builder.Components.CreateEntry($"Revit {version}")
                    .RevitPlatform(GetVersionYear(version))
                    .AppName(project.Name)
                    .ModuleName($"./Contents/{version}/{FindAddInFileName(contentsDirectory / version)}");
            }
        }, manifestDirectory);
    }

    /// <summary>
    ///     Name of the .addin that was actually copied for a Revit version. Derived from the file on
    ///     disk, never from the project name: the Launcher writes its manifest as
    ///     <c>$(PluginSubDir).addin</c> ("AnalyseTool.addin"), so composing the name from
    ///     <c>project.Name</c> produced "AnalyseTool.Launcher.addin" — a ModuleName pointing at a file
    ///     that is not in the bundle, leaving every .bundle install with a dead ApplicationPlugins entry.
    /// </summary>
    static string FindAddInFileName(AbsolutePath versionDirectory)
    {
        string[] addInFiles = Directory.GetFiles(versionDirectory, "*.addin", SearchOption.TopDirectoryOnly);
        Assert.HasSingleItem(addInFiles, $"Expected exactly one .addin in {versionDirectory}");
        return Path.GetFileName(addInFiles[0]);
    }

    /// <summary>
    ///     Compress the bundle into a Zip archive.
    /// </summary>
    static void CompressFolder(AbsolutePath bundleRoot)
    {
        string zipPath = $"{bundleRoot}.zip";
        bundleRoot.CompressTo(zipPath);
        bundleRoot.DeleteDirectory();

        Log.Information("Compressing into a Zip: {Name}", zipPath);
    }

    /// <summary>
    ///     Copy assemblies from the source to the target directory.
    /// </summary>
    static void CopyAssemblies(string sourcePath, string targetPath)
    {
        foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
        }

        foreach (string filePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            Log.Information("{Assembly}", filePath);
            File.Copy(filePath, filePath.Replace(sourcePath, targetPath), true);
        }
    }

    static int GetVersionYear(string revitVersion) => int.Parse(revitVersion.TrimStart('R')) + 2000;
}