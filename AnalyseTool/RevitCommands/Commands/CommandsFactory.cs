using AnalyseTool.RevitCommands.Commands.Base;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class CommandsFactory
    {
        public static IRevitTask CreateRevitCommand(CommandsEnum command)
        {
            return command switch
            {
                CommandsEnum.Selection => new SelectionInRevit(),
                CommandsEnum.Isolation => new IsolationInRevit(),
                CommandsEnum.GetCategories => new GetCategoriesInRevit(),
                CommandsEnum.updateDataParameterFilledEmptyPage => new UpdateDataParameterFilledEmptyPage(),
                _ => throw new NotImplementedException($"The command {command} is not implemented in the CommandsFactory."),
            };
        }
    }
}
