using System;
using System.IO;
using System.Linq;
using WixSharp;
using WixSharp.UI.WPF;

namespace Installer
{
    public class Program
    {
        private static void Main()
        {
            System.IO.DirectoryInfo solutionPath = TryGetSolutionDirectoryInfo();
            string plaginPath24 = Path.Combine(solutionPath.FullName, @"AnalyseTool\bin\Release R24\publish\Revit 2024 Release R24 addin", @"*.*");
            string targetPath24 = Path.Combine("%CommonAppDataFolder%", @"Autodesk\Revit\Addins\2024");
            Compiler.GetMappedWixConstants(true).ForEach(Console.WriteLine);

            Project project = new Project("AnalyzeTool",
                              new Dir(targetPath24,
                                  new Files(plaginPath24)));

            //  todo: add icon
            project.GUID = new Guid("e74a2dbc-8131-4240-abde-e2a776451bba");
            project.Version = new Version(1, 0, 1, 0);
            project.Scope = InstallScope.perUser;
            project.LicenceFile = "";
            project.UI = WUI.WixUI_ProgressOnly;

            project.BuildMsi();
        }

        // todo: add check for solution path
        public static System.IO.DirectoryInfo TryGetSolutionDirectoryInfo(string currentPath = null)
        {
            System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(
                currentPath ?? System.IO.Directory.GetCurrentDirectory());
            while (directory != null && !directory.GetFiles("*.sln").Any())
            {
                directory = directory.Parent;
            }
            return directory;
        }
    }
}