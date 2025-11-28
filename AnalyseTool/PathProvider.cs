using System.IO;
using System.Reflection;

namespace AnalyseTool
{
    internal sealed class PathProvider
    {
        public static string RootDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static string ProfilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitPluginWebView2");
        public static string DebugServerUrl => "http://localhost:22524";
    }
}
