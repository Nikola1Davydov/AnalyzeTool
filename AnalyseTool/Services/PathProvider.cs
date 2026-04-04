using System.IO;
using System.Reflection;

namespace AnalyseTool.Services
{
    internal sealed class PathProvider
    {
        public static string RootDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static string ProfilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SharedData.ToolData.PLUGIN_NAME);
        public static string DebugServerUrl => "http://localhost:22524";
    }
}
