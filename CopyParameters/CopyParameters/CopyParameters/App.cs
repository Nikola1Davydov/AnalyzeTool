using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CopyParameters
{
    internal class App : IExternalApplication
    {
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "TabParameters";
            application.CreateRibbonTab(tabName);

            RibbonPanel ribbonPanel = application.CreateRibbonPanel(tabName, "PanelParameters");

            PushButtonData buttonData = new PushButtonData(nameof(CopyParamatersCommand), "Copy Parameters", typeof(CopyParamatersCommand).Assembly.Location, typeof(CopyParamatersCommand).FullName);

            PushButton pushButton = ribbonPanel.AddItem(buttonData) as PushButton;

            return Result.Succeeded;
        }
    }
}
