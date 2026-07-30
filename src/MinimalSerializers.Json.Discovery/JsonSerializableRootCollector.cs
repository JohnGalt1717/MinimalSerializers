using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalSerializers.Json.Discovery;

/// <summary>
/// Discovers DataContract graphs and MinimalJsonSerializerContext partials in a compilation.
/// </summary>
public static class JsonSerializableRootCollector
{
    public static DiscoveryResult Collect(Compilation compilation, DiscoveryOptions? options = null)
    {
        options ??= new DiscoveryOptions();
        var diagnostics = ImmutableArray.CreateBuilder<DiscoveryDiagnostic>();
        var contexts = ImmutableArray.CreateBuilder<DiscoveredContext>();

        var dataContractAttr = compilation.GetTypeByMetadataName(WellKnownTypeNames.DataContract);
        var dataMemberAttr = compilation.GetTypeByMetadataName(WellKnownTypeNames.DataMember);
        var ignoreDataMemberAttr = compilation.GetTypeByMetadataName(
            WellKnownTypeNames.IgnoreDataMember
        );
        var jsonIgnoreAttr = compilation.GetTypeByMetadataName(WellKnownTypeNames.JsonIgnore);
        var minimalAttr = compilation.GetTypeByMetadataName(
            WellKnownTypeNames.MinimalJsonSerializerContext
        );
        var jsonContextBase = compilation.GetTypeByMetadataName(
            WellKnownTypeNames.JsonSerializerContext
        );

        if (minimalAttr is null)
        {
            diagnostics.Add(
                new DiscoveryDiagnostic(
                    "MSJ0001",
                    DiscoveryDiagnosticSeverity.Info,
                    "MinimalJsonSerializerContextAttribute type was not found in referenced assemblies."
                )
            );
        }

        var contextSymbols = FindContextTypes(compilation, minimalAttr).ToList();
        if (contextSymbols.Count == 0)
        {
            diagnostics.Add(
                new DiscoveryDiagnostic(
                    "MSJ0001",
                    DiscoveryDiagnosticSeverity.Info,
                    minimalAttr is null
                        ? "No [MinimalJsonSerializerContext] types were found (attribute type was not resolvable from references)."
                        : "No [MinimalJsonSerializerContext] types were found."
                )
            );
            return new DiscoveryResult(contexts.ToImmutable(), diagnostics.ToImmutable());
        }

        var rootNames = CollectRootDisplayNames(
            compilation,
            options,
            dataContractAttr,
            dataMemberAttr,
            ignoreDataMemberAttr,
            jsonIgnoreAttr,
            diagnostics
        );

        if (rootNames.Length == 0)
        {
            diagnostics.Add(
                new DiscoveryDiagnostic(
                    "MSJ0002",
                    DiscoveryDiagnosticSeverity.Warning,
                    "A MinimalJsonSerializerContext was found but no [DataContract] types were discovered."
                )
            );
        }

        foreach (var context in contextSymbols)
        {
            var ctxDiagnostics = ImmutableArray.CreateBuilder<DiscoveryDiagnostic>();
            var isPartial = IsPartialType(context);
            var derives = DerivesFrom(context, jsonContextBase);
            var accessibility = context.DeclaredAccessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Internal => "internal",
                Accessibility.Protected => "protected",
                Accessibility.Private => "private",
                Accessibility.ProtectedAndInternal => "private protected",
                Accessibility.ProtectedOrInternal => "protected internal",
                _ => "internal",
            };

            if (!isPartial)
            {
                ctxDiagnostics.Add(
                    new DiscoveryDiagnostic(
                        "MSJ0003",
                        DiscoveryDiagnosticSeverity.Error,
                        $"Type '{context.ToDisplayString()}' must be declared partial."
                    )
                );
            }

            if (!derives)
            {
                ctxDiagnostics.Add(
                    new DiscoveryDiagnostic(
                        "MSJ0003",
                        DiscoveryDiagnosticSeverity.Error,
                        $"Type '{context.ToDisplayString()}' must derive from JsonSerializerContext."
                    )
                );
            }

            var ns =
                context.ContainingNamespace?.IsGlobalNamespace == true
                    ? string.Empty
                    : context.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            contexts.Add(
                new DiscoveredContext(
                    ns,
                    context.Name,
                    accessibility,
                    isPartial,
                    derives,
                    rootNames,
                    ctxDiagnostics.ToImmutable()
                )
            );
        }

        return new DiscoveryResult(contexts.ToImmutable(), diagnostics.ToImmutable());
    }

    private static IEnumerable<INamedTypeSymbol> FindContextTypes(
        Compilation compilation,
        INamedTypeSymbol? minimalAttr
    )
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                if (HasMinimalContextAttribute(symbol, minimalAttr))
                {
                    yield return symbol;
                }
            }
        }
    }

    private static bool HasMinimalContextAttribute(
        INamedTypeSymbol symbol,
        INamedTypeSymbol? minimalAttr
    )
    {
        if (minimalAttr is not null && HasAttribute(symbol, minimalAttr))
        {
            return true;
        }

        // Fallback when the attribute assembly isn't fully resolvable in the task compilation.
        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is "MinimalJsonSerializerContextAttribute" or "MinimalJsonSerializerContext")
            {
                return true;
            }

            var display = attribute.AttributeClass?.ToDisplayString();
            if (
                display is not null
                && display.EndsWith(
                    "MinimalJsonSerializerContextAttribute",
                    StringComparison.Ordinal
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<string> CollectRootDisplayNames(
        Compilation compilation,
        DiscoveryOptions options,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? dataMemberAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        ImmutableArray<DiscoveryDiagnostic>.Builder diagnostics
    )
    {
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<ITypeSymbol>();

        foreach (var type in GetAllNamedTypes(compilation.Assembly.GlobalNamespace))
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Enum))
            {
                continue;
            }

            if (type.IsStatic)
            {
                continue;
            }

            if (!HasNamedAttribute(type, dataContractAttr, "DataContractAttribute", "DataContract"))
            {
                continue;
            }

            if (type.IsGenericType && type.IsUnboundGenericType)
            {
                diagnostics.Add(
                    new DiscoveryDiagnostic(
                        "MSJ0004",
                        DiscoveryDiagnosticSeverity.Warning,
                        $"Open generic DataContract type '{type.ToDisplayString()}' was skipped."
                    )
                );
                continue;
            }

            if (
                type.IsGenericType
                && type.TypeParameters.Length > 0
                && type.TypeArguments.Any(a => a is ITypeParameterSymbol)
            )
            {
                diagnostics.Add(
                    new DiscoveryDiagnostic(
                        "MSJ0004",
                        DiscoveryDiagnosticSeverity.Warning,
                        $"Open generic DataContract type '{type.ToDisplayString()}' was skipped."
                    )
                );
                continue;
            }

            Enqueue(type);
        }

        while (queue.Count > 0)
        {
            var current = UnwrapNullable(queue.Dequeue());
            if (current is null || current.SpecialType == SpecialType.System_Object)
            {
                continue;
            }

            if (current is IErrorTypeSymbol)
            {
                // Common when task compilation lacks a full reference set for BCL primitives.
                // STJ still serializes primitives on object graphs; skip quietly.
                continue;
            }

            if (
                current is INamedTypeSymbol { IsGenericType: true } open
                && open.TypeArguments.Any(a => a is ITypeParameterSymbol)
            )
            {
                continue;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            switch (current)
            {
                case IArrayTypeSymbol array:
                    AddRoot(roots, TypeDisplayNameFormatter.ToGlobalDisplayString(array));
                    Enqueue(array.ElementType);
                    continue;
                case INamedTypeSymbol named
                    when IsSupportedDictionary(named, out var key, out var value):
                    if (options.IncludeDictionaries)
                    {
                        AddRoot(roots, TypeDisplayNameFormatter.ToGlobalDisplayString(named));
                    }
                    Enqueue(key);
                    Enqueue(value);
                    continue;
                case INamedTypeSymbol named when IsSupportedCollection(named, out var element):
                    if (options.IncludeDeclaredCollectionInterfaces)
                    {
                        AddRoot(roots, TypeDisplayNameFormatter.ToGlobalDisplayString(named));
                    }
                    Enqueue(element);
                    continue;
                case INamedTypeSymbol named:
                    if (named.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum)
                    {
                        RegisterObjectOrEnum(named, options, roots);
                        if (named.TypeKind != TypeKind.Enum)
                        {
                            WalkMembers(
                                named,
                                dataContractAttr,
                                dataMemberAttr,
                                ignoreDataMemberAttr,
                                jsonIgnoreAttr,
                                Enqueue
                            );
                        }
                    }
                    continue;
            }
        }

        return roots.ToImmutableArray();

        void Enqueue(ITypeSymbol? type)
        {
            if (type is null)
            {
                return;
            }

            queue.Enqueue(type);
        }
    }

    private static void RegisterObjectOrEnum(
        INamedTypeSymbol type,
        DiscoveryOptions options,
        SortedSet<string> roots
    )
    {
        // Primitives/BCL scalars are handled by STJ without explicit roots.
        if (IsPrimitiveLike(type))
        {
            return;
        }

        AddRoot(roots, TypeDisplayNameFormatter.ToGlobalDisplayString(type));
        if (options.IncludeArrays)
        {
            AddRoot(roots, TypeDisplayNameFormatter.ToArrayDisplayString(type));
        }

        if (options.IncludeList)
        {
            AddRoot(roots, TypeDisplayNameFormatter.ToListDisplayString(type));
        }
    }

    private static bool IsPrimitiveLike(ITypeSymbol type)
    {
        if (type.SpecialType is not SpecialType.None and not SpecialType.System_Object)
        {
            return true;
        }

        var display = type.ToDisplayString();
        return display
            is "System.Guid"
                or "System.DateTime"
                or "System.DateTimeOffset"
                or "System.DateOnly"
                or "System.TimeOnly"
                or "System.TimeSpan"
                or "System.Uri"
                or "System.Version"
                or "System.Decimal"
                or "decimal";
    }

    private static void WalkMembers(
        INamedTypeSymbol type,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? dataMemberAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        Action<ITypeSymbol?> enqueue
    )
    {
        var members = type.GetMembers()
            .Where(m => m is IPropertySymbol or IFieldSymbol)
            .Where(m => !m.IsStatic)
            .ToList();

        var hasDataMembers = members.Any(m =>
            HasNamedAttribute(m, dataMemberAttr, "DataMemberAttribute", "DataMember")
        );

        foreach (var member in members)
        {
            if (
                HasNamedAttribute(
                    member,
                    ignoreDataMemberAttr,
                    "IgnoreDataMemberAttribute",
                    "IgnoreDataMember"
                )
            )
            {
                continue;
            }

            if (HasNamedAttribute(member, jsonIgnoreAttr, "JsonIgnoreAttribute", "JsonIgnore"))
            {
                continue;
            }

            if (hasDataMembers)
            {
                if (!HasNamedAttribute(member, dataMemberAttr, "DataMemberAttribute", "DataMember"))
                {
                    continue;
                }
            }
            else
            {
                // Fallback: public readable instance properties on DataContract types.
                if (
                    member
                    is not IPropertySymbol { DeclaredAccessibility: Accessibility.Public } prop
                )
                {
                    continue;
                }

                if (prop.GetMethod is null)
                {
                    continue;
                }
            }

            var memberType = member switch
            {
                IPropertySymbol p => p.Type,
                IFieldSymbol f => f.Type,
                _ => null,
            };

            enqueue(memberType);
        }

        if (
            type.BaseType is { SpecialType: not SpecialType.System_Object } baseType
            && HasNamedAttribute(
                baseType,
                dataContractAttr,
                "DataContractAttribute",
                "DataContract"
            )
        )
        {
            enqueue(baseType);
        }
    }

    private static bool IsSupportedCollection(INamedTypeSymbol named, out ITypeSymbol element)
    {
        element = null!;
        if (!named.IsGenericType || named.TypeArguments.Length != 1)
        {
            // IEnumerable without T is not useful
            if (
                named.AllInterfaces.Any(i =>
                    i.OriginalDefinition.SpecialType == SpecialType.System_Collections_IEnumerable
                )
                && named.TypeArguments.Length == 1
            )
            {
                element = named.TypeArguments[0];
                return true;
            }

            return false;
        }

        var def = named.OriginalDefinition.ToDisplayString();
        if (
            def
            is "System.Collections.Generic.List<T>"
                or "System.Collections.Generic.IList<T>"
                or "System.Collections.Generic.ICollection<T>"
                or "System.Collections.Generic.IEnumerable<T>"
                or "System.Collections.Generic.IReadOnlyList<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>"
                or "System.Collections.Generic.ISet<T>"
                or "System.Collections.Generic.HashSet<T>"
                or "System.Collections.Generic.IReadOnlySet<T>"
                or "System.Collections.Immutable.ImmutableArray<T>"
                or "System.Collections.Immutable.ImmutableList<T>"
        )
        {
            element = named.TypeArguments[0];
            return true;
        }

        // Also treat types that implement IEnumerable<T>
        foreach (var iface in named.AllInterfaces)
        {
            if (
                iface.OriginalDefinition.SpecialType
                    == SpecialType.System_Collections_Generic_IEnumerable_T
                && iface.TypeArguments.Length == 1
            )
            {
                element = iface.TypeArguments[0];
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedDictionary(
        INamedTypeSymbol named,
        out ITypeSymbol key,
        out ITypeSymbol value
    )
    {
        key = null!;
        value = null!;
        if (!named.IsGenericType || named.TypeArguments.Length != 2)
        {
            return false;
        }

        var def = named.OriginalDefinition.ToDisplayString();
        if (
            def
            is "System.Collections.Generic.Dictionary<TKey, TValue>"
                or "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                or "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>"
        )
        {
            key = named.TypeArguments[0];
            value = named.TypeArguments[1];
            return true;
        }

        foreach (var iface in named.AllInterfaces)
        {
            var ifaceDef = iface.OriginalDefinition.ToDisplayString();
            if (
                ifaceDef
                    is "System.Collections.Generic.IDictionary<TKey, TValue>"
                        or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                && iface.TypeArguments.Length == 2
            )
            {
                key = iface.TypeArguments[0];
                value = iface.TypeArguments[1];
                return true;
            }
        }

        return false;
    }

    private static void AddRoot(SortedSet<string> roots, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            roots.Add(displayName);
        }
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (
            type
                is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                } n
            && n.TypeArguments.Length == 1
        )
        {
            return n.TypeArguments[0];
        }

        // Nullable reference annotations don't change the underlying type symbol.
        return type.WithNullableAnnotation(NullableAnnotation.None);
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType) =>
        symbol
            .GetAttributes()
            .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));

    private static bool HasNamedAttribute(
        ISymbol symbol,
        INamedTypeSymbol? attributeType,
        params string[] names
    )
    {
        if (attributeType is not null && HasAttribute(symbol, attributeType))
        {
            return true;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            var simpleName = attribute.AttributeClass?.Name;
            if (simpleName is not null && names.Contains(simpleName, StringComparer.Ordinal))
            {
                return true;
            }

            var display = attribute.AttributeClass?.ToDisplayString();
            if (
                display is not null
                && names.Any(n => display.EndsWith(n, StringComparison.Ordinal))
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPartialType(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax() is TypeDeclarationSyntax syntax
                && syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var item in GetTypeAndNested(type))
            {
                yield return item;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in GetAllNamedTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypeAndNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var item in GetTypeAndNested(nested))
            {
                yield return item;
            }
        }
    }
}
