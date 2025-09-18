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
        .DependsOn(Compile)
        .Executes(() =>
        {
            Project project = Solution.AnalyseTool;
            Log.Information("Project: {Name}", project.Name);

            string[] targetDirectories = Directory.GetDirectories(project.Directory, "Release *", SearchOption.AllDirectories);
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

            GenerateManifest(project, targetDirectories, manifestPath);
            CompressFolder(bundleRoot);
        });

    /// <summary>
    ///     Generate the Autodesk manifest for the bundle.
    /// </summary>
    void GenerateManifest(Project project, string[] directories, AbsolutePath manifestDirectory)
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
                    .ModuleName($"./Contents/{version}/{project.Name}.addin");
            }
        }, manifestDirectory);
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