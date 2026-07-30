namespace MinimalSerializers.Json.Discovery;

internal static class WellKnownTypeNames
{
    public const string DataContract = "System.Runtime.Serialization.DataContractAttribute";
    public const string DataMember = "System.Runtime.Serialization.DataMemberAttribute";
    public const string IgnoreDataMember = "System.Runtime.Serialization.IgnoreDataMemberAttribute";
    public const string EnumMember = "System.Runtime.Serialization.EnumMemberAttribute";
    public const string JsonIgnore = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    public const string JsonSerializerContext = "System.Text.Json.Serialization.JsonSerializerContext";
    public const string MinimalJsonSerializerContext =
        "MinimalSerializers.Json.MinimalJsonSerializerContextAttribute";
}
