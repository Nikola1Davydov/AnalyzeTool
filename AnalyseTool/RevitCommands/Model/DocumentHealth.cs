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
        [Required]
        public KeyValuePair<string, int> PurgableElements { get; set; }

        // models 
        [Required]
        public KeyValuePair<string, int> ModelGroups { get; set; }
        [Required]
        public KeyValuePair<string, int> DetailGroups { get; set; }
        [Required]
        public KeyValuePair<string, int> InPlaceFamilies { get; set; }
        [Required]
        public KeyValuePair<string, int> Duplicates { get; set; }

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
        [Required]
        public KeyValuePair<string, int> RasterLinks { get; set; }
        [Required]
        public KeyValuePair<string, int> PDFLinks { get; set; }

        // imports
        [Required]
        public KeyValuePair<string, int> RevitImports { get; set; }
        [Required]
        public KeyValuePair<string, int> CadImports { get; set; }
        [Required]
        public KeyValuePair<string, int> RasterImports { get; set; }
        [Required]
        public KeyValuePair<string, int> PDFImports { get; set; }
    }
}
