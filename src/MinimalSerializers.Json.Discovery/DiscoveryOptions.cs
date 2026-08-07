namespace MinimalSerializers.Json.Discovery;

/// <summary>
/// How open-generic DataContract skip diagnostics (MSJ0004) are reported.
/// </summary>
public enum OpenGenericWarningMode
{
    /// <summary>One summary warning with the skip count (default).</summary>
    Summary = 0,

    /// <summary>One warning per skipped open generic type.</summary>
    All = 1,

    /// <summary>Do not emit MSJ0004.</summary>
    None = 2,
}

/// <summary>
/// Options controlling which collection roots are emitted for discovered types.
/// </summary>
public sealed class DiscoveryOptions
{
    public bool IncludeArrays { get; init; } = true;
    public bool IncludeList { get; init; } = true;
    public bool IncludeDeclaredCollectionInterfaces { get; init; } = true;
    public bool IncludeDictionaries { get; init; } = true;

    /// <summary>
    /// Controls MSJ0004 noise for skipped open generic DataContract types. Default: Summary.
    /// </summary>
    public OpenGenericWarningMode OpenGenericWarningMode { get; init; } =
        OpenGenericWarningMode.Summary;
}
