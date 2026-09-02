using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
/// #85 asked for a one-minute experiment to settle whether Document.CreationGUID survives Save As —
/// it decides whether a copy of a model inherits the original's history. Settled here permanently,
/// on a fresh in-memory project saved twice under new names, never on a user's model.
/// </summary>
public sealed class DocumentIdentityTests : RevitApiTest
{
    [Test]
    public async Task CreationGUID_survives_SaveAs_so_a_copy_inherits_it()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AnalyseTool-guid-probe");
        Directory.CreateDirectory(dir);
        string first = Path.Combine(dir, "first.rvt"), second = Path.Combine(dir, "second.rvt");
        foreach (string f in new[] { first, second }) if (File.Exists(f)) File.Delete(f);

        Document doc = Application.NewProjectDocument(UnitSystem.Metric);
        Guid fresh = doc.CreationGUID;
        doc.SaveAs(first, new SaveAsOptions { OverwriteExistingFile = true });
        Guid afterFirst = doc.CreationGUID;
        doc.SaveAs(second, new SaveAsOptions { OverwriteExistingFile = true });
        Guid afterSecond = doc.CreationGUID;
        doc.Close(false);

        Document firstFile = Application.OpenDocumentFile(first);
        Guid firstGuid = firstFile.CreationGUID;
        firstFile.Close(false);
        Document secondFile = Application.OpenDocumentFile(second);
        Guid secondGuid = secondFile.CreationGUID;
        secondFile.Close(false);

        // Measured 2026-09-02 on Revit 2025: the GUID never changes — not on the first Save As, not on
        // the second, and both files carry the original's. A copy therefore inherits the identity, and
        // a history keyed on CreationGUID alone would follow the copy: the key needs PathName beside it
        // (or the central model's GUID for a workshared model), exactly as #85 planned for this outcome.
        using (Assert.Multiple())
        {
            await Assert.That(afterFirst).IsEqualTo(fresh);
            await Assert.That(afterSecond).IsEqualTo(fresh);
            await Assert.That(firstGuid).IsEqualTo(fresh);
            await Assert.That(secondGuid).IsEqualTo(fresh);
        }
    }
}
