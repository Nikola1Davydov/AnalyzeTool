using AnalyseTool.Tools.Elements;
using BenchmarkDotNet.Attributes;
using Newtonsoft.Json;
using Nice3point.BenchmarkDotNet.Revit;

namespace AnalyseTool.Benchmarks;

/// <summary>
/// The category table in the Vue frontend (GetDataByCategoryName): every element of a category with EVERY
/// parameter, as DataElement/ParameterData, then serialized with Newtonsoft for the WebView2 message.
///
/// The question: how does the heaviest payload the platform builds scale with the size of a category, and
/// where does the time go — reading Revit (Collect) or turning it into JSON (CollectAndSerialize)? It is the
/// baseline any live grid over thousands of rows (the Univer spike) has to beat, and it tells whether a paged
/// or column-selected table is worth building before anyone builds it.
/// </summary>
[MemoryDiagnoser]
public class CategoryTableBenchmarks : RevitApiBenchmark
{
    private readonly DataElementsCollectorService _service = new();
    private BenchmarkModel _model = null!;
    private string _category = null!;

    [Params(1_000, 5_000)]
    public int WallCount { get; set; }

    protected sealed override void OnGlobalSetup()
    {
        _model = BenchmarkModel.Create(Application, WallCount);
        _category = _model.WallsCategoryName;
    }

    protected sealed override void OnGlobalCleanup() => _model.Close();

    [Benchmark(Baseline = true)]
    public List<DataElement> Collect() =>
        _service.GetAllElementsByCategory(_model.Document, _category).ToList();

    [Benchmark]
    public string CollectAndSerialize() =>
        JsonConvert.SerializeObject(_service.GetAllElementsByCategory(_model.Document, _category).ToList());
}
