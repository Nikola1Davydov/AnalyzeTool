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
