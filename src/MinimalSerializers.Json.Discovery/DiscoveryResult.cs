using System.Collections.Immutable;

namespace MinimalSerializers.Json.Discovery;

public sealed class DiscoveryResult
{
    public DiscoveryResult(
        ImmutableArray<DiscoveredContext> contexts,
        ImmutableArray<DiscoveryDiagnostic> diagnostics
    )
    {
        Contexts = contexts;
        Diagnostics = diagnostics;
    }

    public ImmutableArray<DiscoveredContext> Contexts { get; }
    public ImmutableArray<DiscoveryDiagnostic> Diagnostics { get; }
}
