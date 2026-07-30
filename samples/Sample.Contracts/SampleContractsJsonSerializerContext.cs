using System.Text.Json.Serialization;
using MinimalSerializers.Json;

namespace Sample.Contracts;

[MinimalJsonSerializerContext]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class SampleContractsJsonSerializerContext : JsonSerializerContext;
