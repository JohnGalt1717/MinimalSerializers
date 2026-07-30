using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using MinimalSerializers.Json;

namespace MinimalSerializers.Json.Tests.Runtime;

public sealed class OptionsExtensionsTests
{
    [Fact]
    public void AddMinimalJsonContext_requires_Default_property()
    {
        var options = new JsonSerializerOptions();
        var act = () => options.AddMinimalJsonContext<BrokenContext>();
        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class BrokenContext : JsonSerializerContext
    {
        public BrokenContext() : base(null) { }
        protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
        public override JsonTypeInfo? GetTypeInfo(Type type) => null;
    }
}
