using System.IO;
using Autodesk.Revit.UI;
using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Threading;
using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
///     The document rule of the template (plan, section 7) with two real project documents, A and B.
/// </summary>
/// <remarks>
///     The documents stay open until Revit closes: the API cannot close the active document.
/// </remarks>
public sealed class ActiveDocumentTests : RevitApiUiTest
{
    private static string? _pathA;
    private static string? _pathB;

    [Before(Test)]
    public void CreateDocuments()
    {
        // Once for the class; files, not open documents, so every test activates what it needs
        _pathA ??= CreateProject("A");
        _pathB ??= CreateProject("B");
    }

    [Test]
    public async Task InvokeAsync_ActiveDocument_WorksWithItAndRemembersItsKey()
    {
        var documentA = Activate(_pathA);
        var activeDocument = new ActiveDocument(new RevitDispatcher());

        var title = await activeDocument.InvokeAsync(document => document.Title);

        await Assert.That(title).IsEqualTo(documentA.Document.Title);
        await Assert.That(activeDocument.Key).IsEqualTo(ActiveDocument.GetKey(documentA.Document));
    }

    [Test]
    public async Task InvokeAsync_UserSwitchedDocumentsBetweenCalls_ThrowsDocumentChanged()
    {
        Activate(_pathA);
        var activeDocument = new ActiveDocument(new RevitDispatcher());
        await activeDocument.InvokeAsync(document => document.Title);

        Activate(_pathB);

        var exception = await Assert.That(async () => { await activeDocument.InvokeAsync(document => document.Title); })
            .Throws<CommandException>();

        await Assert.That(exception!.Code).IsEqualTo(DocumentErrorCodes.DocumentChanged);
    }

    [Test]
    public async Task InvokeAsync_ExpectedAnotherDocument_ThrowsBeforeTheWork()
    {
        var documentB = Activate(_pathB);
        Activate(_pathA);
        var activeDocument = new ActiveDocument(new RevitDispatcher());
        activeDocument.Expect(ActiveDocument.GetKey(documentB.Document));
        var touched = false;

        var exception = await Assert.That(async () => { await activeDocument.InvokeAsync(_ => touched = true); })
            .Throws<CommandException>();

        await Assert.That(exception!.Code).IsEqualTo(DocumentErrorCodes.DocumentChanged);
        await Assert.That(touched).IsFalse();
    }

    [Test]
    public async Task InvokeAsync_ExpectedTheActiveDocument_Works()
    {
        var documentA = Activate(_pathA);
        var activeDocument = new ActiveDocument(new RevitDispatcher());
        activeDocument.Expect(ActiveDocument.GetKey(documentA.Document));

        var title = await activeDocument.InvokeAsync(document => document.Title);

        await Assert.That(title).IsEqualTo(documentA.Document.Title);
    }

    [Test]
    public async Task InvokeUIAsync_ActiveDocument_GivesItsUIDocument()
    {
        var documentA = Activate(_pathA);
        var activeDocument = new ActiveDocument(new RevitDispatcher());

        var activeViewId = await activeDocument.InvokeUIAsync(document => document.ActiveView.Id);

        await Assert.That(activeViewId).IsEqualTo(documentA.ActiveView.Id);
    }

    private UIDocument Activate(string? path)
    {
        return UiApplication.OpenAndActivateDocument(path);
    }

    private string CreateProject(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}-{Path.GetRandomFileName()}.rvt");
        var document = UiApplication.Application.NewProjectDocument(UnitSystem.Metric);
        document.SaveAs(path);
        document.Close(false);
        return path;
    }
}
