using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class GetDocumentHealthStatus : IRevitTask
    {
        Document _doc = Context.Document;
        public async Task Execute(JToken payload, WebView2 webView)
        {
            int allWarningsInDocument = Context.Document.GetWarnings().Count;
            var path =  Context.Document.PathName;
            DocumentHealth docHealth = new DocumentHealth()
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

            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(GetDocumentHealthStatus),
                Payload = JObject.FromObject(docHealth)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
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

            var fileInfo = new System.IO.FileInfo(_doc.PathName);
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
            var viewsOnSheets = new FilteredElementCollector(_doc)
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

            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate);

            foreach (var view in views)
            {
                count += new FilteredElementCollector(_doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Count(e => e.IsHidden(view));
            }

            return count;
        }

    }
}
