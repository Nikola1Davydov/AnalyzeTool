using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Core.Features.Extensions;
using AnalyseTool.Core.Features.Scripting;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tests;

/// <summary>Which commands sit behind the C# switch for AI callers. It used to be a list of names in the
/// bridge, so a new authoring command was exposed until someone remembered the list; now each command
/// declares [CodeAuthoringTool] and the registration carries it.</summary>
public class CodeAuthoringGateTests
{
    [Test]
    public async Task The_authoring_commands_are_exactly_these()
    {
        // Some Core types (RevitTaskHub : IExternalEventHandler) cannot load without Revit; they are
        // not commands, so the ones that did load are the ones to look at.
        Type[] types;
        try { types = typeof(CommandDispatcher).Assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        string[] marked = types
            .Where(t => typeof(IRevitTask).IsAssignableFrom(t) && t.IsDefined(typeof(CodeAuthoringToolAttribute), false))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToArray();

        // Changing this list is a security decision: say why in the commit that changes it.
        await Assert.That(marked).IsEquivalentTo(new[]
        {
            nameof(ExecuteRevitCode), nameof(ReloadExtensionsCommand), nameof(SaveExtensionUi), nameof(UpdateExtensionManifest),
        });
    }

    [Test]
    public async Task The_registration_carries_the_mark()
    {
        using (Assert.Multiple())
        {
            await Assert.That(CommandDispatcher.Describe(typeof(ExecuteRevitCode), "core", null).IsCodeAuthoringTool).IsTrue();
            await Assert.That(CommandDispatcher.Describe(typeof(ReloadExtensionsCommand), "core", null).IsCodeAuthoringTool).IsTrue();
            await Assert.That(CommandDispatcher.Describe(typeof(GetInstalledExtensions), "core", null).IsCodeAuthoringTool).IsFalse();
        }
    }
}
