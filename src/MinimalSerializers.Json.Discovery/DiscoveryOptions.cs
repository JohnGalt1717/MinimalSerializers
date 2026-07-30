namespace MinimalSerializers.Json.Discovery;

/// <summary>
/// Options controlling which collection roots are emitted for discovered types.
/// </summary>
public sealed class DiscoveryOptions
{
    public bool IncludeArrays { get; init; } = true;
    public bool IncludeList { get; init; } = true;
    public bool IncludeDeclaredCollectionInterfaces { get; init; } = true;
    public bool IncludeDictionaries { get; init; } = true;
}
