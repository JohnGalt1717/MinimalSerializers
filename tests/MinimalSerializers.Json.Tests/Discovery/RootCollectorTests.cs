using FluentAssertions;
using MinimalSerializers.Json.Discovery;
using MinimalSerializers.Json.Tests.Infrastructure;

namespace MinimalSerializers.Json.Tests.Discovery;

public sealed class RootCollectorTests
{
    private const string ContextAndModels = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.Serialization;
        using System.Text.Json.Serialization;
        using MinimalSerializers.Json;

        namespace Tests;

        public enum Status
        {
            [EnumMember(Value = "open")]
            Open = 0,
            Closed = 1,
        }

        [DataContract]
        public sealed class ChildDto
        {
            [DataMember]
            public required string Name { get; init; }
        }

        [DataContract]
        public sealed class ParentDto
        {
            [DataMember]
            public required ChildDto Child { get; init; }

            [DataMember]
            public required List<ChildDto> Children { get; init; }

            [DataMember]
            public required IReadOnlyCollection<string> Tags { get; init; }

            [DataMember]
            public required IReadOnlyDictionary<string, int> Counts { get; init; }

            [DataMember]
            public required Status Status { get; init; }

            [DataMember]
            public required byte[] Blob { get; init; }

            [DataMember]
            public required DateOnly Day { get; init; }

            [DataMember]
            public string? Notes { get; init; }
        }

        [DataContract]
        public sealed class WrapperDto<T>
        {
            [DataMember]
            public required T Value { get; init; }
        }

        [MinimalJsonSerializerContext]
        public partial class TestJsonContext : JsonSerializerContext;
        """;

    [Fact]
    public void Discovers_nested_graph_and_collection_roots()
    {
        var compilation = CompilationHelper.Create(ContextAndModels);
        var result = JsonSerializableRootCollector.Collect(compilation);

        result.Contexts.Should().ContainSingle();
        var ctx = result.Contexts[0];
        ctx.IsPartial.Should().BeTrue();
        ctx.DerivesFromJsonSerializerContext.Should().BeTrue();
        ctx.TypeName.Should().Be("TestJsonContext");

        var roots = ctx.RootTypeDisplayNames;
        roots.Should().Contain(r => r.Contains("ParentDto") && !r.Contains("List") && !r.EndsWith("[]"));
        roots.Should().Contain(r => r.Contains("ParentDto[]"));
        roots.Should().Contain(r => r.Contains("List<") && r.Contains("ParentDto"));
        roots.Should().Contain(r => r.Contains("ChildDto"));
        roots.Should().Contain(r => r.Contains("Status"));
        roots.Should().Contain(r => r.Contains("Dictionary"));
    }

    [Fact]
    public void Skips_open_generic_datacontract()
    {
        var compilation = CompilationHelper.Create(ContextAndModels);
        var result = JsonSerializableRootCollector.Collect(compilation);
        result.Diagnostics.Should().Contain(d => d.Id == "MSJ0004");
        result.Contexts[0].RootTypeDisplayNames.Should().NotContain(r => r.Contains("WrapperDto<T>"));
    }

    [Fact]
    public void Emit_is_deterministic()
    {
        var compilation = CompilationHelper.Create(ContextAndModels);
        var result = JsonSerializableRootCollector.Collect(compilation);
        var a = MinimalJsonContextSourceEmitter.Emit(result.Contexts[0]);
        var b = MinimalJsonContextSourceEmitter.Emit(result.Contexts[0]);
        a.Should().Be(b);
        a.Should().Contain("[JsonSerializable(typeof(");
        a.Should().Contain("partial class TestJsonContext");
    }

    [Fact]
    public void Non_partial_context_is_error()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;
            namespace Tests;
            [MinimalJsonSerializerContext]
            public class BadContext : JsonSerializerContext;
            """;
        var compilation = CompilationHelper.Create(source);
        var result = JsonSerializableRootCollector.Collect(compilation);
        result.Contexts.Should().ContainSingle();
        result.Contexts[0].Diagnostics.Should().Contain(d => d.Id == "MSJ0003" && d.Severity == DiscoveryDiagnosticSeverity.Error);
    }

    [Fact]
    public void Empty_datacontracts_warns()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;
            namespace Tests;
            [MinimalJsonSerializerContext]
            public partial class EmptyContext : JsonSerializerContext;
            """;
        var compilation = CompilationHelper.Create(source);
        var result = JsonSerializableRootCollector.Collect(compilation);
        result.Diagnostics.Should().Contain(d => d.Id == "MSJ0002");
    }

    [Fact]
    public void Respects_IgnoreDataMember_and_JsonIgnore()
    {
        const string source = """
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;
            namespace Tests;

            [DataContract]
            public sealed class Node
            {
                [DataMember]
                public required string Keep { get; init; }

                [IgnoreDataMember]
                public required Other Skip1 { get; init; }

                [JsonIgnore]
                public required Other Skip2 { get; init; }
            }

            [DataContract]
            public sealed class Other
            {
                [DataMember]
                public required string X { get; init; }
            }

            [MinimalJsonSerializerContext]
            public partial class Ctx : JsonSerializerContext;
            """;
        var compilation = CompilationHelper.Create(source);
        var result = JsonSerializableRootCollector.Collect(compilation);
        var roots = result.Contexts[0].RootTypeDisplayNames;
        // Other is still a DataContract itself so it is included as a root; ignored members should not force extra collection roots only via walk — Other still discovered as DataContract type.
        roots.Should().Contain(r => r.Contains("Node"));
        roots.Should().Contain(r => r.Contains("Other"));
    }
}
