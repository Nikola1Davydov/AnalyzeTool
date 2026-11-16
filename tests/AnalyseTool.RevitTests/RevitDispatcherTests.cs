using AnalyseTool.Hosting.Threading;
using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
///     <c>IRevitDispatcher</c> in a real Revit. Test bodies run inside a Revit API context, so the dispatcher's
///     <c>AllowDirectInvocation</c> runs the work at once — the path of a ribbon button.
/// </summary>
public sealed class RevitDispatcherTests : RevitApiUiTest
{
    [Test]
    public async Task InvokeAsync_InApiContext_ReturnsTheResultOfTheWork()
    {
        var dispatcher = new RevitDispatcher();

        var version = await dispatcher.InvokeAsync(application => application.Application.VersionNumber);

        await Assert.That(version).IsEqualTo(UiApplication.Application.VersionNumber);
    }

    [Test]
    public async Task InvokeAsync_WorkThrows_PassesTheExceptionToTheCaller()
    {
        var dispatcher = new RevitDispatcher();

        await Assert.That(() => dispatcher.InvokeAsync(_ => throw new InvalidOperationException("Boom.")))
            .Throws<InvalidOperationException>()
            .WithMessage("Boom.");
    }
}
