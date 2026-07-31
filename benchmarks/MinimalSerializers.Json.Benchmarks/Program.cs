using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using MinimalSerializers.Json.Benchmarks;

// BenchmarkDotNet 0.15.x does not recognize the .NET 11 runtime moniker for
// out-of-process toolchains. InProcessEmit runs in the host process instead.
var config = ManualConfig
    .CreateEmpty()
    .AddLogger(ConsoleLogger.Default)
    .AddExporter(MarkdownExporter.GitHub)
    .AddColumnProvider(DefaultColumnProviders.Instance)
    .AddJob(
        Job.Default.WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(1)
            .WithIterationCount(8)
            .WithInvocationCount(4096)
            .WithUnrollFactor(16)
    )
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

BenchmarkRunner.Run<SerializationBenchmarks>(config);
