using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>
    /// Two document-wide reads that are not about families: the workset table, and the type parameters
    /// of a batch of types. They stayed in the platform when the Family Manager moved into its own
    /// extension, because neither is family-specific and both are useful to anything that reads a model.
    /// </summary>
    internal sealed class TypeAndWorksetService
    {
        public TypeParametersResult GetTypeParameters(Document doc, IReadOnlyList<long> typeIds)
                {
                    List<TypeParametersInfo> types = new();
                    foreach (long id in typeIds ?? [])
                    {
                        if (doc.GetElement(new ElementId(id)) is not ElementType type) continue;
                        // ALL parameters (empty values included): the rule builder must offer every parameter a
                        // type carries — an empty value on one type doesn't make the token useless for others.
                        types.Add(new TypeParametersInfo(id, ReadTypeParameters(type, includeEmpty: true)));
                    }
                    return new TypeParametersResult(types);
                }

        public WorksetsResult GetWorksets(Document doc)
                {
                    if (!doc.IsWorkshared)
                        return new WorksetsResult(false, new List<WorksetInfo>());

                    List<WorksetInfo> worksets = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .Select(ws => new WorksetInfo(
                            ws.Id.IntegerValue, ws.Name, ws.IsOpen, ws.IsEditable, ws.Owner ?? string.Empty))
                        .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new WorksetsResult(true, worksets);
                }

        private static List<FamilyParameterInfo> ReadTypeParameters(Element type, bool includeEmpty = false)
        {
            List<FamilyParameterInfo> list = new();
            foreach (Parameter p in type.Parameters)
            {
                if (p?.Definition is null) continue;
                string value = ReadParameterValue(type.Document, p);
                if (string.IsNullOrEmpty(value) && !includeEmpty) continue;
                // The id disambiguates: a type can carry two parameters of one name (Kategorie ×2, or the
                // H/V layout pair Abstand/Layout/Innentyp…), and a name alone cannot say which is which.
                list.Add(new FamilyParameterInfo(p.Definition.Name, value, p.Id.Value,
                    ParameterUtils.IsBuiltInParameter(p.Id) ? ((BuiltInParameter)p.Id.Value).ToString() : null));
            }
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

                private static string ReadParameterValue(Document doc, Parameter p)
        {
            if (!p.HasValue) return string.Empty;
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? string.Empty;
                case StorageType.Integer: // incl. Yes/No — AsValueString renders "Yes"/"No"
                case StorageType.Double:  // formatted in the project's display units
                    return p.AsValueString() ?? string.Empty;
                case StorageType.ElementId:
                {
                    // AsValueString on ElementId params is one of the known crash paths — resolve the
                    // referenced element's name ourselves instead.
                    ElementId id = p.AsElementId();
                    if (id == ElementId.InvalidElementId) return string.Empty;
                    return doc.GetElement(id)?.Name ?? string.Empty;
                }
                default: // StorageType.None — nothing to read, and AsValueString here can crash Revit
                    return string.Empty;
            }
        }
    }

    public sealed record FamilyParameterInfo(
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("value")] string Value,
        [property: JsonProperty("id")] long Id = 0,
        [property: JsonProperty("builtInParameter", NullValueHandling = NullValueHandling.Ignore)] string? BuiltInParameter = null);

    public sealed record TypeParametersInfo(
        [property: JsonProperty("typeId")] long TypeId,
        [property: JsonProperty("parameters")] IReadOnlyList<FamilyParameterInfo> Parameters);

    public sealed record TypeParametersResult(
        [property: JsonProperty("types")] IReadOnlyList<TypeParametersInfo> Types);

    public sealed record WorksetInfo(
        [property: JsonProperty("id")] int Id,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("isOpen")] bool IsOpen,
        [property: JsonProperty("isEditable")] bool IsEditable,
        [property: JsonProperty("owner")] string Owner);

    public sealed record WorksetsResult(
        [property: JsonProperty("isWorkshared")] bool IsWorkshared,
        [property: JsonProperty("worksets")] IReadOnlyList<WorksetInfo> Worksets);
}
