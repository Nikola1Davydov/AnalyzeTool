using SharedData;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle(ToolData.PLUGIN_NAME)]
[assembly: AssemblyProduct(ToolData.PLUGIN_NAME)]

//[Major].[Minor].[Build].[Revision]
[assembly: AssemblyVersion(ToolData.PLUGIN_VERSION)]
[assembly: AssemblyFileVersion(ToolData.PLUGIN_VERSION)]
[assembly: AssemblyInformationalVersion(ToolData.PLUGIN_VERSION)]

// Declared HERE because GenerateAssemblyInfo=False also drops the csproj <InternalsVisibleTo> items.
// The host bootstrap registers this assembly's commands (typeof(...).Assembly) and a few host
// features still touch internal models; keep internal so extensions can't bind.
[assembly: InternalsVisibleTo("AnalyseTool.App")]

// The Revit-free unit tests. Commands are internal, and the ones worth testing — Filter above all,
// which decides what a purge deletes — are pure functions of their request.
[assembly: InternalsVisibleTo("AnalyseTool.Tools.Tests")]
