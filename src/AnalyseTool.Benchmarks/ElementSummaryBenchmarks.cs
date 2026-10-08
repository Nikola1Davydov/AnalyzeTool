using AnalyseTool.Tools.Elements;
using Autodesk.Revit.DB;
using BenchmarkDotNet.Attributes;
using Nice3point.BenchmarkDotNet.Revit;

namespace AnalyseTool.Benchmarks;

/// <summary>
/// GetElements, the AI/MCP element listing (DataElementsCollectorService.GetElementSummaries).
///
/// The question: does <c>limit</c> bound the WORK, as the service's comments and the command's description
/// promise? The query is lazy on purpose so an unfiltered page describes only <c>limit</c> elements, while a
/// name filter has to describe the whole category to know what matches. If that holds, Limit50 stays flat
/// from 1 000 to 5 000 walls while All and FilteredLimit50 grow with the category. If Limit50 grows as well,
/// the cost is elsewhere — most likely the ToElements() that materializes the full category up front — and
/// that is the next thing to change.
/// </summary>
[MemoryDiagnoser]
public class ElementSummaryBenchmarks : RevitApiBenchmark
{
    private readonly DataElementsCollectorService _service = new();
    private BenchmarkModel _model = null!;
    private ElementQuery _all = null!;
    private ElementQuery _limit50 = null!;
    private ElementQuery _filteredLimit50 = null!;
    private ElementQuery _parametersLimit50 = null!;

    [Params(1_000, 5_000)]
    public int WallCount { get; set; }

    protected sealed override void OnGlobalSetup()
    {
        _model = BenchmarkModel.Create(Application, WallCount);

        const string walls = nameof(BuiltInCategory.OST_Walls);
        _all = new ElementQuery { BuiltInCategory = walls };
        _limit50 = new ElementQuery { BuiltInCategory = walls, Limit = 50 };
        // "Benchmark wall" is in the name of 9 of the 10 wall types, so the filter matches most walls and
        // the page fills early — the cost measured is describing the category, not scanning for rare hits.
        _filteredLimit50 = new ElementQuery { BuiltInCategory = walls, TypeNameContains = "Benchmark wall", Limit = 50 };
        _parametersLimit50 = new ElementQuery
        {
            BuiltInCategory = walls,
            Limit = 50,
            ParameterNames =
            [
                LabelUtils.GetLabelFor(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS),
                LabelUtils.GetLabelFor(BuiltInParameter.ALL_MODEL_MARK),
                BenchmarkModel.SharedParameterName,
            ],
        };
    }

    protected sealed override void OnGlobalCleanup() => _model.Close();

    [Benchmark(Baseline = true)]
    public ElementsResult All() => _service.GetElementSummaries(_model.Document, _all);

    [Benchmark]
    public ElementsResult Limit50() => _service.GetElementSummaries(_model.Document, _limit50);

    [Benchmark]
    public ElementsResult FilteredLimit50() => _service.GetElementSummaries(_model.Document, _filteredLimit50);

    [Benchmark]
    public ElementsResult WithParametersLimit50() => _service.GetElementSummaries(_model.Document, _parametersLimit50);
}
