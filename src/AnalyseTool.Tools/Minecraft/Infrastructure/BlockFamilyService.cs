using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// Provides the cube family: one Generic Model family (a 1-block extrusion whose origin is the
    /// block's min corner) with an INSTANCE material parameter, authored programmatically on first
    /// use and cached in the project. Family authoring needs a Generic Model template (.rft) whose
    /// filename is localized per Revit language — a set of known names is searched; when none is
    /// found the caller falls back to DirectShape cubes. Must be called OUTSIDE an open transaction
    /// (family load manages its own).
    /// </summary>
    internal static class BlockFamilyService
    {
        public const string MaterialParamName = "Block Material";

        /// <summary>Localized name fragments of the plain (unhosted) Generic Model template.</summary>
        private static readonly string[] TemplateKeywords =
        {
            "generic model", "allgemeines modell", "modèle générique", "modello generico",
            "modelo genérico", "модель обобщенная", "常规模型",
        };

        /// <summary>Fragments that mark hosted/special variants we must NOT use.</summary>
        private static readonly string[] TemplateExclusions =
        {
            "adaptive", "pattern", "line based", "face based", "wall based", "ceiling based",
            "floor based", "roof based", "two level", "basiert", "adaptiv",
        };

        /// <summary>Returns the ready-to-place symbol, or null when no usable template exists.</summary>
        public static FamilySymbol? EnsureBlockSymbol(Application app, Document doc, double sizeFeet)
        {
            string familyName = FamilyNameFor(sizeFeet);

            Family? family = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

            family ??= AuthorAndLoad(app, doc, familyName, sizeFeet);
            if (family == null) return null;

            ElementId symbolId = family.GetFamilySymbolIds().FirstOrDefault() ?? ElementId.InvalidElementId;
            return doc.GetElement(symbolId) as FamilySymbol;
        }

        /// <summary>Block size is baked into the geometry (families can't be scaled), so each size
        /// gets its own cached family, named by size in millimeters.</summary>
        private static string FamilyNameFor(double sizeFeet) =>
            $"MC_Block_{Math.Round(sizeFeet * 304.8):0}";

        private static Family? AuthorAndLoad(Application app, Document doc, string familyName, double sizeFeet)
        {
            string? template = FindGenericModelTemplate(app);
            if (template == null) return null;

            string tempPath = Path.Combine(Path.GetTempPath(), familyName + ".rfa");
            Document? famDoc = null;
            try
            {
                famDoc = app.NewFamilyDocument(template);

                using (Transaction t = new(famDoc, "Author block"))
                {
                    t.Start();

                    double s = sizeFeet;
                    var profile = new CurveArray();
                    XYZ[] corners = { XYZ.Zero, new(s, 0, 0), new(s, s, 0), new(0, s, 0) };
                    for (int i = 0; i < 4; i++)
                        profile.Append(Line.CreateBound(corners[i], corners[(i + 1) % 4]));
                    var profiles = new CurveArrArray();
                    profiles.Append(profile);

                    SketchPlane plane = SketchPlane.Create(famDoc,
                        Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                    Extrusion cube = famDoc.FamilyCreate.NewExtrusion(true, profiles, plane, s);

                    FamilyParameter materialParam = famDoc.FamilyManager.AddParameter(
                        MaterialParamName, GroupTypeId.Materials, SpecTypeId.Reference.Material, isInstance: true);
                    Parameter cubeMaterial = cube.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    famDoc.FamilyManager.AssociateElementParameterToFamilyParameter(cubeMaterial, materialParam);

                    t.Commit();
                }

                // LoadFamily takes the family name from the document title, so it must be saved once.
                if (File.Exists(tempPath)) File.Delete(tempPath);
                famDoc.SaveAs(tempPath);
                return famDoc.LoadFamily(doc, new OverwriteFamilyLoadOptions());
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or IOException)
            {
                return null;
            }
            finally
            {
                famDoc?.Close(false);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException) { }
            }
        }

        private static string? FindGenericModelTemplate(Application app)
        {
            string dir = app.FamilyTemplatePath;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            return Directory.EnumerateFiles(dir, "*.rft", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    return TemplateKeywords.Any(name.Contains) && !TemplateExclusions.Any(name.Contains);
                })
                // The plain variant has the shortest name ("Metric Generic Model" beats
                // "Metric Generic Model wall based" etc.).
                .OrderBy(f => Path.GetFileName(f).Length)
                .FirstOrDefault();
        }

        private sealed class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
