namespace AnalyseTool.RevitCommands.Commands.Base
{
    internal interface IRevitTask
    {
        void Execute(object elementsIds);
    }
}