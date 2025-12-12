using System.ComponentModel.DataAnnotations;

namespace AnalyseTool.RevitCommands.Model
{
    internal class DocumentHealth
    {
        // warnings
        [Required]
        public KeyValuePair<string, int> TotalWarnings { get; set; }
        [Required]
        public KeyValuePair<string, int> FileSize { get; set; }
        [Required]
        public KeyValuePair<string, int> TotalPlacedElements { get; set; }

        // models 
        [Required]
        public KeyValuePair<string, int> ModelGroups { get; set; }
        [Required]
        public KeyValuePair<string, int> DetailGroups { get; set; }
        [Required]
        public KeyValuePair<string, int> InPlaceFamilies { get; set; }

        // views and sheets
        [Required]
        public KeyValuePair<string, int> TotalViews { get; set; }
        [Required]
        public KeyValuePair<string, int> HiddenElementsInViews { get; set; }
        [Required]
        public KeyValuePair<string, int> ViewsNotOnSheets { get; set; }
        [Required]
        public KeyValuePair<string, int> Sheets { get; set; }

        // links
        [Required]
        public KeyValuePair<string, int> RevitLinks { get; set; }
        [Required]
        public KeyValuePair<string, int> CadLinks { get; set; }

        // imports
        [Required]
        public KeyValuePair<string, int> CadImports { get; set; }
    }
}
