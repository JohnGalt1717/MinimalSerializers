namespace MinimalSerializers.Json;

/// <summary>
/// Marks a partial <c>JsonSerializerContext</c> for automatic [JsonSerializable] root generation
/// from [DataContract] graphs in the same project.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MinimalJsonSerializerContextAttribute : Attribute
{
    /// <summary>When true, emits T[] roots for discovered object/enum types. Default true.</summary>
    public bool IncludeArrays { get; set; } = true;

    /// <summary>When true, emits List&lt;T&gt; roots for discovered object/enum types. Default true.</summary>
    public bool IncludeList { get; set; } = true;

    /// <summary>When true, also registers closed collection interface types found on members. Default true.</summary>
    public bool IncludeDeclaredCollectionInterfaces { get; set; } = true;

    /// <summary>When true, registers dictionary types found on members. Default true.</summary>
    public bool IncludeDictionaries { get; set; } = true;
}
