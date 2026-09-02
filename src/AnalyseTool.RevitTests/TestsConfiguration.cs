using Nice3point.TUnit.Revit.Executors;
using TUnit.Core.Executors;

// Every test body runs on the thread that initialized Revit; hooks that touch Revit carry
// [HookExecutor<RevitThreadExecutor>] themselves.
[assembly: TestExecutor<RevitThreadExecutor>]
