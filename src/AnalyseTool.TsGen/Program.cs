using System.Text;
using AnalyseTool.TsGen;

// Usage: AnalyseTool.TsGen <path to commands.ts>
// Writes only when the content changed, so Vite's watcher does not reload the page on every build.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: AnalyseTool.TsGen <output .ts path>");
    return 2;
}

string path = Path.GetFullPath(args[0]);
string content = TsContractGenerator.Generate();
string? existing = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
if (existing == content)
{
    Console.WriteLine($"TsGen: {path} is up to date.");
    return 0;
}

Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"TsGen: wrote {path}.");
return 0;
