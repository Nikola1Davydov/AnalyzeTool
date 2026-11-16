using AnalyseTool.Hosting.Commands;
using AnalyseTool.Services.Parameters;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Services.SharedParameters;

/// <summary>
///     The project side of shared parameters: which ones the document has and where they are bound, and binding parameters
///     of the shared parameter file to categories. Functions of a document, testable in Revit without a UI; call them on the
///     Revit thread.
/// </summary>
public sealed class ProjectParameterService(ILogger<ProjectParameterService>? logger = null)
{
    /// <summary>The bound parameters and the shared parameters only loaded families carry.</summary>
    public ProjectParametersResult List(Document document)
    {
        var result = new List<ProjectParameterDto>();
        var bound = new HashSet<long>();

        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || iterator.Current is not ElementBinding binding)
            {
                continue;
            }

            var shared = document.GetElement(definition.Id) as SharedParameterElement;
            bound.Add(definition.Id.Value);
            result.Add(new ProjectParameterDto
            {
                Id = definition.Id.Value,
                Guid = shared?.GuidValue.ToString("D"),
                Name = definition.Name,
                IsShared = shared is not null,
                Bound = true,
                IsInstance = binding is InstanceBinding,
                DataType = SpecLabel(definition),
                Group = GroupLabel(definition),
                Categories = binding.Categories.Cast<Category>().Select(category => category.Name).OrderBy(name => name).ToList()
            });
        }

        // Shared parameters the project knows only through its loaded families: in the model, bound to nothing
        foreach (var shared in new FilteredElementCollector(document).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
        {
            if (bound.Contains(shared.Id.Value))
            {
                continue;
            }

            var definition = shared.GetDefinition();
            result.Add(new ProjectParameterDto
            {
                Id = shared.Id.Value,
                Guid = shared.GuidValue.ToString("D"),
                Name = shared.Name,
                IsShared = true,
                Bound = false,
                DataType = SpecLabel(definition),
                Group = GroupLabel(definition)
            });
        }

        return new ProjectParametersResult(result.OrderBy(parameter => parameter.Name).ToList());
    }

    /// <summary>The categories that accept bound parameters and the properties-palette groups.</summary>
    public BindingOptionsResult BindingOptions(Document document)
    {
        var categories = new List<BindableCategoryDto>();
        foreach (Category category in document.Settings.Categories)
        {
            if (!category.AllowsBoundParameters || category.CategoryType is not (CategoryType.Model or CategoryType.Annotation))
            {
                continue;
            }

            categories.Add(new BindableCategoryDto(category.Id.Value, category.Name, category.CategoryType.ToString()));
        }

        var groups = new List<ParameterGroupOptionDto>();
        foreach (var group in ParameterUtils.GetAllBuiltInGroups())
        {
            string label;
            try
            {
                label = LabelUtils.GetLabelForGroup(group);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                continue; // a group without a label is not one a person can pick
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                groups.Add(new ParameterGroupOptionDto(group.TypeId, label));
            }
        }

        return new BindingOptionsResult(
            categories.OrderBy(category => category.Name).ToList(),
            groups.OrderBy(group => group.Label).ToList(),
            GroupTypeId.Data.TypeId);
    }

    /// <summary>
    ///     Binds parameters of Revit's current shared parameter file to the categories, in one transaction. A parameter the
    ///     project already has keeps its categories and gains the new ones — adding «Doors» must not take it off «Walls».
    /// </summary>
    /// <exception cref="CommandException">Revit has no readable shared parameter file, or no category accepts parameters.</exception>
    public BindResult Bind(
        Document document,
        IReadOnlyCollection<Guid> guids,
        IReadOnlyCollection<long> categoryIds,
        bool isInstance,
        string? groupTypeId)
    {
        var application = document.Application;
        var added = new List<string>();
        var updated = new List<string>();
        var problems = new List<BindProblemDto>();

        var file = application.OpenSharedParameterFile()
                   ?? throw new CommandException(SharedParameterErrorCodes.NoSharedParameterFile,
                       "Revit has no shared parameter file set, or it cannot be read.");

        var definitions = new Dictionary<Guid, ExternalDefinition>();
        foreach (DefinitionGroup group in file.Groups)
        {
            foreach (var definition in group.Definitions)
            {
                if (definition is ExternalDefinition external)
                {
                    definitions[external.GUID] = external;
                }
            }
        }

        var categories = categoryIds
            .Select(id => Category.GetCategory(document, new ElementId(id)))
            .Where(category => category is not null && category.AllowsBoundParameters)
            .ToList();
        if (categories.Count == 0)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "Pick at least one category that accepts parameters.");
        }

        var paletteGroup = string.IsNullOrWhiteSpace(groupTypeId) ? GroupTypeId.Data : new ForgeTypeId(groupTypeId);

        using var transaction = new Transaction(document, "Add shared parameters");
        transaction.Start();
        var failures = CollectingFailuresPreprocessor.Apply(transaction, logger);

        foreach (var guid in guids)
        {
            if (!definitions.TryGetValue(guid, out var definition))
            {
                problems.Add(new BindProblemDto(guid.ToString("D"), "Not in Revit's current shared parameter file."));
                continue;
            }

            try
            {
                var set = application.Create.NewCategorySet();
                foreach (var category in categories)
                {
                    set.Insert(category);
                }

                var internalDefinition = SharedParameterElement.Lookup(document, guid)?.GetDefinition();
                var current = internalDefinition is null ? null : document.ParameterBindings.get_Item(internalDefinition) as ElementBinding;
                if (current is not null)
                {
                    foreach (Category category in current.Categories)
                    {
                        set.Insert(category);
                    }
                }

                ElementBinding binding = isInstance ? application.Create.NewInstanceBinding(set) : application.Create.NewTypeBinding(set);
                var ok = current is null
                    ? document.ParameterBindings.Insert(definition, binding, paletteGroup)
                    : document.ParameterBindings.ReInsert(internalDefinition!, binding, paletteGroup);

                if (!ok)
                {
                    problems.Add(new BindProblemDto(definition.Name, "Revit refused the binding."));
                }
                else if (current is null)
                {
                    added.Add(definition.Name);
                }
                else
                {
                    updated.Add(definition.Name);
                }
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
            {
                problems.Add(new BindProblemDto(definition.Name, exception.Message));
            }
        }

        if (added.Count + updated.Count == 0)
        {
            transaction.RollBack();
            return new BindResult(added, updated, problems);
        }

        var rolledBack = failures.RollbackReason(transaction.Commit());
        if (rolledBack is not null)
        {
            problems.AddRange(added.Concat(updated).Select(name => new BindProblemDto(name, rolledBack)));
            added.Clear();
            updated.Clear();
        }

        return new BindResult(added, updated, problems);
    }

    /// <summary>The spec of a definition as Revit labels it; empty when Revit has no label for it.</summary>
    internal static string SpecLabel(Definition definition)
    {
        try
        {
            var spec = definition.GetDataType();
            return spec is null || string.IsNullOrEmpty(spec.TypeId) ? string.Empty : LabelUtils.GetLabelForSpec(spec);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static string GroupLabel(Definition definition)
    {
        try
        {
            var group = definition.GetGroupTypeId();
            return group is null || string.IsNullOrEmpty(group.TypeId) ? string.Empty : LabelUtils.GetLabelForGroup(group);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
