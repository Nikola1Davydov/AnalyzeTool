using AnalyseTool.Tools.Actions;
using Autodesk.Revit.DB;
using BenchmarkDotNet.Attributes;
using Nice3point.BenchmarkDotNet.Revit;

namespace AnalyseTool.Benchmarks;

/// <summary>
/// SetDataToParameters: one batch of writes through ParameterWriteService, in one transaction.
///
/// The question: what does a batch cost, and how much of it is FINDING the parameter? A built-in parameter
/// id resolves directly (get_Parameter), while any other id — every shared and project parameter, i.e. most
/// of what users actually fill in — first walks all parameters of the element and then falls back to the
/// ParameterElement's definition. If BySharedParameter is far behind ByBuiltInParameter, Resolve is the thing to
/// fix (a definition cache per batch), not the writing. The batch size is the 500 the service's own comments
/// talk about.
///
/// Every invocation writes a value that differs from the last one, so Revit really changes the elements
/// instead of short-circuiting an unchanged value.
/// </summary>
[MemoryDiagnoser]
public class ParameterWriteBenchmarks : RevitApiBenchmark
{
    private const int WallCount = 2_000;
    private const int BatchSize = 500;

    private readonly ParameterWriteService _service = new();
    private BenchmarkModel _model = null!;
    private int _invocation;

    protected sealed override void OnGlobalSetup() => _model = BenchmarkModel.Create(Application, WallCount);

    protected sealed override void OnGlobalCleanup() => _model.Close();

    [Benchmark(Baseline = true)]
    public SetDataResult ByBuiltInParameter() =>
        _service.Write(_model.Document, Batch((long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS),
            ParameterWriteService.Mode.Overwrite);

    [Benchmark]
    public SetDataResult BySharedParameter() =>
        _service.Write(_model.Document, Batch(_model.SharedParameterId.Value), ParameterWriteService.Mode.Overwrite);

    private List<ParameterWriteService.Item?> Batch(long parameterId)
    {
        string value = $"Run {_invocation++}";
        List<ParameterWriteService.Item?> items = new(BatchSize);
        for (int i = 0; i < BatchSize; i++)
            items.Add(new ParameterWriteService.Item(_model.Walls[i].Id.Value, parameterId, value));
        return items;
    }
}
