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

        // Collection wrappers get explicit TypeInfoPropertyName.
        ctx.Roots.Should()
            .Contain(r =>
                r.TypeDisplayName.Contains("List<")
                && r.TypeDisplayName.Contains("ParentDto")
                && r.TypeInfoPropertyName != null
                && r.TypeInfoPropertyName.StartsWith("ListOf_", StringComparison.Ordinal)
            );
        ctx.Roots.Should()
            .Contain(r =>
                r.TypeDisplayName.EndsWith("ParentDto[]", StringComparison.Ordinal)
                && r.TypeInfoPropertyName != null
                && r.TypeInfoPropertyName.StartsWith("ArrayOf_", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void Skips_open_generic_datacontract_with_summary_warning_by_default()
    {
        var compilation = CompilationHelper.Create(ContextAndModels);
        var result = JsonSerializableRootCollector.Collect(compilation);
        var msj0004 = result.Diagnostics.Where(d => d.Id == "MSJ0004").ToList();
        msj0004.Should().ContainSingle();
        msj0004[0].Message.Should().Contain("Skipped");
        msj0004[0].Message.Should().Contain("open generic");
        result.Contexts[0].RootTypeDisplayNames.Should().NotContain(r => r.Contains("WrapperDto<T>"));
    }

    [Fact]
    public void Open_generic_warning_mode_all_emits_per_type()
    {
        const string source = """
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;
            namespace Tests;

            [DataContract]
            public sealed class A<T>
            {
                [DataMember] public required T Value { get; init; }
            }

            [DataContract]
            public sealed class B<T>
            {
                [DataMember] public required T Value { get; init; }
            }

            [MinimalJsonSerializerContext]
            public partial class Ctx : JsonSerializerContext;
            """;
        var compilation = CompilationHelper.Create(source);
        var result = JsonSerializableRootCollector.Collect(
            compilation,
            new DiscoveryOptions { OpenGenericWarningMode = OpenGenericWarningMode.All }
        );
        var openGeneric = result.Diagnostics.Where(d => d.Id == "MSJ0004").ToList();
        openGeneric.Should().HaveCount(2);
        openGeneric.Should().OnlyContain(d => d.Message.Contains("was skipped"));
    }

    [Fact]
    public void Open_generic_warning_mode_none_is_silent()
    {
        var compilation = CompilationHelper.Create(ContextAndModels);
        var result = JsonSerializableRootCollector.Collect(
            compilation,
            new DiscoveryOptions { OpenGenericWarningMode = OpenGenericWarningMode.None }
        );
        result.Diagnostics.Should().NotContain(d => d.Id == "MSJ0004");
    }

    [Fact]
    public void List_dto_name_gets_mangled_list_roots_without_short_name_collision()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;
            namespace Tests;

            [DataContract]
            public sealed record MoneyDetailsDto
            {
                [DataMember] public required string Id { get; init; }
            }

            // Common API naming: "list response" DTOs that collide with List<T> short names.
            [DataContract]
            public sealed record ListMoneyDetailsDto
            {
                [DataMember] public required MoneyDetailsDto[] Items { get; init; }
            }

            [DataContract]
            public sealed record ListOrderDto
            {
                [DataMember] public required string Id { get; init; }
            }

            [MinimalJsonSerializerContext]
            public partial class Ctx : JsonSerializerContext;
            """;
        var compilation = CompilationHelper.Create(source);
        var result = JsonSerializableRootCollector.Collect(compilation);
        var ctx = result.Contexts[0];

        ctx.Roots.Should()
            .Contain(r =>
                r.TypeDisplayName.Contains("ListMoneyDetailsDto", StringComparison.Ordinal)
                && !r.TypeDisplayName.Contains("List<", StringComparison.Ordinal)
                && r.TypeInfoPropertyName == null
            );

        var listOfMoney = ctx.Roots.Single(r =>
            r.TypeDisplayName.Contains("List<", StringComparison.Ordinal)
            && r.TypeDisplayName.Contains("MoneyDetailsDto", StringComparison.Ordinal)
            && !r.TypeDisplayName.Contains("ListMoney", StringComparison.Ordinal)
        );
        listOfMoney.TypeInfoPropertyName.Should().NotBeNullOrEmpty();
        listOfMoney.TypeInfoPropertyName.Should().StartWith("ListOf_");

        var listOfListMoney = ctx.Roots.Single(r =>
            r.TypeDisplayName.Contains("List<", StringComparison.Ordinal)
            && r.TypeDisplayName.Contains("ListMoneyDetailsDto", StringComparison.Ordinal)
        );
        listOfListMoney.TypeInfoPropertyName.Should().NotBeNullOrEmpty();

        // Emitted attributes must use TypeInfoPropertyName for List<> roots.
        var emit = MinimalJsonContextSourceEmitter.Emit(ctx);
        emit.Should().Contain("TypeInfoPropertyName = \"ListOf_");
        emit.Should()
            .Contain(
                "typeof(global::System.Collections.Generic.List<global::Tests.MoneyDetailsDto>)"
            );
        // User DTO keeps bare registration (no forced rename unless colliding with another DTO).
        emit.Should().Contain("[JsonSerializable(typeof(global::Tests.ListMoneyDetailsDto))]");
        emit.Should().Contain("[JsonSerializable(typeof(global::Tests.ListOrderDto))]");

        // All TypeInfoPropertyName values must be unique.
        var names = ctx
            .Roots.Where(r => r.TypeInfoPropertyName is not null)
            .Select(r => r.TypeInfoPropertyName!)
            .ToList();
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Generic_dto_inheritance_emits_MSJ0009()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using MinimalSerializers.Json;
            namespace Tests;

            public enum CategoryFields
            {
                Name = 0,
                Code = 1,
            }

            [DataContract]
            public record QuerySubGroupResultDto<TFields>
                where TFields : Enum
            {
                [DataMember] public required TFields FieldName { get; init; }
                [DataMember] public required string? Value { get; init; }
            }

            [DataContract]
            public sealed record QueryGroupResultDto<TFields> : QuerySubGroupResultDto<TFields>
                where TFields : Enum
            {
                [DataMember]
                public IReadOnlyCollection<QuerySubGroupResultDto<TFields>>? SubGroupResults { get; init; }
            }

            [DataContract]
            public sealed record HolderDto
            {
                [DataMember]
                public required QueryGroupResultDto<CategoryFields> Group { get; init; }
            }

            [MinimalJsonSerializerContext]
            public partial class Ctx : JsonSerializerContext;
            """;
        var compilation = CompilationHelper.Create(source);
        var result = JsonSerializableRootCollector.Collect(compilation);

        var msj0009 = result.Diagnostics.Where(d => d.Id == "MSJ0009").ToList();
        msj0009.Should().NotBeEmpty();
        msj0009.Should().Contain(d => d.Message.Contains("CS0102", StringComparison.Ordinal));
        msj0009
            .Should()
            .Contain(d =>
                d.Message.Contains("QueryGroupResultDto", StringComparison.Ordinal)
                && d.Message.Contains("QuerySubGroupResultDto", StringComparison.Ordinal)
            );

        // Closed constructions still registered.
        var roots = result.Contexts[0].RootTypeDisplayNames;
        roots.Should().Contain(r => r.Contains("QueryGroupResultDto") && r.Contains("CategoryFields"));
        roots
            .Should()
            .Contain(r => r.Contains("QuerySubGroupResultDto") && r.Contains("CategoryFields"));
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
