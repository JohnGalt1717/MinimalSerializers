using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace MinimalSerializers.Json.Benchmarks;

/// <summary>
/// Compares three STJ paths on the same payload:
/// 1. Reflection — stock <see cref="DefaultJsonTypeInfoResolver"/> (no source-gen context).
/// 2. Manual STJ — complete hand-written <c>[JsonSerializable]</c> context.
/// 3. Minimal STJ — context roots emitted by MinimalSerializers; serializers still from stock STJ SG.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Method)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class SerializationBenchmarks
{
    private BenchOrder _order = null!;
    private string _json = null!;
    private JsonSerializerOptions _reflection = null!;
    private JsonSerializerOptions _manual = null!;
    private JsonSerializerOptions _minimal = null!;

    [GlobalSetup]
    public void Setup()
    {
        _order = new BenchOrder
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Customer = "Customer",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Items =
            [
                new BenchItem
                {
                    Id = 1,
                    Name = "A",
                    Price = 1.5m,
                },
                new BenchItem
                {
                    Id = 2,
                    Name = "B",
                    Price = 2.5m,
                },
                new BenchItem
                {
                    Id = 3,
                    Name = "C",
                    Price = 3.5m,
                },
            ],
        };

        // Path without MinimalSerializers / without any JsonSerializerContext:
        // plain web defaults + reflection type info.
        _reflection = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        // Hand-maintained complete [JsonSerializable] list (the pain this package removes).
        _manual = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = ManualBenchContext.Default,
        };

        // MinimalSerializers-discovered roots → same STJ source generator as manual.
        _minimal = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = MinimalBenchContext.Default,
        };

        // Warm each path once so first-use reflection metadata cost is not in the timed loop.
        _ = JsonSerializer.Serialize(_order, _reflection);
        _ = JsonSerializer.Serialize(_order, _manual);
        _json = JsonSerializer.Serialize(_order, _minimal);
        _ = JsonSerializer.Deserialize<BenchOrder>(_json, _reflection);
        _ = JsonSerializer.Deserialize<BenchOrder>(_json, _manual);
        _ = JsonSerializer.Deserialize<BenchOrder>(_json, _minimal);
    }

    // --- Serialize ---

    [Benchmark(Baseline = true)]
    public string Reflection_Serialize() => JsonSerializer.Serialize(_order, _reflection);

    [Benchmark]
    public string ManualStj_Serialize() => JsonSerializer.Serialize(_order, _manual);

    [Benchmark]
    public string MinimalStj_Serialize() => JsonSerializer.Serialize(_order, _minimal);

    // --- Deserialize ---

    [Benchmark]
    public BenchOrder? Reflection_Deserialize() =>
        JsonSerializer.Deserialize<BenchOrder>(_json, _reflection);

    [Benchmark]
    public BenchOrder? ManualStj_Deserialize() =>
        JsonSerializer.Deserialize<BenchOrder>(_json, _manual);

    [Benchmark]
    public BenchOrder? MinimalStj_Deserialize() =>
        JsonSerializer.Deserialize<BenchOrder>(_json, _minimal);
}
