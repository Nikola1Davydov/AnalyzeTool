using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>One integer cell coordinate in the wire payloads (Minecraft convention: y is UP).</summary>
    internal sealed record CellDto
    {
        [JsonProperty("x")]
        [Description("Cell x (east).")]
        public int X { get; set; }

        [JsonProperty("y")]
        [Description("Cell y (UP — Minecraft convention).")]
        public int Y { get; set; }

        [JsonProperty("z")]
        [Description("Cell z (south).")]
        public int Z { get; set; }
    }
}
