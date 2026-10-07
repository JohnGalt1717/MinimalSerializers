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
        ImmutableArray<DiscoveredRoot> roots,
        ImmutableArray<DiscoveryDiagnostic> diagnostics,
        bool emitMetadataGenerationMode = false
    )
    {
        NamespaceName = namespaceName;
        TypeName = typeName;
        Accessibility = accessibility;
        IsPartial = isPartial;
        DerivesFromJsonSerializerContext = derivesFromJsonSerializerContext;
        Roots = roots;
        Diagnostics = diagnostics;
        EmitMetadataGenerationMode = emitMetadataGenerationMode;
    }

    public string NamespaceName { get; }
    public string TypeName { get; }
    public string Accessibility { get; }
    public bool IsPartial { get; }
    public bool DerivesFromJsonSerializerContext { get; }

    /// <summary>Roots to emit as [JsonSerializable] attributes.</summary>
    public ImmutableArray<DiscoveredRoot> Roots { get; }

    /// <summary>
    /// Convenience projection of <see cref="Roots"/> type display names (stable order).
    /// Prefer <see cref="Roots"/> when TypeInfoPropertyName matters.
    /// </summary>
    public ImmutableArray<string> RootTypeDisplayNames =>
        Roots.Select(static r => r.TypeDisplayName).ToImmutableArray();

    public ImmutableArray<DiscoveryDiagnostic> Diagnostics { get; }

    /// <summary>
    /// When true, the generated partial emits Metadata-only STJ source-gen
    /// (no duplicate <c>JsonSourceGenerationOptions</c> on the user partial).
    /// </summary>
    public bool EmitMetadataGenerationMode { get; }

    public string FullyQualifiedMetadataName =>
        string.IsNullOrEmpty(NamespaceName) ? TypeName : NamespaceName + "." + TypeName;
}
