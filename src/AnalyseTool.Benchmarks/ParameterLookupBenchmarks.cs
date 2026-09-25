using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using BenchmarkDotNet.Attributes;
using Nice3point.BenchmarkDotNet.Revit;

namespace AnalyseTool.Benchmarks;

/// <summary>
/// How GetElements reads the parameters a caller asked for by name.
///
/// The question: DataElementsCollectorService.ExtractParameters walks EVERY parameter of every element
/// (Element.Parameters, a few hundred on a wall) to find the two or three that were requested. The
/// alternative is one Element.LookupParameter per requested name. Which is cheaper, and does the answer flip
/// with the number of names requested? The answer decides whether ExtractParameters changes.
///
/// ScanAllParameters is a copy of ExtractParameters (it is private); keep the two in step. Both variants
/// take the FIRST parameter of a given name, as ExtractParameters does, so they return the same values.
/// </summary>
[MemoryDiagnoser]
public class ParameterLookupBenchmarks : RevitApiBenchmark
{
    private const int WallCount = 2_000;

    private BenchmarkModel _model = null!;
    private string[] _names = null!;
    private HashSet<string> _wanted = null!;

    [Params(1, 3)]
    public int RequestedNames { get; set; }

    protected sealed override void OnGlobalSetup()
    {
        _model = BenchmarkModel.Create(Application, WallCount);
        string[] candidates =
        [
            LabelUtils.GetLabelFor(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS),
            LabelUtils.GetLabelFor(BuiltInParameter.ALL_MODEL_MARK),
            BenchmarkModel.SharedParameterName,
        ];
        _names = candidates.Take(RequestedNames).ToArray();
        _wanted = new HashSet<string>(_names, StringComparer.OrdinalIgnoreCase);
    }

    protected sealed override void OnGlobalCleanup() => _model.Close();

    [Benchmark(Baseline = true)]
    public int ScanAllParameters()
    {
        int found = 0;
        foreach (Wall wall in _model.Walls)
        {
            Dictionary<string, string> values = new();
            foreach (Parameter p in wall.Parameters)
            {
                string name = p.Definition?.Name ?? string.Empty;
                if (name.Length == 0 || values.ContainsKey(name) || !_wanted.Contains(name)) continue;
                try { values[name] = p.GetParameterValue() ?? string.Empty; }
                catch { values[name] = string.Empty; }
            }
            found += values.Count;
        }
        return found;
    }

    [Benchmark]
    public int LookupByName()
    {
        int found = 0;
        foreach (Wall wall in _model.Walls)
        {
            Dictionary<string, string> values = new();
            foreach (string name in _names)
            {
                Parameter? p = wall.LookupParameter(name);
                if (p == null) continue;
                try { values[name] = p.GetParameterValue() ?? string.Empty; }
                catch { values[name] = string.Empty; }
            }
            found += values.Count;
        }
        return found;
    }
}
