using AnalyseTool.RevitCommands.Commands;
using Autodesk.Revit.UI;

namespace AnalyseTool.RevitCommands.DataModel
{
    internal class CommandsFactory
    {
        public static IRevitTask CreateRevitCommand(string command)
        {
            bool result = Enum.TryParse<CommandsEnum>(command, ignoreCase: true, out CommandsEnum parsedCommand);
            if (!result) TaskDialog.Show("Error", $"The command {command} is not recognized.");

            return parsedCommand switch
            {
                CommandsEnum.Selection => new SelectionInRevit(),
                CommandsEnum.Isolation => new IsolationInRevit(),
                CommandsEnum.GetCategories => new GetCategoriesInRevit(),
                CommandsEnum.GetDataByCategoryName => new GetDataByCategoryName(),
                CommandsEnum.CheckUpdate => new CheckUpdate(),

                _ => throw new NotImplementedException($"The command {command} is not implemented in the CommandsFactory."),
            };
        }
    }
}
