using MinimalSerializers.Json.Discovery;
using MinimalSerializers.Json.Tests.Infrastructure;

namespace MinimalSerializers.Json.Tests.Discovery;

public sealed class EmitterSnapshotTests
{
    [Fact]
    public Task KitchenSink_emit_snapshot()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;

            namespace Sample;

            public enum Color { Red, Green }

            [DataContract]
            public sealed class Leaf
            {
                [DataMember] public required int Value { get; init; }
            }

            [DataContract]
            public sealed class Root
            {
                [DataMember] public required Leaf Leaf { get; init; }
                [DataMember] public required List<Leaf> Leaves { get; init; }
                [DataMember] public required Color Color { get; init; }
                [DataMember] public required Guid Id { get; init; }
            }

            [MinimalJsonSerializerContext]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var compilation = CompilationHelper.Create(source);
        var result = JsonSerializableRootCollector.Collect(compilation);
        var emit = MinimalJsonContextSourceEmitter.Emit(result.Contexts[0]);
        return Verify(emit);
    }
}
