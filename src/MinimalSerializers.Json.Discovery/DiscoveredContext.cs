using System.Collections.Immutable;

namespace MinimalSerializers.Json.Discovery;

public sealed class DiscoveredContext
{
    public DiscoveredContext(
        string namespaceName,
        string typeName,
        string accessibility,
        bool isPartial,
        bool derivesFromJsonSerializerContext,
        ImmutableArray<string> rootTypeDisplayNames,
        ImmutableArray<DiscoveryDiagnostic> diagnostics
    )
    {
        NamespaceName = namespaceName;
        TypeName = typeName;
        Accessibility = accessibility;
        IsPartial = isPartial;
        DerivesFromJsonSerializerContext = derivesFromJsonSerializerContext;
        RootTypeDisplayNames = rootTypeDisplayNames;
        Diagnostics = diagnostics;
    }

    public string NamespaceName { get; }
    public string TypeName { get; }
    public string Accessibility { get; }
    public bool IsPartial { get; }
    public bool DerivesFromJsonSerializerContext { get; }
    public ImmutableArray<string> RootTypeDisplayNames { get; }
    public ImmutableArray<DiscoveryDiagnostic> Diagnostics { get; }

    public string FullyQualifiedMetadataName =>
        string.IsNullOrEmpty(NamespaceName) ? TypeName : NamespaceName + "." + TypeName;
}
