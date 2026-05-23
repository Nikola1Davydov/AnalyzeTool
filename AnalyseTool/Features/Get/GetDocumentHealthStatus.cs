using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace AnalyseTool.Features.Get
{
    internal sealed class GetDocumentHealthStatus : IRevitTask
    {
        private Document _doc = null!;

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app =>
            {
                _doc = app.ActiveUIDocument.Document;
                return new DocumentHealth()
                {
                    // warnings
                    TotalWarnings = Create("Total warnings", GetTotalWarnings()),
                    FileSize = Create("File size (MB)", GetFileSize()),
                    TotalPlacedElements = Create("Total placed elements", GetPlacedElements()),

                    // models
                    ModelGroups = Create("Model groups", GetModelGroups()),
                    DetailGroups = Create("Detail groups", GetDetailGroups()),
                    InPlaceFamilies = Create("In-place families", GetInPlaceFamilies()),

                    // views and sheets
                    TotalViews = Create("Total views", GetViews()),
                    HiddenElementsInViews = Create("Hidden elements in views", GetHiddenElements()),
                    ViewsNotOnSheets = Create("Views not on sheets", GetViewsNotOnSheets()),
                    Sheets = Create("Sheets", GetSheets()),

                    // links
                    RevitLinks = Create("Revit links", GetRevitLinks()),
                    CadLinks = Create("CAD links", GetCadLinks()),

                    // imports
                    CadImports = Create("CAD imports", GetCadImports()),
                };
            });
        private KeyValuePair<string, int> Create(string titel, int count) =>
            new KeyValuePair<string, int>(titel, count);

        private int GetTotalWarnings()
        {
            return _doc.GetWarnings().Count;
        }
        private int GetFileSize()
        {
            if (string.IsNullOrEmpty(_doc.PathName))
                return 0;

            FileInfo fileInfo = new FileInfo(_doc.PathName);
            return (int)(fileInfo.Length / 1024 / 1024);
        }
        private int GetPlacedElements()
        {
            return new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .Count();
        }
        private int GetModelGroups()
        {
            return new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_IOSModelGroups)
                .WhereElementIsNotElementType()
                .Count();
        }
        private int GetDetailGroups()
        {
            return new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_IOSDetailGroups)
                .WhereElementIsNotElementType()
                .Count();
        }
        private int GetInPlaceFamilies()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Count(fi => fi.Symbol.Family.IsInPlace);
        }
        private int GetViews()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(v => !v.IsTemplate);
        }

        private int GetSheets()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Count();
        }
        private int GetViewsNotOnSheets()
        {
            HashSet<ElementId> viewsOnSheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Select(vp => vp.ViewId)
                .ToHashSet();

            return new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(v => !v.IsTemplate && !viewsOnSheets.Contains(v.Id));
        }
        private int GetRevitLinks()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Count();
        }

        private int GetCadLinks()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Count(i => i.IsLinked);
        }

        private int GetCadImports()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Count(i => !i.IsLinked);
        }
        private int GetHiddenElements()
        {
            int count = 0;

            IEnumerable<View> views = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate);

            foreach (View? view in views)
            {
                count += new FilteredElementCollector(_doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Count(e => e.IsHidden(view));
            }

            return count;
        }
        private class DocumentHealth
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
}
