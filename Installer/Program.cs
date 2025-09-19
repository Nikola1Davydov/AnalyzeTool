using Installer;
using System;
using System.IO;
using System.Linq;
using WixSharp;

const string outputName = SharedData.ToolData.PLUGIN_NAME;
const string projectName = SharedData.ToolData.PLUGIN_NAME;
const string projectVersion = SharedData.ToolData.PLUGIN_VERSION;

System.IO.DirectoryInfo solutionPath = TryGetSolutionDirectoryInfo();
string targetPath(string revitVersion) => Path.Combine("%CommonAppDataFolder%", $@"Autodesk\Revit\Addins\20{revitVersion}");
string plaginPath(string revitVersion)
{
    int revitVersionInt = int.Parse(revitVersion);
    if (revitVersionInt < 25)
    {
        return Path.Combine(solutionPath.FullName, $@"AnalyseTool\bin\Release R{revitVersion}\net48");
    }
    {
        return Path.Combine(solutionPath.FullName, $@"AnalyseTool\bin\Release R{revitVersion}\net8.0-windows");
    }
}

string[] versions =
{
    plaginPath("24"),
    plaginPath("25"),
    plaginPath("26"),
};


Project project = new Project
{
    OutDir = "output",
    Name = projectName,
    Platform = Platform.x64,
    UI = WUI.WixUI_InstallDir,
    MajorUpgrade = MajorUpgrade.Default,
    GUID = new Guid("e74a2dbc-8131-4240-abde-e2a776451bba"),
    LicenceFile= Path.Combine(solutionPath.FullName, "Installer", "LICENSE.rtf"),
    //BannerImage = @"",
    //BackgroundImage = @"",
    Version = new Version(projectVersion),
    ControlPanelInfo =
    {
        Manufacturer = SharedData.ToolData.PLUGIN_AUTHOR,
        //ProductIcon = Path.Combine(solutionPath.FullName, "SharedData/Resources/Icons/AnalyzeTool_Icon.ico"),
        Name = SharedData.ToolData.PLUGIN_NAME,
        Readme = SharedData.ToolData.LINK_TO_GITHUB,
        HelpLink = SharedData.ToolData.LINK_TO_GITHUB,
        UrlInfoAbout = SharedData.ToolData.LINK_TO_GITHUB,
        UrlUpdateInfo = SharedData.ToolData.LINK_TO_DOWNLOADS
    }
};

WixEntity[] wixEntities = Generator.GenerateWixEntities(versions);
//project.RemoveDialogsBetween(NativeDialogs.WelcomeDlg, NativeDialogs.CustomizeDlg);

BuildSingleUserMsi();
BuildMultiUserUserMsi();

void BuildSingleUserMsi()
{
    project.InstallScope = InstallScope.perUser;
    project.OutFileName = $"{outputName}-{project.Version}-SingleUser";
    project.Dirs =
    [
        new InstallDir(@"%AppDataFolder%\Autodesk\Revit\Addins\", wixEntities)
    ];
    project.BuildMsi();
}

void BuildMultiUserUserMsi()
{
    project.InstallScope = InstallScope.perMachine;
    project.OutFileName = $"{outputName}-{project.Version}-MultiUser";
    project.Dirs =
    [
        new InstallDir(@"%CommonAppDataFolder%\Autodesk\Revit\Addins\", wixEntities)
    ];
    project.BuildMsi();
}

static System.IO.DirectoryInfo TryGetSolutionDirectoryInfo(string currentPath = null)
{
    System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(
        currentPath ?? System.IO.Directory.GetCurrentDirectory());
    while (directory != null && !directory.GetFiles("*.sln").Any())
    {
        directory = directory.Parent;
    }
    return directory;
}