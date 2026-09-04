using Autodesk.Revit.DB;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>One element as the index stores it. Numbers are in the document's DISPLAY units, the
    /// unit every value the platform reads or writes is expressed in (#113). <see cref="VersionGuid"/>
    /// is what a reconcile compares: it changes whenever Revit modifies the element.</summary>
    internal sealed record ElementRow(
        string UniqueId, long ElementId, bool IsType,
        string? Category, string? BuiltInCategory, string? CategoryType,
        string Name, string? FamilyName, string? TypeName, long? TypeElementId,
        long? LevelId, long? WorksetId, string VersionGuid,
        double? LocX, double? LocY, double? LocZ,
        double? BboxMinX, double? BboxMinY, double? BboxMinZ,
        double? BboxMaxX, double? BboxMaxY, double? BboxMaxZ);

    /// <summary>A parameter DEFINITION, stored once: the name a person sees, the language-independent
    /// built-in name or shared GUID (#121: shared parameters are identified by GUID, never by name),
    /// what the value means (spec) and the display unit it is stored in.</summary>
    internal sealed record ParameterDef(
        long ParamId, string Name, string? BuiltInParameter, string? SharedGuid,
        string StorageType, string? Spec, string? Unit, bool IsReadOnly);

    /// <summary>One parameter VALUE on one element. All three value columns null means "the parameter is
    /// there and empty" — a different finding from "the parameter is not there", which has no row
    /// (#121: the two need different fixes).</summary>
    internal sealed record ParameterValueRow(long ElementId, long ParamId, string? ValueText, double? ValueNum, long? ValueId);

    /// <summary>What one <see cref="Read"/> yields: the element's row, the definitions this reader had not
    /// seen before (so a caller can insert them once), and the element's values.</summary>
    internal sealed record ElementRead(ElementRow Row, IReadOnlyList<ParameterDef> NewDefs, IReadOnlyList<ParameterValueRow> Values);

    /// <summary>
    /// Reads elements into index rows. A function of the <see cref="Document"/>, so it is testable inside
    /// Revit without a UIApplication (tier 3) and reusable by the indexer proper; the command decides the
    /// chunking and the thread, this class only reads. Revit-thread only: it holds Revit objects in its
    /// caches (element types, format options), which must not be touched from anywhere else.
    ///
    /// The value reading here mirrors AnalyseTool.Tools/Shared/ParameterExtensions.cs (DescribeUnits and
    /// the display-unit conversion); Core cannot reference Tools, so this is a deliberate second copy.
    /// If the two start to drift, the helper moves into the Sdk as a contract decision.
    /// </summary>
    internal sealed class ElementRowReader
    {
        private readonly Document _doc;
        private readonly Units _units;
        private readonly ForgeTypeId? _lengthUnit;
        private readonly bool _withParameters;
        private readonly Dictionary<long, ElementType?> _types = new();
        private readonly Dictionary<string, ForgeTypeId?> _unitBySpec = new(StringComparer.Ordinal);
        private readonly HashSet<long> _knownDefs = new();

        public ElementRowReader(Document doc, bool withParameters)
        {
            _doc = doc;
            _units = doc.GetUnits();
            _withParameters = withParameters;
            _lengthUnit = SafeUnit(SpecTypeId.Length);
        }

        /// <summary>Everything the index covers: instances AND types of every model category, plus the
        /// levels (a datum, not a model category, and the join every other row needs). By category id
        /// rather than BuiltInCategory: a document may carry categories the enum does not name.</summary>
        public static IReadOnlyList<ElementId> CollectIds(Document doc)
        {
            List<ElementFilter> filters = doc.Settings.Categories
                .Cast<Category>()
                .Where(category => category != null && category.CategoryType == CategoryType.Model)
                .Select(category => (ElementFilter)new ElementCategoryFilter(category.Id))
                .ToList();

            HashSet<ElementId> ids = new();
            if (filters.Count > 0)
                foreach (ElementId id in new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(filters)).ToElementIds())
                    ids.Add(id);
            foreach (ElementId id in new FilteredElementCollector(doc).OfClass(typeof(Level)).ToElementIds())
                ids.Add(id);
            return ids.ToList();
        }

        /// <summary>The reconcile pass: (id, version) of every element WITHOUT reading anything else. Its
        /// cost is what decides whether "sweep and re-read only what changed" is cheap enough to run on
        /// every document open.</summary>
        public static List<(long Id, Guid Version)> SweepVersions(Document doc, IReadOnlyList<ElementId> ids)
        {
            List<(long, Guid)> result = new(ids.Count);
            foreach (ElementId id in ids)
            {
                Element? element = doc.GetElement(id);
                if (element is null) continue;
                result.Add((id.Value, element.VersionGuid));
            }
            return result;
        }

        public ElementRead Read(Element element)
        {
            ElementType? type = element as ElementType ?? ResolveType(element);
            Category? category = element.Category;

            long? typeId = element is ElementType || element.GetTypeId() == ElementId.InvalidElementId
                ? null
                : element.GetTypeId().Value;
            long? levelId = element.LevelId == ElementId.InvalidElementId ? null : element.LevelId.Value;
            long? worksetId = _doc.IsWorkshared ? element.WorksetId.IntegerValue : null;

            (double? lx, double? ly, double? lz) = Location(element);
            BoundingBoxXYZ? box = SafeBox(element);

            ElementRow row = new(
                element.UniqueId, element.Id.Value, element is ElementType,
                category?.Name, BuiltInName(category), category?.CategoryType.ToString(),
                element.Name, FamilyName(type), type?.Name, typeId,
                levelId, worksetId, element.VersionGuid.ToString(),
                lx, ly, lz,
                Length(box?.Min.X), Length(box?.Min.Y), Length(box?.Min.Z),
                Length(box?.Max.X), Length(box?.Max.Y), Length(box?.Max.Z));

            if (!_withParameters) return new ElementRead(row, Array.Empty<ParameterDef>(), Array.Empty<ParameterValueRow>());

            List<ParameterDef> newDefs = new();
            List<ParameterValueRow> values = new();
            foreach (Parameter parameter in element.Parameters)
            {
                Definition? definition = parameter.Definition;
                if (definition is null) continue;
                long paramId = parameter.Id.Value;
                if (_knownDefs.Add(paramId)) newDefs.Add(Describe(parameter, definition, paramId));
                values.Add(ReadValue(element.Id.Value, parameter, definition, paramId));
            }
            return new ElementRead(row, newDefs, values);
        }

        private ParameterDef Describe(Parameter parameter, Definition definition, long paramId)
        {
            bool builtIn = ParameterUtils.IsBuiltInParameter(parameter.Id);
            ForgeTypeId? spec = definition.GetDataType();
            string? specName = spec is null || string.IsNullOrEmpty(spec.TypeId) ? null : ShortForgeId(spec);
            ForgeTypeId? unit = UnitFor(spec);
            return new ParameterDef(
                paramId, definition.Name,
                builtIn ? ((BuiltInParameter)paramId).ToString() : null,
                parameter.IsShared ? parameter.GUID.ToString() : null,
                parameter.StorageType.ToString(),
                specName,
                unit is null ? null : ShortForgeId(unit),
                parameter.IsReadOnly);
        }

        private ParameterValueRow ReadValue(long elementId, Parameter parameter, Definition definition, long paramId)
        {
            if (!parameter.HasValue) return new ParameterValueRow(elementId, paramId, null, null, null);

            switch (parameter.StorageType)
            {
                case StorageType.Double:
                {
                    double value = parameter.AsDouble();
                    ForgeTypeId? unit = UnitFor(definition.GetDataType());
                    if (unit is not null) value = UnitUtils.ConvertFromInternalUnits(value, unit);
                    return new ParameterValueRow(elementId, paramId, SafeValueString(parameter), value, null);
                }
                case StorageType.Integer:
                    return new ParameterValueRow(elementId, paramId, SafeValueString(parameter), parameter.AsInteger(), null);
                case StorageType.String:
                    return new ParameterValueRow(elementId, paramId, parameter.AsString(), null, null);
                case StorageType.ElementId:
                {
                    ElementId id = parameter.AsElementId();
                    long? value = id == ElementId.InvalidElementId ? null : id.Value;
                    return new ParameterValueRow(elementId, paramId, SafeValueString(parameter), null, value);
                }
                default:
                    return new ParameterValueRow(elementId, paramId, null, null, null);
            }
        }

        private ElementType? ResolveType(Element element)
        {
            ElementId typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId) return null;
            if (!_types.TryGetValue(typeId.Value, out ElementType? type))
            {
                type = _doc.GetElement(typeId) as ElementType;
                _types[typeId.Value] = type;
            }
            return type;
        }

        private ForgeTypeId? UnitFor(ForgeTypeId? spec)
        {
            if (spec is null || string.IsNullOrEmpty(spec.TypeId)) return null;
            if (_unitBySpec.TryGetValue(spec.TypeId, out ForgeTypeId? cached)) return cached;
            ForgeTypeId? unit = SafeUnit(spec);
            _unitBySpec[spec.TypeId] = unit;
            return unit;
        }

        private ForgeTypeId? SafeUnit(ForgeTypeId spec)
        {
            try
            {
                if (!UnitUtils.IsMeasurableSpec(spec)) return null;
                ForgeTypeId? unit = _units.GetFormatOptions(spec)?.GetUnitTypeId();
                return unit is null || string.IsNullOrEmpty(unit.TypeId) ? null : unit;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        private double? Length(double? internalValue) =>
            internalValue is null ? null
            : _lengthUnit is null ? internalValue
            : UnitUtils.ConvertFromInternalUnits(internalValue.Value, _lengthUnit);

        /// <summary>A point for the element: the location point, or the middle of the location curve. An
        /// UNBOUND curve (the spike found one on a real model — a line with no ends cannot be evaluated
        /// at a normalized parameter) yields its origin instead; anything Revit refuses yields nothing.</summary>
        private (double?, double?, double?) Location(Element element)
        {
            XYZ? point;
            try
            {
                point = element.Location switch
                {
                    LocationPoint lp => lp.Point,
                    LocationCurve { Curve: { IsBound: true } curve } => curve.Evaluate(0.5, normalized: true),
                    LocationCurve { Curve: { } curve } => curve.Evaluate(0, normalized: false),
                    _ => null,
                };
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                point = null;
            }
            return point is null ? (null, null, null) : (Length(point.X), Length(point.Y), Length(point.Z));
        }

        private static BoundingBoxXYZ? SafeBox(Element element)
        {
            try { return element.get_BoundingBox(null); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        private static string? SafeValueString(Parameter parameter)
        {
            try { return parameter.AsValueString(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        private static string? FamilyName(ElementType? type) =>
            type is not null && !string.IsNullOrWhiteSpace(type.FamilyName) ? type.FamilyName : null;

        /// <summary>"OST_Walls" for a built-in category, null for anything else — the enum name is the
        /// language-independent id; a value the enum does not define has no name to give.</summary>
        private static string? BuiltInName(Category? category)
        {
            if (category is null) return null;
            BuiltInCategory bic = category.BuiltInCategory;
            return bic == BuiltInCategory.INVALID || !Enum.IsDefined(typeof(BuiltInCategory), bic) ? null : bic.ToString();
        }

        /// <summary>"autodesk.spec.aec:length-2.0.0" → "length"; "autodesk.unit.unit:millimeters-1.0.1" →
        /// "millimeters". Same rule as Tools' ParameterExtensions.ShortForgeId.</summary>
        internal static string ShortForgeId(ForgeTypeId id)
        {
            string s = id.TypeId;
            int colon = s.LastIndexOf(':');
            if (colon >= 0) s = s[(colon + 1)..];
            int dash = s.IndexOf('-');
            if (dash > 0) s = s[..dash];
            return s.StartsWith("spec.", StringComparison.Ordinal) ? s[5..] : s;
        }
    }
}
