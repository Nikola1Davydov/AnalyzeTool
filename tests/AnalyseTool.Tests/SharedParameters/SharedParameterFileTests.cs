using System.IO;
using System.Text;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Services.SharedParameters;

namespace AnalyseTool.Tests.SharedParameters;

/// <summary>
///     The shared parameter file is written by hand, not through Revit's <c>DefinitionFile</c>, so the format rules are ours
///     to keep: columns by header name, nothing the editor cannot show is lost on save, the encoding Revit wrote stays, and
///     an edit made elsewhere is not overwritten.
/// </summary>
public sealed class SharedParameterFileTests
{
    private const string RevitFile =
        "# This is a Revit shared parameter file.\r\n" +
        "# Do not edit manually.\r\n" +
        "*META\tVERSION\tMINVERSION\r\n" +
        "META\t2\t1\r\n" +
        "*GROUP\tID\tNAME\r\n" +
        "GROUP\t1\tCommon\r\n" +
        "GROUP\t2\tDoors\r\n" +
        "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n" +
        "PARAM\t6b1b5b1e-0000-4000-8000-000000000001\tFire rating\tTEXT\t\t2\t1\tEI rating\t1\t0\r\n" +
        "PARAM\t6b1b5b1e-0000-4000-8000-000000000002\tRoom height\tLENGTH\t\t1\t1\t\t1\t1\r\n";

    private string _directory = null!;

    [Before(Test)]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "at-spf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [After(Test)]
    public void DeleteDirectory()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public async Task Parse_RevitFile_ReadsGroupsAndParametersByHeaderName()
    {
        var file = SharedParameterFile.Parse(RevitFile);

        await Assert.That(file.Groups.Count).IsEqualTo(2);
        await Assert.That(file.Parameters.Count).IsEqualTo(2);
        var fire = file.Parameters[0];
        await Assert.That(fire.Name).IsEqualTo("Fire rating");
        await Assert.That(fire.DataType).IsEqualTo("TEXT");
        await Assert.That(fire.GroupId).IsEqualTo(2);
        await Assert.That(fire.Description).IsEqualTo("EI rating");
        await Assert.That(file.Parameters[1].HideWhenNoValue).IsTrue();
    }

    [Test]
    public async Task Parse_ColumnsInAnotherOrder_ReadsThemByName()
    {
        var text = RevitFile
            .Replace("*PARAM\tGUID\tNAME\tDATATYPE", "*PARAM\tNAME\tGUID\tDATATYPE")
            .Replace("PARAM\t6b1b5b1e-0000-4000-8000-000000000001\tFire rating\t", "PARAM\tFire rating\t6b1b5b1e-0000-4000-8000-000000000001\t")
            .Replace("PARAM\t6b1b5b1e-0000-4000-8000-000000000002\tRoom height\t", "PARAM\tRoom height\t6b1b5b1e-0000-4000-8000-000000000002\t");

        var file = SharedParameterFile.Parse(text);

        await Assert.That(file.Parameters[0].Name).IsEqualTo("Fire rating");
        await Assert.That(file.Parameters[0].Guid).IsEqualTo(Guid.Parse("6b1b5b1e-0000-4000-8000-000000000001"));
        await Assert.That(file.Serialize()).IsEqualTo(text);
    }

    [Test]
    public async Task Serialize_RevitFile_ReproducesItExactly()
    {
        await Assert.That(SharedParameterFile.Parse(RevitFile).Serialize()).IsEqualTo(RevitFile);
    }

    [Test]
    public async Task Serialize_UnknownColumnsAndSections_SurviveARoundTrip()
    {
        var text = RevitFile
                       .Replace("HIDEWHENNOVALUE\r\n", "HIDEWHENNOVALUE\tFUTURE\r\n")
                       .Replace("\tEI rating\t1\t0\r\n", "\tEI rating\t1\t0\tkeep-me\r\n")
                   + "*EXTRA\tA\r\nEXTRA\tx\r\n";

        var file = SharedParameterFile.Parse(text);
        await Assert.That(file.Parameters[0].ExtraColumns["FUTURE"]).IsEqualTo("keep-me");

        var written = file.Serialize();
        await Assert.That(written).Contains("\tFUTURE\r\n");
        await Assert.That(written).Contains("\tkeep-me\r\n");
        await Assert.That(written).Contains("*EXTRA\tA\r\nEXTRA\tx\r\n");
    }

    [Test]
    public async Task Parse_RowBeforeItsHeader_Throws()
    {
        await Assert.That(() => SharedParameterFile.Parse("PARAM\tx\r\n")).Throws<FormatException>();
    }

    [Test]
    public async Task Save_Utf16FileWithExtraColumns_KeepsEncodingColumnsAndWritesABackup()
    {
        var path = Path.Combine(_directory, "shared.txt");
        var text = RevitFile
            .Replace("HIDEWHENNOVALUE\r\n", "HIDEWHENNOVALUE\tFUTURE\r\n")
            .Replace("\tEI rating\t1\t0\r\n", "\tEI rating\t1\t0\tkeep-me\r\n");
        File.WriteAllText(path, text, new UnicodeEncoding(false, true));

        var read = SharedParameterFileService.Read(path, path);
        var edited = read.Parameters.Select(parameter => parameter.Name == "Fire rating" ? parameter with { Name = "Fire resistance" } : parameter).ToList();
        SharedParameterFileService.Save(path, read.Stamp, read.Groups, edited, path);

        var bytes = File.ReadAllBytes(path);
        await Assert.That(bytes[0] == 0xFF && bytes[1] == 0xFE).IsTrue();
        var saved = File.ReadAllText(path);
        await Assert.That(saved).Contains("\tFire resistance\t");
        await Assert.That(saved).Contains("\tkeep-me\r\n");
        await Assert.That(File.Exists(path + ".bak")).IsTrue();
    }

    [Test]
    public async Task Save_FileChangedSinceRead_RefusesWithFileChanged()
    {
        var path = Path.Combine(_directory, "shared.txt");
        File.WriteAllText(path, RevitFile, new UnicodeEncoding(false, true));
        var read = SharedParameterFileService.Read(path, path);

        File.AppendAllText(path, "PARAM\t6b1b5b1e-0000-4000-8000-000000000003\tAdded elsewhere\tTEXT\t\t1\t1\t\t1\t0\r\n", new UnicodeEncoding(false, false));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var exception = await Assert.That(() => SharedParameterFileService.Save(path, read.Stamp, read.Groups, read.Parameters, path))
            .Throws<CommandException>();

        await Assert.That(exception!.Code).IsEqualTo(SharedParameterErrorCodes.FileChanged);
        await Assert.That(File.ReadAllText(path)).Contains("Added elsewhere");
    }

    [Test]
    public async Task Save_DuplicateNamesOrMissingGroups_RefusesWithInvalidFile()
    {
        var path = Path.Combine(_directory, "shared.txt");
        File.WriteAllText(path, RevitFile, new UnicodeEncoding(false, true));
        var read = SharedParameterFileService.Read(path, path);

        var clash = read.Parameters.Select(parameter => parameter with { Name = "Same" }).ToList();
        var clashException = await Assert.That(() => SharedParameterFileService.Save(path, read.Stamp, read.Groups, clash, path))
            .Throws<CommandException>();

        var orphan = read.Parameters.Select(parameter => parameter with { GroupId = 99 }).ToList();
        var orphanException = await Assert.That(() => SharedParameterFileService.Save(path, read.Stamp, read.Groups, orphan, path))
            .Throws<CommandException>();

        await Assert.That(clashException!.Code).IsEqualTo(SharedParameterErrorCodes.InvalidFile);
        await Assert.That(orphanException!.Code).IsEqualTo(SharedParameterErrorCodes.InvalidFile);
    }

    [Test]
    public async Task Save_NewFile_IsCreatedWithRevitHeaderAndUtf16()
    {
        var path = Path.Combine(_directory, "new.txt");
        SharedParameterFileService.Save(path, null,
            [new SharedParameterGroupDto { Id = 1, Name = "Common" }],
            [new SharedParameterDto { Guid = Guid.NewGuid().ToString("D"), Name = "Note", DataType = "TEXT", GroupId = 1 }],
            null);

        var text = File.ReadAllText(path);
        await Assert.That(text).StartsWith("# This is a Revit shared parameter file.");
        await Assert.That(SharedParameterFile.Parse(text).Parameters.Single().Name).IsEqualTo("Note");
        var bytes = File.ReadAllBytes(path);
        await Assert.That(bytes[0] == 0xFF && bytes[1] == 0xFE).IsTrue();
    }

    [Test]
    public async Task Read_BrokenFile_ReportsTheErrorWithAStamp()
    {
        var path = Path.Combine(_directory, "broken.txt");
        File.WriteAllText(path, "PARAM\tno header\r\n");

        var read = SharedParameterFileService.Read(path, null);

        await Assert.That(read.Exists).IsTrue();
        await Assert.That(read.Error).IsNotNull();
        await Assert.That(read.Stamp).IsNotEmpty();
    }

    [Test]
    public async Task Read_NoPath_IsNoFile()
    {
        var read = SharedParameterFileService.Read(null, null);

        await Assert.That(read.Path).IsNull();
        await Assert.That(read.Exists).IsFalse();
    }

    [Test]
    public async Task Serialize_TabsAndLineBreaksInADescription_DoNotSplitTheRow()
    {
        var file = SharedParameterFile.Parse(RevitFile);
        file.Parameters[0].Description = "line one\nline\ttwo";

        var back = SharedParameterFile.Parse(file.Serialize());

        await Assert.That(back.Parameters.Count).IsEqualTo(2);
        await Assert.That(back.Parameters[0].Description).IsEqualTo("line one line two");
    }
}
