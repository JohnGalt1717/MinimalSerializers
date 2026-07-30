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

    public static string ToGlobalDisplayString(ITypeSymbol type) => type.ToDisplayString(Format);

    public static string ToArrayDisplayString(ITypeSymbol elementType) =>
        ToGlobalDisplayString(elementType) + "[]";

    public static string ToListDisplayString(ITypeSymbol elementType) =>
        "global::System.Collections.Generic.List<" + ToGlobalDisplayString(elementType) + ">";
}
