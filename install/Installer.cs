using System.Text.Json;
using Installer;
using WixSharp;
using WixSharp.CommonTasks;
using WixSharp.Controls;

// build/ runs the installer from the repository root: args[0] is the version, the rest are the publish folders
var settings = JsonDocument.Parse(System.IO.File.ReadAllText(@"build\appsettings.json")).RootElement;
var productName = settings.GetProperty("Product").GetProperty("Name").GetString()!;
var upgradeCode = new Guid(settings.GetProperty("Product").GetProperty("UpgradeCode").GetString()!);
var vendorName = settings.GetProperty("Vendor").GetProperty("Name").GetString()!;

var versioning = Versioning.CreateFromVersionString(args[0]);
var project = new Project
{
    OutDir = "output",
    Name = productName,
    Platform = Platform.x64,
    UI = WUI.WixUI_FeatureTree,
    // Until the first tag every build is 0.0.1 for MSI (it keeps only X.Y.Z), so a test build must replace the previous one
    MajorUpgrade = new MajorUpgrade
    {
        AllowSameVersionUpgrades = true,
        DowngradeErrorMessage = "A newer version is already installed."
    },
    GUID = upgradeCode,
    BannerImage = @"install\Resources\Icons\BannerImage.png",
    BackgroundImage = @"install\Resources\Icons\BackgroundImage.png",
    Version = versioning.VersionPrefix,
    ControlPanelInfo =
    {
        Manufacturer = vendorName,
        ProductIcon = @"install\Resources\Icons\ShellIcon.ico"
    }
};

var wixEntities = Generator.GenerateWixEntities(args[1..]);
project.RemoveDialogsBetween(NativeDialogs.WelcomeDlg, NativeDialogs.CustomizeDlg);

BuildSingleUserMsi();
BuildMultiUserMsi();

void BuildSingleUserMsi()
{
    project.Scope = InstallScope.perUser;
    project.OutFileName = $"{productName}-{versioning.Version}-SingleUser";
    project.Dirs =
    [
        new Dir(@"%AppDataFolder%\Autodesk\Revit\Addins\", [.. wixEntities.Select(entity => entity.Directory)])
    ];
    project.BuildMsi();
}

void BuildMultiUserMsi()
{
    project.Scope = InstallScope.perMachine;
    project.OutFileName = $"{productName}-{versioning.Version}-MultiUser";

    project.Dirs = wixEntities
        .GroupBy(entity => entity.Version switch
        {
            >= 2027 => @"%ProgramFiles%\Autodesk\Revit\Addins",
            _ => @"%CommonAppDataFolder%\Autodesk\Revit\Addins"
        })
        .Select(root => new Dir(root.Key, [.. root.Select(entity => entity.Directory)]))
        .ToArray();

    project.BuildMsi();
}
