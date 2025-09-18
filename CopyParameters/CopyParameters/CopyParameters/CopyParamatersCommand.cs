using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;

namespace CopyParameters
{
    [Transaction(TransactionMode.Manual)]
    public class CopyParamatersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            Reference sourceRefence = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element, new WallSelectionFilter(), "please select element");
            var targetRefenceList = uidoc.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element, new WallSelectionFilter(), "please select element");

            using (Transaction transaction = new Transaction(doc, "copyParameters"))
            {
                transaction.Start();
                foreach (var targetRefence in targetRefenceList)
                {
                    CopyParameters(doc.GetElement(sourceRefence.ElementId), doc.GetElement(targetRefence.ElementId));
                }
                transaction.Commit();
            }


            return Result.Succeeded;
        }

        private void CopyParameters(Element sourceElement, Element targetElement)
        {
            ElementId levelTop = sourceElement.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).AsElementId();
            double offesetTop = sourceElement.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET).AsDouble();
            ElementId levelBottom = sourceElement.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId();
            double offsetBottom = sourceElement.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).AsDouble();
            double noLevelOffset = sourceElement.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble();

            if (levelTop != ElementId.InvalidElementId)
            {
                targetElement.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(levelTop);
                targetElement.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET).Set(offesetTop);
            }
            else
            {
                targetElement.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(ElementId.InvalidElementId);
                targetElement.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).Set(noLevelOffset);
            }

            targetElement.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).Set(levelBottom);
            targetElement.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(offsetBottom);
        }
        public class WallSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Wall;
            }
            public bool AllowReference(Reference reference, XYZ position)
            {
                return false; // No references allowed
            }
        }
    }
}
