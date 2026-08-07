using System.Text;
using Microsoft.CodeAnalysis;

namespace MinimalSerializers.Json.Discovery;

internal static class TypeDisplayNameFormatter
{
    private static readonly SymbolDisplayFormat Format = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
    );

    private static readonly SymbolDisplayFormat ShortNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
    );

    public static string ToGlobalDisplayString(ITypeSymbol type) => type.ToDisplayString(Format);

    public static string ToArrayDisplayString(ITypeSymbol elementType) =>
        ToGlobalDisplayString(elementType) + "[]";

    public static string ToListDisplayString(ITypeSymbol elementType) =>
        "global::System.Collections.Generic.List<" + ToGlobalDisplayString(elementType) + ">";

    /// <summary>
    /// Builds a stable STJ TypeInfoPropertyName identifier from a type display shape.
    /// </summary>
    public static string ToTypeInfoPropertyName(string prefix, ITypeSymbol type)
    {
        var shortName = type.ToDisplayString(ShortNameFormat);
        return prefix + "_" + SanitizeIdentifier(shortName);
    }

    public static string ToTypeInfoPropertyNameFromDisplay(string prefix, string globalDisplay)
    {
        var trimmed = globalDisplay;
        if (trimmed.StartsWith("global::", StringComparison.Ordinal))
        {
            trimmed = trimmed["global::".Length..];
        }

        return prefix + "_" + SanitizeIdentifier(trimmed);
    }

    /// <summary>
    /// Approximate STJ default short name used for TypeInfo property collision checks.
    /// </summary>
    public static string ToDefaultStjShortName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return ToDefaultStjShortName(array.ElementType) + "Array";
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            return SanitizeIdentifier(named.ToDisplayString(ShortNameFormat));
        }

        return SanitizeIdentifier(type.Name);
    }

    public static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "Type";
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        if (sb.Length == 0)
        {
            return "Type";
        }

        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }
}
