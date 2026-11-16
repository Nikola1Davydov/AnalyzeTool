using System.IO;
using System.Text.Json;
using Autodesk.Revit.UI;
using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Threading;
using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
///     The document key and the header of <c>DocumentCheckMiddleware</c>, without the Revit user interface.
/// </summary>
public sealed class DocumentKeyTests : RevitApiTest
{
    private Document _document = null!;

    [Before(Test)]
    public void CreateDocument()
    {
        _document = Application.NewProjectDocument(UnitSystem.Metric);
    }

    [After(Test)]
    public void CloseDocument()
    {
        var path = _document.PathName;
        _document.Close(false);

        if (path.Length > 0)
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task GetKey_UnsavedDocument_IsTheTitle()
    {
        await Assert.That(ActiveDocument.GetKey(_document)).IsEqualTo(_document.Title);
    }

    [Test]
    public async Task GetKey_SavedDocument_IsThePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.rvt");
        _document.SaveAs(path);

        await Assert.That(ActiveDocument.GetKey(_document)).IsEqualTo(_document.PathName);
    }

    [Test]
    public async Task DocumentCheck_HeaderWithKey_FixesTheDocumentOfTheCall()
    {
        var activeDocument = new ActiveDocument(new UnusedDispatcher());
        var request = new CommandRequest("Any", default(JsonElement), CommandSources.WebView)
        {
            Headers = new Dictionary<string, string> { [DocumentCheckMiddleware.Header] = @"C:\Models\A.rvt" }
        };

        await new DocumentCheckMiddleware(activeDocument).InvokeAsync(new CommandContext(null, request, null!), _ => Task.FromResult<object?>(null));

        await Assert.That(activeDocument.Key).IsEqualTo(@"C:\Models\A.rvt");
    }

    [Test]
    public async Task DocumentCheck_NoHeader_LeavesTheDocumentToTheFirstAccess()
    {
        var activeDocument = new ActiveDocument(new UnusedDispatcher());
        var request = new CommandRequest("Any", default(JsonElement), CommandSources.Ribbon);

        await new DocumentCheckMiddleware(activeDocument).InvokeAsync(new CommandContext(null, request, null!), _ => Task.FromResult<object?>(null));

        await Assert.That(activeDocument.Key).IsNull();
    }
}

/// <summary>The middleware tests never reach the model; a real dispatcher would need the Revit user interface.</summary>
internal sealed class UnusedDispatcher : IRevitDispatcher
{
    public Task<T> InvokeAsync<T>(Func<UIApplication, T> work, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task InvokeAsync(Action<UIApplication> work, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
