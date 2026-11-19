using System.IO;
using System.Reflection;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Sdk.Testing;

namespace AnalyseTool.Extensions.Testing;

/// <summary>
///     The model test suites of the loaded extensions. <see cref="Loading.ExtensionHost" /> fills it whenever it loads an
///     extension: a saved file reloads the extension, and with it its tests.
/// </summary>
public sealed class ModelTestCatalog(ExtensionPaths paths)
{
    private readonly Dictionary<string, IReadOnlyList<SuiteDefinition>> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after the suites of an extension changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the suites, by extension and in the order declared.</summary>
    public IReadOnlyList<ModelTestSuiteInfo> GetSuites()
    {
        return Suites().Select(Describe).ToList();
    }

    /// <summary>Finds the suites of the extension assembly and records them for the extension.</summary>
    /// <param name="assembly">The loaded assembly of the extension.</param>
    /// <param name="descriptor">The extension.</param>
    /// <param name="problems">Receives what could not be declared.</param>
    /// <returns>How many suites it has.</returns>
    internal int Load(Assembly assembly, ExtensionDescriptor descriptor, ICollection<string> problems)
    {
        var suites = Discover(assembly, descriptor.Id, descriptor.Directory, BaselineDirectory(descriptor), problems);
        Set(descriptor.Id, suites);
        return suites.Count;
    }

    /// <summary>Records the suites of an extension; none removes it.</summary>
    internal void Set(string extensionId, IReadOnlyList<SuiteDefinition> suites)
    {
        bool changed;
        lock (_byExtension)
        {
            changed = suites.Count > 0 || _byExtension.ContainsKey(extensionId);
            if (suites.Count == 0)
            {
                _byExtension.Remove(extensionId);
            }
            else
            {
                _byExtension[extensionId] = suites;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Forgets the suites of an extension that left the pipeline.</summary>
    internal void Remove(string extensionId)
    {
        Set(extensionId, []);
    }

    internal IReadOnlyList<SuiteDefinition> Suites()
    {
        lock (_byExtension)
        {
            return _byExtension.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).SelectMany(pair => pair.Value).ToList();
        }
    }

    internal (SuiteDefinition Suite, TestDefinition Test)? FindTest(string testId)
    {
        foreach (var suite in Suites())
        {
            if (suite.Tests.FirstOrDefault(test => string.Equals(test.Id, testId, StringComparison.OrdinalIgnoreCase)) is { } test)
            {
                return (suite, test);
            }
        }

        return null;
    }

    /// <summary>Creates every suite class of an assembly and lets it declare its tests.</summary>
    internal static List<SuiteDefinition> Discover(Assembly assembly, string extensionId, string extensionDirectory, string baselineDirectory, ICollection<string> problems)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.OfType<Type>().ToArray();
        }

        var suites = new List<SuiteDefinition>();
        foreach (var type in types.Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModelTestSuite).IsAssignableFrom(type)))
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                problems.Add($"{type.FullName} needs a public parameterless constructor to declare its model tests.");
                continue;
            }

            var suite = new SuiteDefinition
            {
                Id = $"{extensionId}.{type.Name}",
                ExtensionId = extensionId,
                ClassName = type.Name,
                Directory = extensionDirectory,
                BaselineDirectory = baselineDirectory
            };
            var builder = new SuiteBuilder(suite, extensionDirectory);
            try
            {
                ((IModelTestSuite)Activator.CreateInstance(type)!).Define(builder);
            }
            catch (Exception exception)
            {
                var inner = exception is TargetInvocationException { InnerException: { } cause } ? cause : exception;
                problems.Add($"{type.FullName}.Define threw {inner.GetType().Name}: {inner.Message}");
                continue;
            }

            foreach (var problem in builder.Complete())
            {
                problems.Add(problem);
            }

            suites.Add(suite);
        }

        return suites;
    }

    internal static ModelTestSuiteInfo Describe(SuiteDefinition suite)
    {
        return new ModelTestSuiteInfo(suite.Id, suite.DisplayName, suite.ExtensionId, suite.Directory, suite.Tests.Select(Describe).ToList());
    }

    internal static ModelTestInfo Describe(TestDefinition test)
    {
        return new ModelTestInfo(test.Id, test.Name, test.Describe(), test.Kind.ToString(), test.Severity.ToString(),
            test.Traits.Select(trait => $"{trait.Key}={trait.Value}").Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            test.Slow, test.SkipReason, test.File, test.Line, test.FixLabel);
    }

    /// <summary>The user's own folder keeps the baseline (versioned with the tests); an installed package's goes to the profile.</summary>
    private string BaselineDirectory(ExtensionDescriptor descriptor)
    {
        return descriptor.Zone == ExtensionZone.Dev ? descriptor.Directory : Path.Combine(paths.ProfileRoot, "model-tests", descriptor.Id);
    }
}
