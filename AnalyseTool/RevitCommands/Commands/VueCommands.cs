using AnalyseTool.RevitCommands.Commands.Base;
using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands.Commands
{
    internal record VueCommands
    {
        public CommandsEnum CommandsEnum { get; init; }
        public object JsonData { get; init; }
    }
}

