using AnalyseTool.TsGen;

namespace AnalyseTool.Tests;

/// <summary>
/// clientapp/src/generated/commands.ts is the page's view of the command contract. A Debug build of the
/// Launcher rewrites it on a developer machine; CI does not regenerate, so this is where a C# change that
/// was never built into the file — a renamed property, a new nullable — turns red instead of reaching
/// the page as an undefined at run time.
/// </summary>
public class TsContractTests
{
    private static string CommittedPath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "clientapp", "src", "generated", "commands.ts");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("clientapp/src/generated/commands.ts not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public async Task Committed_file_matches_the_commands()
    {
        string committed = (await File.ReadAllTextAsync(CommittedPath())).ReplaceLineEndings("\n");
        string generated = TsContractGenerator.Generate();

        await Assert.That(committed == generated).IsTrue()
            .Because("commands.ts is out of date — rebuild the Launcher in Debug (or run " +
                     "`dotnet build src/AnalyseTool.TsGen/AnalyseTool.TsGen.csproj -c \"Debug R25\"`) and commit the file.");
    }

    [Test]
    public async Task Output_names_and_optionality_follow_the_wire()
    {
        string ts = TsContractGenerator.Generate();

        // [JsonProperty("path")] on a string? that is written even when null.
        await Assert.That(ts).Contains("  path: string | null;");
        // NullValueHandling.Ignore: left out when null, so optional and never null.
        await Assert.That(ts).Contains("  error?: string;");
        // A nested input record is named after its command and written in camelCase.
        await Assert.That(ts).Contains("export interface GetParameterReportRequest {");
        await Assert.That(ts).Contains("  topValues?: number;");
        // An input whose properties are all optional can be called without one.
        await Assert.That(ts).Contains("GetSharedParameterFile: (input: GetSharedParameterFileRequest = {}, options?: InvokeOptions) =>");
    }
}
