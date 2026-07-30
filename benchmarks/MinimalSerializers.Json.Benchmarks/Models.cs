using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MinimalSerializers.Json;

namespace MinimalSerializers.Json.Benchmarks;

[DataContract]
public sealed class BenchItem
{
    [DataMember] public required int Id { get; init; }
    [DataMember] public required string Name { get; init; }
    [DataMember] public required decimal Price { get; init; }
}

[DataContract]
public sealed class BenchOrder
{
    [DataMember] public required Guid Id { get; init; }
    [DataMember] public required string Customer { get; init; }
    [DataMember] public required List<BenchItem> Items { get; init; }
    [DataMember] public required DateTimeOffset CreatedAt { get; init; }
}

[MinimalJsonSerializerContext]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class MinimalBenchContext : JsonSerializerContext;

[JsonSerializable(typeof(BenchOrder))]
[JsonSerializable(typeof(BenchOrder[]))]
[JsonSerializable(typeof(List<BenchOrder>))]
[JsonSerializable(typeof(BenchItem))]
[JsonSerializable(typeof(BenchItem[]))]
[JsonSerializable(typeof(List<BenchItem>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ManualBenchContext : JsonSerializerContext;
