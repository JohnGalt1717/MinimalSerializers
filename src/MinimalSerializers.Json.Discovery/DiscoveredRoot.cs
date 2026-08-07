namespace MinimalSerializers.Json.Discovery;

/// <summary>
/// A single [JsonSerializable] root to emit, optionally with an explicit TypeInfoPropertyName
/// to avoid STJ SYSLIB1031 short-name collisions (e.g. List&lt;T&gt; vs types named List*Dto).
/// </summary>
public sealed class DiscoveredRoot : IComparable<DiscoveredRoot>
{
    public DiscoveredRoot(string typeDisplayName, string? typeInfoPropertyName = null)
    {
        TypeDisplayName = typeDisplayName;
        TypeInfoPropertyName = typeInfoPropertyName;
    }

    /// <summary>Fully qualified typeof(...) display string (global::...).</summary>
    public string TypeDisplayName { get; }

    /// <summary>
    /// Optional STJ TypeInfoPropertyName. Null means let STJ choose the default short name.
    /// </summary>
    public string? TypeInfoPropertyName { get; }

    public int CompareTo(DiscoveredRoot? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byType = string.CompareOrdinal(TypeDisplayName, other.TypeDisplayName);
        if (byType != 0)
        {
            return byType;
        }

        return string.CompareOrdinal(TypeInfoPropertyName, other.TypeInfoPropertyName);
    }

    public override bool Equals(object? obj) =>
        obj is DiscoveredRoot other
        && string.Equals(TypeDisplayName, other.TypeDisplayName, StringComparison.Ordinal)
        && string.Equals(
            TypeInfoPropertyName,
            other.TypeInfoPropertyName,
            StringComparison.Ordinal
        );

    public override int GetHashCode() => HashCode.Combine(TypeDisplayName, TypeInfoPropertyName);

    public override string ToString() =>
        TypeInfoPropertyName is null
            ? TypeDisplayName
            : $"{TypeDisplayName} => {TypeInfoPropertyName}";
}
