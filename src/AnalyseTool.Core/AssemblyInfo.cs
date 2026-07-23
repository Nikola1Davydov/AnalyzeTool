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
// The host keeps using Core internals until the split is complete; the types stay internal so
// extensions (which may only see the Sdk contract) cannot bind to platform internals. Transport
// satellites get access to the CommandQueue the same way — one IVT line per trusted transport
// project, nothing else in Core changes.
[assembly: InternalsVisibleTo("AnalyseTool.App")]
[assembly: InternalsVisibleTo("AnalyseTool.Mcp.Bridge")]
