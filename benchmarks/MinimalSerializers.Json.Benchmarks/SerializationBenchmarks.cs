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
///
/// Also compares options-based deserialize vs the typed JsonTypeInfo overload
/// (Context.Default.T), which is the fastest source-gen entry point.
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
    private JsonTypeInfo<BenchOrder> _manualTypeInfo = null!;
    private JsonTypeInfo<BenchOrder> _minimalTypeInfo = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Larger than a toy DTO so parse+graph work is meaningful, but still a
        // realistic order-shaped payload (not pathological).
        var items = new List<BenchItem>(64);
        for (var i = 0; i < 64; i++)
        {
            items.Add(
                new BenchItem
                {
                    Id = i,
                    Name = $"Item-{i:D3}",
                    Price = 1.5m + i,
                }
            );
        }

        _order = new BenchOrder
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Customer = "Customer",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Items = items,
        };

        _reflection = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        _manual = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = ManualBenchContext.Default,
        };

        _minimal = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = MinimalBenchContext.Default,
        };

        _manualTypeInfo = ManualBenchContext.Default.BenchOrder;
        _minimalTypeInfo = MinimalBenchContext.Default.BenchOrder;

        // Warm each path once so first-use reflection metadata cost is not timed.
        _ = JsonSerializer.Serialize(_order, _reflection);
        _ = JsonSerializer.Serialize(_order, _manual);
        _json = JsonSerializer.Serialize(_order, _minimalTypeInfo);
        _ = JsonSerializer.Deserialize<BenchOrder>(_json, _reflection);
        _ = JsonSerializer.Deserialize(_json, _manualTypeInfo);
        _ = JsonSerializer.Deserialize(_json, _minimalTypeInfo);
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

    [Benchmark]
    public string ManualStj_Serialize_TypeInfo() =>
        JsonSerializer.Serialize(_order, _manualTypeInfo);

    [Benchmark]
    public string MinimalStj_Serialize_TypeInfo() =>
        JsonSerializer.Serialize(_order, _minimalTypeInfo);

    // --- Deserialize (options / resolver chain; fair vs reflection) ---

    [Benchmark]
    public BenchOrder? Reflection_Deserialize() =>
        JsonSerializer.Deserialize<BenchOrder>(_json, _reflection);

    [Benchmark]
    public BenchOrder? ManualStj_Deserialize() =>
        JsonSerializer.Deserialize<BenchOrder>(_json, _manual);

    [Benchmark]
    public BenchOrder? MinimalStj_Deserialize() =>
        JsonSerializer.Deserialize<BenchOrder>(_json, _minimal);

    // --- Deserialize (typed JsonTypeInfo; preferred source-gen API) ---

    [Benchmark]
    public BenchOrder? ManualStj_Deserialize_TypeInfo() =>
        JsonSerializer.Deserialize(_json, _manualTypeInfo);

    [Benchmark]
    public BenchOrder? MinimalStj_Deserialize_TypeInfo() =>
        JsonSerializer.Deserialize(_json, _minimalTypeInfo);
}
