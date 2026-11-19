using System.IO;
using System.Security.Cryptography;
using System.Text;
using AnalyseTool.Extensions.Catalog;

namespace AnalyseTool.Extensions.Building;

/// <summary>The build of a source extension for the running Revit year.</summary>
/// <param name="AssemblyPath">The DLL to load; <see langword="null" /> when nothing compiled yet.</param>
/// <param name="Compiled">Whether this call compiled; <see langword="false" /> when the cached build was current.</param>
/// <param name="Diagnostics">The errors and warnings of this compilation; empty when the cache was used.</param>
/// <param name="Version">The version the build is of: a pinned version id, or <see langword="null" /> for the current files.</param>
public sealed record ExtensionBuild(string? AssemblyPath, bool Compiled, IReadOnlyList<CompileDiagnostic> Diagnostics, string? Version)
{
    /// <summary>Gets a value indicating whether the latest compilation failed.</summary>
    public bool Failed => Diagnostics.Any(diagnostic => diagnostic.Severity == "error");
}

/// <summary>
///     Compiles the sources of an extension into <c>bin\&lt;year&gt;\</c> — once per change: the build is named after a hash of
///     the sources, the libraries and the Revit year, so a start of Revit with unchanged sources compiles nothing.
/// </summary>
/// <remarks>
///     <c>bin\</c> is ignored by git: builds are never versions. A failed compilation writes nothing, so the last good build
///     stays and keeps loading while the author fixes the code.
/// </remarks>
public static class ExtensionBuilder
{
    /// <summary>The folder of the builds, inside the extension folder.</summary>
    public const string OutputFolder = "bin";

    /// <summary>The folder of further DLLs the sources reference.</summary>
    public const string LibraryFolder = "lib";

    /// <summary>Builds the current files of the extension.</summary>
    public static ExtensionBuild Build(ExtensionDescriptor extension, string revitVersion, bool force = false)
    {
        var sources = ExtensionCatalog.GetSourceFiles(extension.Directory)
            .Select(file => (Path: Path.GetRelativePath(extension.Directory, file), Text: File.ReadAllText(file)))
            .ToList();
        return Build(extension, revitVersion, sources, null, force);
    }

    /// <summary>Builds the sources of a version of the extension (a pinned version) — given as relative path and text.</summary>
    public static ExtensionBuild Build(ExtensionDescriptor extension, string revitVersion, IReadOnlyList<(string Path, string Text)> sources, string? version, bool force = false)
    {
        var libraries = GetLibraries(extension.Directory);
        var hash = Hash(sources, libraries, revitVersion);
        var outputDirectory = Path.Combine(extension.Directory, OutputFolder, revitVersion);
        var name = AssemblyName(extension.Id);
        var output = Path.Combine(outputDirectory, $"{name}.{hash}.dll");

        if (!force && File.Exists(output))
        {
            return new ExtensionBuild(output, false, [], version);
        }

        var result = ExtensionCompiler.Compile(sources, name, libraries);
        if (!result.Success)
        {
            return new ExtensionBuild(FindLatest(outputDirectory, name), true, result.Diagnostics, version);
        }

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(output, result.Assembly!);
        File.WriteAllBytes(Path.ChangeExtension(output, ".pdb"), result.Pdb!);
        Prune(outputDirectory, name, keep: output);
        return new ExtensionBuild(output, true, result.Diagnostics, version);
    }

    /// <summary>The assembly name of an extension: its id, safe as a file name.</summary>
    public static string AssemblyName(string extensionId)
    {
        var builder = new StringBuilder(extensionId.Length);
        foreach (var character in extensionId)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '_');
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> GetLibraries(string directory)
    {
        var folder = Path.Combine(directory, LibraryFolder);
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.dll").Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
    }

    private static string Hash(IReadOnlyList<(string Path, string Text)> sources, IReadOnlyList<string> libraries, string revitVersion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(revitVersion + "\n" + typeof(ExtensionCompiler).Assembly.GetName().Version));
        foreach (var (path, text) in sources.OrderBy(source => source.Path, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path.Replace('\\', '/') + "\n" + text.Replace("\r\n", "\n") + "\n\0"));
        }

        foreach (var library in libraries)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(library) + File.GetLastWriteTimeUtc(library).Ticks));
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..12].ToLowerInvariant();
    }

    /// <summary>The newest build of any version, for when the current sources do not compile.</summary>
    private static string? FindLatest(string outputDirectory, string name)
    {
        return Directory.Exists(outputDirectory)
            ? Directory.GetFiles(outputDirectory, $"{name}.*.dll").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    /// <summary>Keeps the last few builds: a pinned version and the current files may alternate. Locked files stay.</summary>
    private static void Prune(string outputDirectory, string name, string keep)
    {
        foreach (var old in Directory.GetFiles(outputDirectory, $"{name}.*.dll").Where(file => file != keep).OrderByDescending(File.GetLastWriteTimeUtc).Skip(4))
        {
            try
            {
                File.Delete(old);
                File.Delete(Path.ChangeExtension(old, ".pdb"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
