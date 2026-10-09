using System.Text;
using AnalyseTool.Tools.SharedParameters;

namespace AnalyseTool.Tests;

/// <summary>
/// The shared parameter file is written by hand, not through Revit's DefinitionFile, so the format rules
/// are ours to keep: columns by header name, nothing the editor cannot show is lost on save, the encoding
/// Revit wrote stays, and an edit made elsewhere is not overwritten.
/// </summary>
public class SharedParameterFileTests
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

    private string _dir = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-spf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public async Task Parses_groups_and_parameters_by_header_name()
    {
        SharedParameterFile file = SharedParameterFile.Parse(RevitFile);

        await Assert.That(file.Groups.Count).IsEqualTo(2);
        await Assert.That(file.Parameters.Count).IsEqualTo(2);
        SharedParameterDefinition fire = file.Parameters[0];
        await Assert.That(fire.Name).IsEqualTo("Fire rating");
        await Assert.That(fire.DataType).IsEqualTo("TEXT");
        await Assert.That(fire.GroupId).IsEqualTo(2);
        await Assert.That(fire.Description).IsEqualTo("EI rating");
        await Assert.That(file.Parameters[1].HideWhenNoValue).IsTrue();
    }

    [Test]
    public async Task Serialize_reproduces_a_revit_file_exactly()
    {
        await Assert.That(SharedParameterFile.Parse(RevitFile).Serialize()).IsEqualTo(RevitFile);
    }

    [Test]
    public async Task Unknown_columns_and_sections_survive_a_round_trip()
    {
        string text = RevitFile
            .Replace("HIDEWHENNOVALUE\r\n", "HIDEWHENNOVALUE\tFUTURE\r\n")
            .Replace("\tEI rating\t1\t0\r\n", "\tEI rating\t1\t0\tkeep-me\r\n")
            + "*EXTRA\tA\r\nEXTRA\tx\r\n";

        SharedParameterFile file = SharedParameterFile.Parse(text);
        await Assert.That(file.Parameters[0].ExtraColumns["FUTURE"]).IsEqualTo("keep-me");

        string written = file.Serialize();
        await Assert.That(written).Contains("\tFUTURE\r\n");
        await Assert.That(written).Contains("\tkeep-me\r\n");
        await Assert.That(written).Contains("*EXTRA\tA\r\nEXTRA\tx\r\n");
    }

    [Test]
    public async Task Save_keeps_utf16_and_extra_columns_and_writes_a_backup()
    {
        string path = Path.Combine(_dir, "shared.txt");
        string text = RevitFile
            .Replace("HIDEWHENNOVALUE\r\n", "HIDEWHENNOVALUE\tFUTURE\r\n")
            .Replace("\tEI rating\t1\t0\r\n", "\tEI rating\t1\t0\tkeep-me\r\n");
        File.WriteAllText(path, text, new UnicodeEncoding(false, true));

        SharedParameterFileData read = SharedParameterFileService.Read(path, path);
        List<SharedParameterDto> edited = read.Parameters
            .Select(p => p.Name == "Fire rating" ? p with { Name = "Fire resistance" } : p).ToList();
        SharedParameterFileService.Save(path, read.Stamp, read.Groups, edited, path);

        byte[] bytes = File.ReadAllBytes(path);
        await Assert.That(bytes[0] == 0xFF && bytes[1] == 0xFE).IsTrue();
        string saved = File.ReadAllText(path);
        await Assert.That(saved).Contains("\tFire resistance\t");
        await Assert.That(saved).Contains("\tkeep-me\r\n");
        await Assert.That(File.Exists(path + ".bak")).IsTrue();
    }

    [Test]
    public async Task Save_refuses_when_the_file_changed_since_it_was_read()
    {
        string path = Path.Combine(_dir, "shared.txt");
        File.WriteAllText(path, RevitFile, new UnicodeEncoding(false, true));
        SharedParameterFileData read = SharedParameterFileService.Read(path, path);

        File.AppendAllText(path, "PARAM\t6b1b5b1e-0000-4000-8000-000000000003\tAdded elsewhere\tTEXT\t\t1\t1\t\t1\t0\r\n",
            new UnicodeEncoding(false, false));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        await Assert.That(() => SharedParameterFileService.Save(path, read.Stamp, read.Groups, read.Parameters, path))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Save_refuses_duplicate_names_and_missing_groups()
    {
        string path = Path.Combine(_dir, "shared.txt");
        File.WriteAllText(path, RevitFile, new UnicodeEncoding(false, true));
        SharedParameterFileData read = SharedParameterFileService.Read(path, path);

        List<SharedParameterDto> clash = read.Parameters
            .Select(p => p with { Name = "Same" }).ToList();
        await Assert.That(() => SharedParameterFileService.Save(path, read.Stamp, read.Groups, clash, path))
            .Throws<InvalidOperationException>();

        List<SharedParameterDto> orphan = read.Parameters
            .Select(p => p with { GroupId = 99 }).ToList();
        await Assert.That(() => SharedParameterFileService.Save(path, read.Stamp, read.Groups, orphan, path))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_new_file_is_created_with_revit_header_and_utf16()
    {
        string path = Path.Combine(_dir, "new.txt");
        SharedParameterFileService.Save(path, null,
            [new SharedParameterGroupDto { Id = 1, Name = "Common" }],
            [new SharedParameterDto { Guid = Guid.NewGuid().ToString("D"), Name = "Note", DataType = "TEXT", GroupId = 1 }],
            null);

        string text = File.ReadAllText(path);
        await Assert.That(text).StartsWith("# This is a Revit shared parameter file.");
        await Assert.That(SharedParameterFile.Parse(text).Parameters.Single().Name).IsEqualTo("Note");
        byte[] bytes = File.ReadAllBytes(path);
        await Assert.That(bytes[0] == 0xFF && bytes[1] == 0xFE).IsTrue();
    }

    [Test]
    public async Task Tabs_and_line_breaks_in_a_description_do_not_split_the_row()
    {
        SharedParameterFile file = SharedParameterFile.Parse(RevitFile);
        file.Parameters[0].Description = "line one\nline\ttwo";

        SharedParameterFile back = SharedParameterFile.Parse(file.Serialize());
        await Assert.That(back.Parameters.Count).IsEqualTo(2);
        await Assert.That(back.Parameters[0].Description).IsEqualTo("line one line two");
    }
}
