using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands.Base
{
    internal interface IRevitTask
    {
        void Execute(JObject elementsIds);
    }
}