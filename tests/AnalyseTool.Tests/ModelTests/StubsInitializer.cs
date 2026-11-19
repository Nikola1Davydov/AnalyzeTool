using System.Runtime.CompilerServices;
using AnalyseTool.Tests.Extensions;

namespace AnalyseTool.Tests.ModelTests;

/// <summary>
///     The model tests name Revit types (categories, parameters, rooms) in the test methods themselves: the stubs must
///     answer before any of them is loaded, not only once an extension environment exists.
/// </summary>
internal static class StubsInitializer
{
    [ModuleInitializer]
    internal static void Install()
    {
        RevitStubs.Install();
    }
}
