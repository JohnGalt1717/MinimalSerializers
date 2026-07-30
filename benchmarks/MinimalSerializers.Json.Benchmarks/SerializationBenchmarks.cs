using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;

namespace MinimalSerializers.Json.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
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
                new BenchItem { Id = 1, Name = "A", Price = 1.5m },
                new BenchItem { Id = 2, Name = "B", Price = 2.5m },
                new BenchItem { Id = 3, Name = "C", Price = 3.5m },
            ],
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
        _json = JsonSerializer.Serialize(_order, _manual);
    }

    [Benchmark(Baseline = true)]
    public string Reflection_Serialize() => JsonSerializer.Serialize(_order, _reflection);

    [Benchmark]
    public string ManualStj_Serialize() => JsonSerializer.Serialize(_order, _manual);

    [Benchmark]
    public string MinimalStj_Serialize() => JsonSerializer.Serialize(_order, _minimal);

    [Benchmark]
    public BenchOrder? Reflection_Deserialize() => JsonSerializer.Deserialize<BenchOrder>(_json, _reflection);

    [Benchmark]
    public BenchOrder? ManualStj_Deserialize() => JsonSerializer.Deserialize<BenchOrder>(_json, _manual);

    [Benchmark]
    public BenchOrder? MinimalStj_Deserialize() => JsonSerializer.Deserialize<BenchOrder>(_json, _minimal);
}
