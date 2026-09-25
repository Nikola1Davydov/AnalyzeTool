using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Nice3point.BenchmarkDotNet.Revit;

// WithCurrentConfiguration: BenchmarkDotNet rebuilds this project for every benchmark process, and the
// Revit year lives in the configuration name (Release.R25 …) — without it the child build falls back to
// plain "Release" and has no Revit year at all.
//
// Optimize=true is passed down explicitly because the referenced plugin projects get the SAME
// configuration name, and "Release.R25" is not the literal "Release" the .NET SDK switches the optimizer
// on for: without it Tools would be measured as a Debug build.
IConfig configuration = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default
        .WithCurrentConfiguration()
        .WithArguments([new MsBuildArgument("/p:Optimize=true")]));

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, configuration);
