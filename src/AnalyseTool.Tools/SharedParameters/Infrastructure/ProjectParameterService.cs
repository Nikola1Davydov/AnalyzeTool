using AnalyseTool.Tools.Shared;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.SharedParameters
{
    /// <summary>
    /// The project side of shared parameters: which ones the document has and where they are bound, and
    /// binding parameters from the shared parameter file to categories. Functions of a Document (and the
    /// Application, for the shared parameter file), so they are testable inside Revit without a UI.
    /// </summary>
    internal sealed class ProjectParameterService
    {
        public ProjectParametersResult List(Document doc)
        {
            List<ProjectParameterDto> result = new();
            HashSet<long> bound = new();

            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                if (it.Key is not InternalDefinition def || it.Current is not ElementBinding binding) continue;
                SharedParameterElement? shared = doc.GetElement(def.Id) as SharedParameterElement;
                bound.Add(def.Id.Value);
                result.Add(new ProjectParameterDto
                {
                    Id = def.Id.Value,
                    Guid = shared?.GuidValue.ToString("D"),
                    Name = def.Name,
                    IsShared = shared is not null,
                    Bound = true,
                    IsInstance = binding is InstanceBinding,
                    DataType = SpecLabel(def),
                    Group = GroupLabel(def),
                    Categories = binding.Categories.Cast<Category>().Select(c => c.Name).OrderBy(n => n).ToList(),
                });
            }

            // Shared parameters the project only knows through its loaded families: in the model, but
            // bound to nothing.
            foreach (SharedParameterElement shared in new FilteredElementCollector(doc)
                         .OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
            {
                if (bound.Contains(shared.Id.Value)) continue;
                InternalDefinition def = shared.GetDefinition();
                result.Add(new ProjectParameterDto
                {
                    Id = shared.Id.Value,
                    Guid = shared.GuidValue.ToString("D"),
                    Name = shared.Name,
                    IsShared = true,
                    Bound = false,
                    DataType = SpecLabel(def),
                    Group = GroupLabel(def),
                });
            }

            return new ProjectParametersResult { Parameters = result.OrderBy(p => p.Name).ToList() };
        }

        public BindingOptionsResult BindingOptions(Document doc)
        {
            List<BindableCategoryDto> categories = new();
            foreach (Category c in doc.Settings.Categories)
            {
                if (!c.AllowsBoundParameters) continue;
                if (c.CategoryType != CategoryType.Model && c.CategoryType != CategoryType.Annotation) continue;
                categories.Add(new BindableCategoryDto { Id = c.Id.Value, Name = c.Name, Type = c.CategoryType.ToString() });
            }

            List<ParameterGroupOptionDto> groups = new();
            foreach (ForgeTypeId group in ParameterUtils.GetAllBuiltInGroups())
            {
                string label;
                try { label = LabelUtils.GetLabelForGroup(group); }
                catch (Exception) { continue; } // a group with no label is not one a person can pick
                if (!string.IsNullOrWhiteSpace(label)) groups.Add(new ParameterGroupOptionDto { TypeId = group.TypeId, Label = label });
            }

            return new BindingOptionsResult
            {
                Categories = categories.OrderBy(c => c.Name).ToList(),
                Groups = groups.OrderBy(g => g.Label).ToList(),
                DefaultGroup = GroupTypeId.Data.TypeId,
            };
        }

        /// <summary>
        /// Binds parameters of Revit's current shared parameter file to the given categories. A parameter
        /// the project already has keeps its categories and gains the new ones — adding "Doors" must not
        /// quietly take it off "Walls".
        /// </summary>
        public BindResult Bind(Application app, Document doc, IReadOnlyCollection<Guid> guids,
            IReadOnlyCollection<long> categoryIds, bool isInstance, string? groupTypeId)
        {
            BindResult result = new();

            DefinitionFile? file = app.OpenSharedParameterFile();
            if (file is null)
                throw new InvalidOperationException("Revit has no shared parameter file set, or it cannot be read.");

            Dictionary<Guid, ExternalDefinition> definitions = new();
            foreach (DefinitionGroup group in file.Groups)
                foreach (Definition d in group.Definitions)
                    if (d is ExternalDefinition ext) definitions[ext.GUID] = ext;

            List<Category> categories = categoryIds
                .Select(id => Category.GetCategory(doc, new ElementId(id)))
                .Where(c => c is not null && c.AllowsBoundParameters)
                .ToList()!;
            if (categories.Count == 0) throw new InvalidOperationException("Pick at least one category that accepts parameters.");

            ForgeTypeId paletteGroup = string.IsNullOrWhiteSpace(groupTypeId) ? GroupTypeId.Data : new ForgeTypeId(groupTypeId);

            using Transaction transaction = new(doc, "Add shared parameters");
            transaction.Start();
            CollectingFailuresPreprocessor.Apply(transaction);

            foreach (Guid guid in guids)
            {
                if (!definitions.TryGetValue(guid, out ExternalDefinition? definition))
                {
                    result.Problems.Add(new BindProblemDto { Name = guid.ToString("D"), Reason = "Not in Revit's current shared parameter file." });
                    continue;
                }

                try
                {
                    CategorySet set = app.Create.NewCategorySet();
                    foreach (Category c in categories) set.Insert(c);

                    SharedParameterElement? existing = SharedParameterElement.Lookup(doc, guid);
                    InternalDefinition? internalDef = existing?.GetDefinition();
                    ElementBinding? current = internalDef is null ? null : doc.ParameterBindings.get_Item(internalDef) as ElementBinding;
                    if (current is not null)
                        foreach (Category c in current.Categories) set.Insert(c);

                    ElementBinding binding = isInstance ? app.Create.NewInstanceBinding(set) : app.Create.NewTypeBinding(set);
                    bool ok = current is null
                        ? doc.ParameterBindings.Insert(definition, binding, paletteGroup)
                        : doc.ParameterBindings.ReInsert(internalDef!, binding, paletteGroup);
                    if (!ok)
                        result.Problems.Add(new BindProblemDto { Name = definition.Name, Reason = "Revit refused the binding." });
                    else if (current is null) result.Added.Add(definition.Name);
                    else result.Updated.Add(definition.Name);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
                {
                    result.Problems.Add(new BindProblemDto { Name = definition.Name, Reason = ex.Message });
                }
            }

            if (result.Added.Count + result.Updated.Count > 0) transaction.Commit();
            else transaction.RollBack();
            return result;
        }

        internal static string SpecLabel(Definition def)
        {
            try
            {
                ForgeTypeId spec = def.GetDataType();
                return spec is null || string.IsNullOrEmpty(spec.TypeId) ? string.Empty : LabelUtils.GetLabelForSpec(spec);
            }
            catch (Exception) { return string.Empty; } // a spec Revit has no label for — show nothing rather than fail the list
        }

        private static string GroupLabel(Definition def)
        {
            try
            {
                ForgeTypeId group = def.GetGroupTypeId();
                return group is null || string.IsNullOrEmpty(group.TypeId) ? string.Empty : LabelUtils.GetLabelForGroup(group);
            }
            catch (Exception) { return string.Empty; }
        }
    }
}
