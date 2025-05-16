using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;

namespace SiCadLauncher
{
    //[Transaction(TransactionMode.Manual)]
    //internal class TestCommand : IExternalCommand
    //{
    //    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    //    {
    //        UIApplication uiapp = commandData.Application;

    //        RuntimeCompiler reloadAssembly = new RuntimeCompiler();
    //        Assembly assembly = reloadAssembly.CreateCommand();
    //        foreach (var type in assembly.GetTypes())
    //        {
    //            Console.WriteLine($"Type: {type}");
    //        }
    //        var commands = assembly.GetTypes().Where(e => typeof(IExternalCommand).IsAssignableFrom(e));
    //        foreach (var command in commands)
    //        {
    //            Console.WriteLine($"Command: {command}");
    //            var ribbonPanel = App.currentPanel;
    //            //ribbonPanel.AddItem(ribbonPanel.NewPushButtonData(command));
    //            foreach (var com in commands)
    //            {
    //                App.AddPushButton(ribbonPanel, "test", com.FullName);

    //            }
    //        }
    //        return Result.Succeeded;
    //    }
    //}
}
