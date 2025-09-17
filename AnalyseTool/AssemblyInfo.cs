using SharedData;
using System.Reflection;

// Общая информация о сборке
[assembly: AssemblyTitle(ToolData.PLUGIN_NAME)]
[assembly: AssemblyProduct(ToolData.PLUGIN_NAME)]

// Версия сборки
// Формат: [Major].[Minor].[Build].[Revision]
[assembly: AssemblyVersion(ToolData.PLUGIN_VERSION)]        // используется CLR для связывания
[assembly: AssemblyFileVersion(ToolData.PLUGIN_VERSION)]   // видна в свойствах файла (Windows Explorer)
[assembly: AssemblyInformationalVersion(ToolData.PLUGIN_VERSION)] // можно указывать "1.0.0-beta"
