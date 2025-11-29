using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WixSharp;

namespace Installer;

public static class Generator
{
    /// <summary>
    ///     Generates Wix entities, features and directories for the installer.
    /// </summary>
    public static WixEntity[] GenerateWixEntities(IEnumerable<string> args)
    {
        Regex versionRegex = new Regex(@"\d+");
        Dictionary<string, List<WixEntity>> versionStorages = new Dictionary<string, List<WixEntity>>();

        Feature revitFeature = new Feature
        {
            Name = "Revit Add-in",
            Description = "Revit add-in installation files",
            Display = FeatureDisplay.expand
        };

        foreach (string directory in args)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(directory);
            string fileVersionNumber = versionRegex.Match(directoryInfo.Parent.Name).Value;
            string fileVersion = (int.Parse(fileVersionNumber) + 2000).ToString();
            Feature feature = new Feature
            {
                Name = fileVersion,
                Description = $"Install add-in for Revit {fileVersion}",
                ConfigurableDir = $"INSTALL{fileVersion}"
            };

            revitFeature.Add(feature);

            Files files = new Files(feature, $@"{directory}\*.*", FilterEntities);

            if (versionStorages.TryGetValue(fileVersion, out List<WixEntity>? storage))
            {
                storage.Add(files);
            }
            else
            {
                versionStorages[fileVersion] = new List<WixEntity>
                {
                    files,
                };
            }

            LogFeatureFiles(directory, fileVersion);
        }

        return versionStorages
            .Select(storage => new Dir(new Id($"INSTALL{storage.Key}"), storage.Key, storage.Value.ToArray()))
            .Cast<WixEntity>()
            .ToArray();
    }

    /// <summary>
    ///     Filter installer files and exclude from output. 
    /// </summary>
    private static bool FilterEntities(string file)
    {
        return !file.EndsWith(".pdb");
    }

    /// <summary>
    ///    Write a list of installer files.
    /// </summary>
    private static void LogFeatureFiles(string directory, string fileVersion)
    {
        string[] assemblies = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Console.WriteLine($"Installer files for version '{fileVersion}':");

        foreach (string? assembly in assemblies.Where(FilterEntities))
        {
            Console.WriteLine($"'{assembly}'");
        }
    }
    private static System.IO.DirectoryInfo TryGetSolutionDirectoryInfo(string currentPath = null)
    {
        System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(
            currentPath ?? System.IO.Directory.GetCurrentDirectory());
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }
        return directory;
    }
}