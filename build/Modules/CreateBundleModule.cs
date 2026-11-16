using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Autodesk.PackageBuilder;
using Build.Options;
using Microsoft.Extensions.Options;
using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.FileSystem;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;
using Shouldly;
using File = ModularPipelines.FileSystem.File;

namespace Build.Modules;

/// <summary>
///     Create the Autodesk .bundle package for the Autodesk App Store.
/// </summary>
[DependsOn<ResolveVersioningModule>]
[DependsOn<CompileProjectModule>]
public sealed partial class CreateBundleModule(
    IOptions<BuildOptions> buildOptions,
    IOptions<ProductOptions> productOptions,
    IOptions<VendorOptions> vendorOptions) : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var versioningResult = await context.GetModule<ResolveVersioningModule>();
        var versioning = versioningResult.ValueOrDefault!;

        var targetDirectories = AddinPublish.GetFolders();
        var productName = productOptions.Value.Name;

        var outputFolder = context.Git().RootDirectory.GetFolder(buildOptions.Value.OutputDirectory);
        var bundleFolder = outputFolder.CreateFolder($"{productName}.bundle");
        var contentFolder = bundleFolder.CreateFolder("Contents");
        var manifestFile = bundleFolder.GetFile("PackageContents.xml");

        PackFiles(targetDirectories, contentFolder);
        GenerateManifest(productName, targetDirectories, manifestFile, versioning);

        var outputFile = outputFolder.GetFile($"{bundleFolder.Name}.zip");
        context.Files.Zip.ZipFolder(bundleFolder, outputFile.Path);
        await bundleFolder.DeleteAsync(cancellationToken);

        context.Summary.KeyValue("Artifacts", "Bundle", outputFile.Path);
    }

    private static void PackFiles(Folder[] targetDirectories, Folder contentFolder)
    {
        foreach (var targetDirectory in targetDirectories)
        {
            TryParseVersion(targetDirectory.Path, out var version)
                .ShouldBeTrue($"Could not parse version from directory name: {targetDirectory.Path}");

            var versionFolder = contentFolder.CreateFolder(version);
            foreach (var filePath in targetDirectory.GetFiles(file => file.Exists))
            {
                var relativePath = Path.GetRelativePath(targetDirectory.Path, filePath.Path);
                var destinationPath = versionFolder.GetFile(relativePath);
                if (!destinationPath.Folder!.Exists)
                {
                    destinationPath.Folder!.Create();
                }

                filePath.CopyTo(destinationPath.Path);
            }
        }
    }

    /// <summary>
    ///     Generate the Autodesk manifest.
    /// </summary>
    private void GenerateManifest(string productName, Folder[] targetDirectories, File manifestDirectory, ResolveVersioningResult versioning)
    {
        BuilderUtils.Build<PackageContentsBuilder>(builder =>
        {
            builder.ApplicationPackage.Create()
                .ProductType(ProductTypes.Application)
                .AutodeskProduct(AutodeskProducts.Revit)
                .Name(productName)
                .AppVersion(versioning.Version);

            builder.CompanyDetails.Create(vendorOptions.Value.Name)
                .Email(vendorOptions.Value.Email)
                .Url(vendorOptions.Value.Url);

            foreach (var targetDirectory in targetDirectories)
            {
                TryParseVersion(targetDirectory.Path, out var version)
                    .ShouldBeTrue($"Could not parse version from directory name: {targetDirectory.Path}");

                var addinManifests = targetDirectory.GetFiles(file => file.Extension == ".addin");
                foreach (var addinManifest in addinManifests)
                {
                    var relativePath = Path.GetRelativePath(targetDirectory.Path, addinManifest.Path);

                    builder.Components.CreateEntry($"Revit {version}")
                        .RevitPlatform(int.Parse(version))
                        .AppName(productName)
                        .ModuleName($"./Contents/{version}/{relativePath}");
                }
            }
        }, manifestDirectory);
    }

    /// <summary>
    ///     Parse a version string from the given input.
    /// </summary>
    private static bool TryParseVersion(string input, [NotNullWhen(true)] out string? version)
    {
        version = null;
        var match = VersionRegex().Match(input);
        if (!match.Success) return false;

        switch (match.Value.Length)
        {
            case 4:
                version = match.Value;
                return true;
            case 2:
                version = $"20{match.Value}";
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///     A regular expression to match the last sequence of numeric characters in a string.
    /// </summary>
    [GeneratedRegex(@"(\d+)(?!.*\d)")]
    private static partial Regex VersionRegex();
}
