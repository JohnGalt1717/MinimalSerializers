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

        var roots = CollectRoots(
            compilation,
            options,
            dataContractAttr,
            dataMemberAttr,
            ignoreDataMemberAttr,
            jsonIgnoreAttr,
            diagnostics
        );

        if (roots.Length == 0)
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
                    roots,
                    ctxDiagnostics.ToImmutable(),
                    emitMetadataGenerationMode: !HasJsonSourceGenerationOptions(context)
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
        foreach (var attribute in TryGetAttributes(symbol))
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

    private static bool HasJsonSourceGenerationOptions(INamedTypeSymbol symbol)
    {
        foreach (var attribute in TryGetAttributes(symbol))
        {
            var name = attribute.AttributeClass?.Name;
            if (name is "JsonSourceGenerationOptionsAttribute" or "JsonSourceGenerationOptions")
            {
                return true;
            }
        }

        return false;
    }

    private static void TryRegisterErrorTypeCollection(
        IErrorTypeSymbol error,
        DiscoveryOptions options,
        SortedDictionary<string, DiscoveredRoot> rootsByDisplay,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        Dictionary<ITypeSymbol, bool> poisonCache,
        Action<ITypeSymbol?> enqueue
    )
    {
        if (error.TypeArguments.Length == 1 && IsKnownCollectionTypeName(error.Name))
        {
            var element = StripNrt(error.TypeArguments[0]);
            if (
                ContainsNonJsonFrameworkGraph(
                    element,
                    dataContractAttr,
                    ignoreDataMemberAttr,
                    jsonIgnoreAttr,
                    poisonCache
                )
            )
            {
                return;
            }

            var include =
                error.Name == "List"
                    ? options.IncludeList
                    : options.IncludeDeclaredCollectionInterfaces;
            if (include)
            {
                var display =
                    "global::"
                    + ErrorTypeCollectionNamespace(error.Name)
                    + "."
                    + error.Name
                    + "<"
                    + TypeDisplayNameFormatter.ToGlobalDisplayString(element)
                    + ">";
                var prefix = error.Name == "List" ? "ListOf" : "CollectionOf";
                AddRoot(
                    rootsByDisplay,
                    new DiscoveredRoot(
                        display,
                        TypeDisplayNameFormatter.ToTypeInfoPropertyNameFromDisplay(prefix, display)
                    )
                );
            }

            enqueue(element);
            return;
        }

        if (error.TypeArguments.Length == 2 && IsKnownDictionaryTypeName(error.Name))
        {
            var key = StripNrt(error.TypeArguments[0]);
            var value = StripNrt(error.TypeArguments[1]);
            if (
                ContainsNonJsonFrameworkGraph(
                    key,
                    dataContractAttr,
                    ignoreDataMemberAttr,
                    jsonIgnoreAttr,
                    poisonCache
                )
                || ContainsNonJsonFrameworkGraph(
                    value,
                    dataContractAttr,
                    ignoreDataMemberAttr,
                    jsonIgnoreAttr,
                    poisonCache
                )
            )
            {
                return;
            }

            if (options.IncludeDictionaries)
            {
                var display =
                    "global::"
                    + ErrorTypeCollectionNamespace(error.Name)
                    + "."
                    + error.Name
                    + "<"
                    + TypeDisplayNameFormatter.ToGlobalDisplayString(key)
                    + ", "
                    + TypeDisplayNameFormatter.ToGlobalDisplayString(value)
                    + ">";
                AddRoot(
                    rootsByDisplay,
                    new DiscoveredRoot(
                        display,
                        TypeDisplayNameFormatter.ToTypeInfoPropertyNameFromDisplay(
                            "DictionaryOf",
                            display
                        )
                    )
                );
            }

            enqueue(key);
            enqueue(value);
        }
    }

    private static bool IsKnownCollectionTypeName(string name) =>
        name
            is "List"
                or "IList"
                or "ICollection"
                or "IEnumerable"
                or "IReadOnlyList"
                or "IReadOnlyCollection"
                or "ISet"
                or "HashSet"
                or "IReadOnlySet"
                or "ImmutableArray"
                or "ImmutableList";

    private static bool IsKnownDictionaryTypeName(string name) =>
        name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary" or "ImmutableDictionary";

    private static string ErrorTypeCollectionNamespace(string name) =>
        name is "ImmutableArray" or "ImmutableList" or "ImmutableDictionary"
            ? "System.Collections.Immutable"
            : "System.Collections.Generic";

    private static bool IsKnownCollectionDefinition(INamedTypeSymbol definition)
    {
        var name = definition.Name;
        var ns = definition.ContainingNamespace?.ToDisplayString();
        if (ns is "System.Collections.Generic")
        {
            return name
                is "List"
                    or "IList"
                    or "ICollection"
                    or "IEnumerable"
                    or "IReadOnlyList"
                    or "IReadOnlyCollection"
                    or "ISet"
                    or "HashSet"
                    or "IReadOnlySet";
        }

        return ns is "System.Collections.Immutable" && name is "ImmutableArray" or "ImmutableList";
    }

    private static bool IsKnownDictionaryDefinition(INamedTypeSymbol definition)
    {
        var name = definition.Name;
        var ns = definition.ContainingNamespace?.ToDisplayString();
        if (ns is "System.Collections.Generic")
        {
            return name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary";
        }

        return ns is "System.Collections.Immutable" && name is "ImmutableDictionary";
    }

    private static ImmutableArray<DiscoveredRoot> CollectRoots(
        Compilation compilation,
        DiscoveryOptions options,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? dataMemberAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        ImmutableArray<DiscoveryDiagnostic>.Builder diagnostics
    )
    {
        // Keyed by typeof display string so we keep a single entry per closed type.
        var rootsByDisplay = new SortedDictionary<string, DiscoveredRoot>(StringComparer.Ordinal);
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<ITypeSymbol>();
        var openGenericSkips = new List<string>();
        var genericInheritanceWarned = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default
        );
        var poisonCache = new Dictionary<ITypeSymbol, bool>(SymbolEqualityComparer.Default);

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

            if (IsOpenGeneric(type))
            {
                // Abstract open generics are intentional bases (QueryRequestDto<T>, etc.).
                // Still skip them as roots; do not warn. Concrete open generics still warn.
                if (!type.IsAbstract)
                {
                    openGenericSkips.Add(type.ToDisplayString());
                }
                // Surface the STJ CS0102 footgun on the open definition as well as closed uses.
                DiagnoseGenericDtoInheritance(
                    type,
                    dataContractAttr,
                    diagnostics,
                    genericInheritanceWarned
                );
                continue;
            }

            Enqueue(type);
        }

        EmitOpenGenericDiagnostics(options, openGenericSkips, diagnostics);

        while (queue.Count > 0)
        {
            var current = UnwrapNullable(queue.Dequeue());
            if (current is null || current.SpecialType == SpecialType.System_Object)
            {
                continue;
            }

            if (current is IErrorTypeSymbol error)
            {
                // Duplicate BCL refs or missing ImplicitUsings make List<T> ErrorType.
                // Still register known collection shapes from the parsed generic name.
                TryRegisterErrorTypeCollection(
                    error,
                    options,
                    rootsByDisplay,
                    dataContractAttr,
                    ignoreDataMemberAttr,
                    jsonIgnoreAttr,
                    poisonCache,
                    Enqueue
                );
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
                case INamedTypeSymbol primitive when IsPrimitiveLike(primitive):
                    continue;
                case IArrayTypeSymbol array:
                    if (
                        ContainsNonJsonFrameworkGraph(
                            array,
                            dataContractAttr,
                            ignoreDataMemberAttr,
                            jsonIgnoreAttr,
                            poisonCache
                        )
                    )
                    {
                        continue;
                    }
                    if (options.IncludeArrays)
                    {
                        AddArrayRoot(
                            rootsByDisplay,
                            array,
                            mangle: !IsPrimitiveLike(array.ElementType)
                        );
                    }
                    Enqueue(array.ElementType);
                    continue;
                case INamedTypeSymbol named
                    when IsSupportedDictionary(named, out var key, out var value):
                    if (
                        ContainsNonJsonFrameworkGraph(
                            named,
                            dataContractAttr,
                            ignoreDataMemberAttr,
                            jsonIgnoreAttr,
                            poisonCache
                        )
                    )
                    {
                        continue;
                    }
                    if (options.IncludeDictionaries)
                    {
                        AddCollectionLikeRoot(rootsByDisplay, named, "DictionaryOf");
                    }
                    Enqueue(key);
                    Enqueue(value);
                    continue;
                case INamedTypeSymbol named when IsSupportedCollection(named, out var element):
                    if (
                        ContainsNonJsonFrameworkGraph(
                            named,
                            dataContractAttr,
                            ignoreDataMemberAttr,
                            jsonIgnoreAttr,
                            poisonCache
                        )
                    )
                    {
                        continue;
                    }
                    if (
                        named.OriginalDefinition.Name == "List"
                        && IsKnownCollectionDefinition(named.OriginalDefinition)
                    )
                    {
                        if (options.IncludeList)
                        {
                            AddRoot(
                                rootsByDisplay,
                                new DiscoveredRoot(
                                    TypeDisplayNameFormatter.ToGlobalDisplayString(named),
                                    TypeDisplayNameFormatter.ToTypeInfoPropertyName(
                                        "ListOf",
                                        element
                                    )
                                )
                            );
                        }
                    }
                    else if (options.IncludeDeclaredCollectionInterfaces)
                    {
                        AddCollectionLikeRoot(rootsByDisplay, named, "CollectionOf");
                    }
                    Enqueue(element);
                    continue;
                case INamedTypeSymbol named:
                    // Multipart DTOs (IFormFile members) are form-bound, not JSON.
                    // Rooting them makes STJ expand HttpContext and never complete.
                    if (
                        ContainsNonJsonFrameworkGraph(
                            named,
                            dataContractAttr,
                            ignoreDataMemberAttr,
                            jsonIgnoreAttr,
                            poisonCache
                        )
                    )
                    {
                        continue;
                    }

                    if (named.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum)
                    {
                        RegisterObjectOrEnum(
                            named,
                            options,
                            rootsByDisplay,
                            dataContractAttr,
                            diagnostics,
                            genericInheritanceWarned
                        );
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

        ResolveShortNameCollisions(rootsByDisplay);

        return rootsByDisplay.Values.ToImmutableArray();

        void Enqueue(ITypeSymbol? type)
        {
            if (type is null)
            {
                return;
            }

            queue.Enqueue(type);
        }
    }

    private static void EmitOpenGenericDiagnostics(
        DiscoveryOptions options,
        List<string> openGenericSkips,
        ImmutableArray<DiscoveryDiagnostic>.Builder diagnostics
    )
    {
        if (openGenericSkips.Count == 0)
        {
            return;
        }

        openGenericSkips.Sort(StringComparer.Ordinal);

        switch (options.OpenGenericWarningMode)
        {
            case OpenGenericWarningMode.None:
                return;
            case OpenGenericWarningMode.All:
                foreach (var name in openGenericSkips)
                {
                    diagnostics.Add(
                        new DiscoveryDiagnostic(
                            "MSJ0004",
                            DiscoveryDiagnosticSeverity.Warning,
                            $"Open generic DataContract type '{name}' was skipped."
                        )
                    );
                }
                return;
            default:
                // Summary (default)
                if (openGenericSkips.Count == 1)
                {
                    diagnostics.Add(
                        new DiscoveryDiagnostic(
                            "MSJ0004",
                            DiscoveryDiagnosticSeverity.Warning,
                            $"Skipped 1 open generic DataContract type: '{openGenericSkips[0]}'. Set MinimalJsonWarnOpenGenerics=all for per-type details."
                        )
                    );
                }
                else
                {
                    diagnostics.Add(
                        new DiscoveryDiagnostic(
                            "MSJ0004",
                            DiscoveryDiagnosticSeverity.Warning,
                            $"Skipped {openGenericSkips.Count} open generic DataContract types. Set MinimalJsonWarnOpenGenerics=all for per-type details, or none to silence."
                        )
                    );
                }
                return;
        }
    }

    private static bool IsOpenGeneric(INamedTypeSymbol type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        if (type.IsUnboundGenericType)
        {
            return true;
        }

        return type.TypeParameters.Length > 0
            && type.TypeArguments.Any(a => a is ITypeParameterSymbol);
    }

    private static void RegisterObjectOrEnum(
        INamedTypeSymbol type,
        DiscoveryOptions options,
        SortedDictionary<string, DiscoveredRoot> rootsByDisplay,
        INamedTypeSymbol? dataContractAttr,
        ImmutableArray<DiscoveryDiagnostic>.Builder diagnostics,
        HashSet<INamedTypeSymbol> genericInheritanceWarned
    )
    {
        // Primitives/BCL scalars are handled by STJ without explicit roots.
        if (IsPrimitiveLike(type))
        {
            return;
        }

        DiagnoseGenericDtoInheritance(
            type,
            dataContractAttr,
            diagnostics,
            genericInheritanceWarned
        );

        // Abstract DataContracts are bases (QueryRequestDto<T>, chart DTOs). Rooting
        // the closed generic base alongside a concrete derived type hangs STJ.
        if (type.IsAbstract)
        {
            return;
        }

        // Plain object/enum roots keep STJ default names unless a later collision forces a rename.
        // Do not also emit T[] / List<T> for every object — that triples STJ roots and
        // makes CoreCompile never finish on large graphs. Collection shapes are
        // registered when they appear as members.
        AddRoot(
            rootsByDisplay,
            new DiscoveredRoot(TypeDisplayNameFormatter.ToGlobalDisplayString(type))
        );
    }

    /// <summary>
    /// STJ source-gen can emit duplicate nested accessor classes (CS0102) for closed forms of
    /// <c>Derived&lt;T&gt; : Base&lt;T&gt;</c> DataContract graphs. Detect and warn clearly.
    /// </summary>
    private static void DiagnoseGenericDtoInheritance(
        INamedTypeSymbol type,
        INamedTypeSymbol? dataContractAttr,
        ImmutableArray<DiscoveryDiagnostic>.Builder diagnostics,
        HashSet<INamedTypeSymbol> genericInheritanceWarned
    )
    {
        if (!type.IsGenericType || type.TypeArguments.Length == 0)
        {
            return;
        }

        if (
            type.BaseType is not { SpecialType: not SpecialType.System_Object } baseType
            || !baseType.IsGenericType
        )
        {
            return;
        }

        if (
            !HasNamedAttribute(
                baseType.OriginalDefinition,
                dataContractAttr,
                "DataContractAttribute",
                "DataContract"
            )
            && !HasNamedAttribute(
                baseType,
                dataContractAttr,
                "DataContractAttribute",
                "DataContract"
            )
        )
        {
            // Also accept when the open generic definition carries the attribute (common case).
            var openBase = baseType.OriginalDefinition;
            if (
                !HasNamedAttribute(
                    openBase,
                    dataContractAttr,
                    "DataContractAttribute",
                    "DataContract"
                )
            )
            {
                // Still diagnose if both sides are DataContract-shaped via attribute name fallback
                // on either the constructed or definition form — already covered above.
            }
        }

        // Require the base to be a DataContract type (constructed or its open definition).
        var baseIsDataContract =
            HasNamedAttribute(baseType, dataContractAttr, "DataContractAttribute", "DataContract")
            || HasNamedAttribute(
                baseType.OriginalDefinition,
                dataContractAttr,
                "DataContractAttribute",
                "DataContract"
            );
        if (!baseIsDataContract)
        {
            return;
        }

        // Same type-argument identity (Derived<T> : Base<T> / Derived<T,U> : Base<T,U> with matching args).
        if (!SharesTypeArgumentsWithBase(type, baseType))
        {
            return;
        }

        // Dedupe on the open definition so open + closed constructions share one warning.
        var key = type.OriginalDefinition;
        if (!genericInheritanceWarned.Add(key))
        {
            return;
        }

        diagnostics.Add(
            new DiscoveryDiagnostic(
                "MSJ0009",
                DiscoveryDiagnosticSeverity.Warning,
                $"Generic DataContract inheritance '{type.ToDisplayString()} : {baseType.ToDisplayString()}' can break System.Text.Json source generation with CS0102 (duplicate nested accessor types). Prefer composition or flattened records without Derived<T> : Base<T>. Closed constructions of both types are still registered."
            )
        );
    }

    private static bool SharesTypeArgumentsWithBase(
        INamedTypeSymbol derived,
        INamedTypeSymbol baseType
    )
    {
        // Match when every base type argument appears in the derived type argument list
        // in the same order for the overlapping prefix (covers Derived<T> : Base<T> and
        // Derived<T,U> : Base<T>).
        if (baseType.TypeArguments.Length == 0 || derived.TypeArguments.Length == 0)
        {
            return false;
        }

        if (baseType.TypeArguments.Length > derived.TypeArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < baseType.TypeArguments.Length; i++)
        {
            if (
                !SymbolEqualityComparer.Default.Equals(
                    baseType.TypeArguments[i],
                    derived.TypeArguments[i]
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static void AddArrayRoot(
        SortedDictionary<string, DiscoveredRoot> rootsByDisplay,
        IArrayTypeSymbol array,
        bool mangle
    )
    {
        var display = TypeDisplayNameFormatter.ToGlobalDisplayString(array);
        string? name = mangle
            ? TypeDisplayNameFormatter.ToTypeInfoPropertyName("ArrayOf", array.ElementType)
            : null;
        AddRoot(rootsByDisplay, new DiscoveredRoot(display, name));
    }

    private static void AddCollectionLikeRoot(
        SortedDictionary<string, DiscoveredRoot> rootsByDisplay,
        INamedTypeSymbol named,
        string prefix
    )
    {
        var display = TypeDisplayNameFormatter.ToGlobalDisplayString(named);
        // Always assign a mangled name for collection/dictionary closed shapes so they cannot
        // collide with user DTOs named List* / Dictionary* / etc.
        // Use the full closed type for uniqueness (includes element args).
        var name = TypeDisplayNameFormatter.ToTypeInfoPropertyNameFromDisplay(prefix, display);
        AddRoot(rootsByDisplay, new DiscoveredRoot(display, name));
    }

    private static void AddRoot(
        SortedDictionary<string, DiscoveredRoot> rootsByDisplay,
        DiscoveredRoot root
    )
    {
        if (string.IsNullOrWhiteSpace(root.TypeDisplayName))
        {
            return;
        }

        // Prefer an entry that already has an explicit TypeInfoPropertyName.
        // Prefer ListOf_* over CollectionOf_* for the same List<T> display.
        if (rootsByDisplay.TryGetValue(root.TypeDisplayName, out var existing))
        {
            if (existing.TypeInfoPropertyName is null && root.TypeInfoPropertyName is not null)
            {
                rootsByDisplay[root.TypeDisplayName] = root;
            }
            else if (
                existing.TypeInfoPropertyName is not null
                && root.TypeInfoPropertyName is not null
                && existing.TypeInfoPropertyName.StartsWith(
                    "CollectionOf_",
                    StringComparison.Ordinal
                )
                && root.TypeInfoPropertyName.StartsWith("ListOf_", StringComparison.Ordinal)
            )
            {
                rootsByDisplay[root.TypeDisplayName] = root;
            }

            return;
        }

        rootsByDisplay[root.TypeDisplayName] = root;
    }

    /// <summary>
    /// If two non-wrapper roots would still share the same STJ short name (same type name,
    /// different namespaces), assign unique TypeInfoPropertyName values.
    /// </summary>
    private static void ResolveShortNameCollisions(
        SortedDictionary<string, DiscoveredRoot> rootsByDisplay
    )
    {
        // Approximate STJ short names for roots that still use the default.
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (display, root) in rootsByDisplay)
        {
            if (root.TypeInfoPropertyName is not null)
            {
                continue;
            }

            var shortName = ExtractDefaultShortName(display);
            if (!groups.TryGetValue(shortName, out var list))
            {
                list = [];
                groups[shortName] = list;
            }

            list.Add(display);
        }

        foreach (var (shortName, displays) in groups)
        {
            if (displays.Count < 2)
            {
                continue;
            }

            displays.Sort(StringComparer.Ordinal);
            for (var i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                var mangled =
                    TypeDisplayNameFormatter.ToTypeInfoPropertyNameFromDisplay("Type", display)
                    + (i == 0 ? string.Empty : $"_{i + 1}");
                rootsByDisplay[display] = new DiscoveredRoot(display, mangled);
            }
        }

        // Also ensure assigned property names are unique across all roots.
        EnsureUniquePropertyNames(rootsByDisplay);
    }

    private static void EnsureUniquePropertyNames(
        SortedDictionary<string, DiscoveredRoot> rootsByDisplay
    )
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var ordered = rootsByDisplay.Keys.ToList();
        foreach (var display in ordered)
        {
            var root = rootsByDisplay[display];
            if (root.TypeInfoPropertyName is null)
            {
                // Reserve the default short name so later explicit names don't steal it.
                used.Add(ExtractDefaultShortName(display));
                continue;
            }

            var name = root.TypeInfoPropertyName;
            if (used.Add(name))
            {
                continue;
            }

            var suffix = 2;
            string candidate;
            do
            {
                candidate = name + "_" + suffix;
                suffix++;
            } while (!used.Add(candidate));

            rootsByDisplay[display] = new DiscoveredRoot(display, candidate);
        }
    }

    private static string ExtractDefaultShortName(string globalDisplay)
    {
        var trimmed = globalDisplay;
        if (trimmed.StartsWith("global::", StringComparison.Ordinal))
        {
            trimmed = trimmed["global::".Length..];
        }

        // Array: Foo.Bar[] → BarArray-ish via sanitize of last segment + Array
        if (trimmed.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = trimmed[..^2];
            var lastDot = element.LastIndexOf('.');
            var simple = lastDot >= 0 ? element[(lastDot + 1)..] : element;
            return TypeDisplayNameFormatter.SanitizeIdentifier(simple) + "Array";
        }

        // Generic: Namespace.List<Foo.Bar> → take type name before '<'
        var generic = trimmed.IndexOf('<');
        if (generic >= 0)
        {
            var head = trimmed[..generic];
            var lastDot = head.LastIndexOf('.');
            var simple = lastDot >= 0 ? head[(lastDot + 1)..] : head;
            // STJ often names List<X> as ListX / ListOfX; sanitize full generic short form.
            return TypeDisplayNameFormatter.SanitizeIdentifier(simple + trimmed[generic..]);
        }

        {
            var lastDot = trimmed.LastIndexOf('.');
            var simple = lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
            return TypeDisplayNameFormatter.SanitizeIdentifier(simple);
        }
    }

    private static bool IsPrimitiveLike(ITypeSymbol type)
    {
        if (type.SpecialType is not SpecialType.None and not SpecialType.System_Object)
        {
            return true;
        }

        var display = type.ToDisplayString();
        if (
            display
            is "System.Guid"
                or "System.DateTime"
                or "System.DateTimeOffset"
                or "System.DateOnly"
                or "System.TimeOnly"
                or "System.TimeSpan"
                or "System.Uri"
                or "System.Version"
                or "System.Decimal"
                or "decimal"
                or "System.Text.Json.JsonElement"
                or "System.Text.Json.JsonDocument"
                or "System.Text.Json.JsonValueKind"
        )
        {
            return true;
        }

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns is "System.Text.Json.Nodes")
        {
            return true;
        }

        return IsNonJsonFrameworkTypeName(type.Name);
    }

    /// <summary>
    /// True when this type's JSON graph would pull multipart/runtime types
    /// (<c>IFormFile</c>, <c>Stream</c>, …). Those graphs make STJ source-gen
    /// never complete; omit the whole type as a JSON root. No consumer
    /// <c>[JsonIgnore]</c> or csproj flag is required.
    /// </summary>
    private static bool ContainsNonJsonFrameworkGraph(
        ITypeSymbol type,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        Dictionary<ITypeSymbol, bool> cache
    ) =>
        ContainsNonJsonFrameworkGraph(
            type,
            dataContractAttr,
            ignoreDataMemberAttr,
            jsonIgnoreAttr,
            cache,
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)
        );

    private static bool ContainsNonJsonFrameworkGraph(
        ITypeSymbol type,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        Dictionary<ITypeSymbol, bool> cache,
        HashSet<ITypeSymbol> visiting
    )
    {
        type = UnwrapNullable(type) ?? type;

        if (cache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(type))
        {
            return false;
        }

        var poison = ComputeNonJsonFrameworkGraph(
            type,
            dataContractAttr,
            ignoreDataMemberAttr,
            jsonIgnoreAttr,
            cache,
            visiting
        );

        visiting.Remove(type);

        // Do not memoize "safe" while an ancestor is still in progress: a later
        // Stream member on that ancestor would otherwise leave a cyclic peer cached
        // as safe.
        if (poison || visiting.Count == 0)
        {
            cache[type] = poison;
        }

        return poison;
    }

    private static bool ComputeNonJsonFrameworkGraph(
        ITypeSymbol type,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        Dictionary<ITypeSymbol, bool> cache,
        HashSet<ITypeSymbol> visiting
    )
    {
        if (type is IErrorTypeSymbol)
        {
            return IsNonJsonFrameworkType(type);
        }

        if (IsNonJsonFrameworkType(type))
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsNonJsonFrameworkGraph(
                array.ElementType,
                dataContractAttr,
                ignoreDataMemberAttr,
                jsonIgnoreAttr,
                cache,
                visiting
            );
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (IsPrimitiveLike(named) || named.TypeKind == TypeKind.Enum)
        {
            return false;
        }

        var isDataContract = HasNamedAttribute(
            named,
            dataContractAttr,
            "DataContractAttribute",
            "DataContract"
        );

        if (
            isDataContract
            && MembersContainNonJsonFrameworkGraph(
                named,
                dataContractAttr,
                ignoreDataMemberAttr,
                jsonIgnoreAttr,
                includeNonPublic: true,
                cache,
                visiting
            )
        )
        {
            return true;
        }

        if (IsSupportedCollection(named, out var element))
        {
            return ContainsNonJsonFrameworkGraph(
                element,
                dataContractAttr,
                ignoreDataMemberAttr,
                jsonIgnoreAttr,
                cache,
                visiting
            );
        }

        if (IsSupportedDictionary(named, out var key, out var value))
        {
            return ContainsNonJsonFrameworkGraph(
                    key,
                    dataContractAttr,
                    ignoreDataMemberAttr,
                    jsonIgnoreAttr,
                    cache,
                    visiting
                )
                || ContainsNonJsonFrameworkGraph(
                    value,
                    dataContractAttr,
                    ignoreDataMemberAttr,
                    jsonIgnoreAttr,
                    cache,
                    visiting
                );
        }

        return !isDataContract
            && MembersContainNonJsonFrameworkGraph(
                named,
                dataContractAttr,
                ignoreDataMemberAttr,
                jsonIgnoreAttr,
                includeNonPublic: false,
                cache,
                visiting
            );
    }

    private static bool MembersContainNonJsonFrameworkGraph(
        INamedTypeSymbol named,
        INamedTypeSymbol? dataContractAttr,
        INamedTypeSymbol? ignoreDataMemberAttr,
        INamedTypeSymbol? jsonIgnoreAttr,
        bool includeNonPublic,
        Dictionary<ITypeSymbol, bool> cache,
        HashSet<ITypeSymbol> visiting
    )
    {
        foreach (var member in named.GetMembers())
        {
            if (member.IsStatic)
            {
                continue;
            }

            if (
                HasNamedAttribute(
                    member,
                    ignoreDataMemberAttr,
                    "IgnoreDataMemberAttribute",
                    "IgnoreDataMember"
                ) || HasNamedAttribute(member, jsonIgnoreAttr, "JsonIgnoreAttribute", "JsonIgnore")
            )
            {
                continue;
            }

            ITypeSymbol? memberType = member switch
            {
                IPropertySymbol p
                    when includeNonPublic
                        || (
                            p.DeclaredAccessibility == Accessibility.Public
                            && p.GetMethod is not null
                        ) => p.Type,
                IFieldSymbol f
                    when includeNonPublic || f.DeclaredAccessibility == Accessibility.Public =>
                    f.Type,
                _ => null,
            };
            if (memberType is null)
            {
                continue;
            }

            if (
                ContainsNonJsonFrameworkGraph(
                    memberType,
                    dataContractAttr,
                    ignoreDataMemberAttr,
                    jsonIgnoreAttr,
                    cache,
                    visiting
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonJsonFrameworkType(ITypeSymbol type)
    {
        if (IsNonJsonFrameworkTypeName(type.Name))
        {
            return true;
        }

        if (type is IErrorTypeSymbol)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (IsNonJsonFrameworkTypeName(current.Name))
            {
                return true;
            }
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var iface in named.AllInterfaces)
            {
                if (IsNonJsonFrameworkTypeName(iface.Name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNonJsonFrameworkTypeName(string name) =>
        name
            is "IFormFile"
                or "IFormFileCollection"
                or "FormFile"
                or "FormFileCollection"
                or "Stream"
                or "PipeReader"
                or "PipeWriter"
                or "HttpContext"
                or "HttpRequest"
                or "HttpResponse";

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
        else if (
            type.BaseType is { SpecialType: not SpecialType.System_Object } baseType2
            && HasNamedAttribute(
                baseType2.OriginalDefinition,
                dataContractAttr,
                "DataContractAttribute",
                "DataContract"
            )
        )
        {
            // Closed construction of a DataContract open generic base.
            enqueue(baseType2);
        }
    }

    private static bool IsSupportedCollection(INamedTypeSymbol named, out ITypeSymbol element)
    {
        element = null!;
        if (named is IErrorTypeSymbol)
        {
            return false;
        }

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

        if (IsKnownCollectionDefinition(named.OriginalDefinition))
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
        if (named is IErrorTypeSymbol)
        {
            return false;
        }

        if (!named.IsGenericType || named.TypeArguments.Length != 2)
        {
            return false;
        }

        if (IsKnownDictionaryDefinition(named.OriginalDefinition))
        {
            key = named.TypeArguments[0];
            value = named.TypeArguments[1];
            return true;
        }

        foreach (var iface in named.AllInterfaces)
        {
            if (
                iface.TypeArguments.Length == 2
                && IsKnownDictionaryDefinition(iface.OriginalDefinition)
            )
            {
                key = iface.TypeArguments[0];
                value = iface.TypeArguments[1];
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol StripNrt(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.None);

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

    private static ImmutableArray<AttributeData> TryGetAttributes(ISymbol symbol)
    {
        try
        {
            return symbol.GetAttributes();
        }
        catch (NullReferenceException)
        {
            // Mixed task-host / consumer BCL refs can NRE inside AttributeUsage decode.
            return ImmutableArray<AttributeData>.Empty;
        }
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType) =>
        TryGetAttributes(symbol)
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

        foreach (var attribute in TryGetAttributes(symbol))
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
