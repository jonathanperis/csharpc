using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// The shadow corelib (docs/10-corelib.md): BCL types re-implemented in C# under
/// <c>src/Csharpc/corelib/</c>, compiled together with the user's program.
///
/// User code (and corelib code itself) binds against the *real* .NET reference assemblies, so
/// Roslyn's overload resolution, nullability and diagnostics are exact. A corelib type declares the
/// library type it implements with <c>[Shadows("System.Collections.Generic.List`1")]</c>; codegen
/// then redirects every library symbol to its shadow:
///
///   System.Collections.Generic.List&lt;int&gt;        -> CsharpcCoreLib.ListImpl&lt;int&gt;
///   List&lt;int&gt;.Add(int)                         -> ListImpl&lt;int&gt;.Add(int)
///   Enumerable.Where&lt;string&gt;(IEnumerable, Func) -> CsharpcCoreLib.EnumerableImpl.Where&lt;string&gt;(...)
///
/// Members are matched by name, generic arity and a canonical signature in which shadow types are
/// spelled with their real names, so a shadow member may mention either the real or the shadow
/// type. Type arguments are never rewritten: only the generic definition is swapped, which keeps
/// one canonical instantiation per real type (List&lt;List&lt;int&gt;&gt; -> ListImpl&lt;List&lt;int&gt;&gt;).
/// </summary>
public sealed class CoreLib
{
    public const string Namespace = "CsharpcCoreLib";
    public const string TreePrefix = "<corelib>/";

    private readonly Compilation _compilation;
    private readonly Dictionary<string, INamedTypeSymbol> _shadowByReal = [];
    private readonly Dictionary<INamedTypeSymbol, string> _realByShadow = new(SymbolEqualityComparer.Default);
    /// <summary>[ShadowMembers]: extra static members for library types we don't replace (e.g. string.Join&lt;T&gt;).</summary>
    private readonly Dictionary<string, List<INamedTypeSymbol>> _memberShadows = [];
    private readonly Dictionary<ISymbol, ISymbol> _cache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(ISymbol Real, ISymbol Shadow), ISymbol?> _definitionMatch = [];

    /// <summary>CsharpcCoreLib.CoreIntrinsics: extern helpers whose bodies codegen synthesizes per instantiation.</summary>
    public INamedTypeSymbol? IntrinsicsType { get; }

    public CoreLib(Compilation compilation)
    {
        _compilation = compilation;
        var ns = compilation.Assembly.GlobalNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == Namespace);
        if (ns is null) return;
        Scan(ns);
        IntrinsicsType = ns.GetTypeMembers("CoreIntrinsics").FirstOrDefault();
    }

    private void Scan(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns) { Scan(ns); continue; }
            if (member is not INamedTypeSymbol type) continue;
            foreach (var attr in type.GetAttributes())
            {
                if (attr.ConstructorArguments.FirstOrDefault().Value is not string real) continue;
                if (attr.AttributeClass?.Name == "ShadowsAttribute")
                {
                    _shadowByReal[real] = type;
                    _realByShadow[type] = real;
                }
                else if (attr.AttributeClass?.Name == "ShadowMembersAttribute")
                {
                    if (!_memberShadows.TryGetValue(real, out var list)) _memberShadows[real] = list = [];
                    list.Add(type);
                }
            }
            Scan(type);
        }
    }

    public bool HasShadows => _shadowByReal.Count > 0 || _memberShadows.Count > 0;

    /// <summary>True for any symbol declared in the corelib sources.</summary>
    public static bool IsCoreLib(ISymbol symbol)
    {
        var ns = symbol as INamespaceSymbol ?? symbol.ContainingNamespace;
        while (ns is { ContainingNamespace.IsGlobalNamespace: false }) ns = ns.ContainingNamespace;
        return ns is { Name: Namespace, ContainingNamespace.IsGlobalNamespace: true };
    }

    /// <summary>"System.Collections.Generic.List`1" for a shadow definition, else null.</summary>
    public string? RealNameOf(INamedTypeSymbol type) => _realByShadow.GetValueOrDefault(type.OriginalDefinition);

    private bool IsLibrary(ISymbol symbol) =>
        !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly);

    // ======================================================================================
    // Types
    // ======================================================================================

    public ITypeSymbol Map(ITypeSymbol type) => type is INamedTypeSymbol named && TryMap(named) is { } mapped ? mapped : type;

    public INamedTypeSymbol? TryMap(INamedTypeSymbol type)
    {
        if (!HasShadows || !IsLibrary(type)) return null;
        if (_cache.TryGetValue(type, out var cached)) return (INamedTypeSymbol)cached;
        var definition = type.OriginalDefinition;
        if (!_shadowByReal.TryGetValue(Mangler.MetadataName(definition), out var shadow)) return null;

        INamedTypeSymbol result;
        if (type.ContainingType is { } outer && TryMap(outer) is { } mappedOuter)
        {
            var nested = mappedOuter.GetTypeMembers(shadow.Name, shadow.Arity).FirstOrDefault()
                ?? throw CompileError.Unsupported($"corelib: nested shadow '{shadow.Name}' not found in '{mappedOuter}'", shadow);
            result = type.TypeArguments.Length > 0 ? nested.Construct([.. type.TypeArguments]) : nested;
        }
        else
        {
            result = shadow.Arity > 0 && !type.IsDefinition ? shadow.Construct([.. type.TypeArguments]) : shadow;
        }
        _cache[type] = result;
        return result;
    }

    // ======================================================================================
    // Members
    // ======================================================================================

    public IMethodSymbol Map(IMethodSymbol method)
    {
        if (!HasShadows || !IsLibrary(method)) return method;
        if (_cache.TryGetValue(method, out var cached)) return (IMethodSymbol)cached;
        var result = method;
        if (method.ContainingType is { } type && TryMap(type) is { } shadowType
            && MatchDefinition(method.OriginalDefinition, shadowType.OriginalDefinition) is IMethodSymbol match)
        {
            result = InConstructed(match, shadowType);
            if (method.IsGenericMethod && !method.IsDefinition) result = result.Construct([.. method.TypeArguments]);
        }
        else if (method.IsStatic && method.ContainingType is { } owner
                 && _memberShadows.TryGetValue(Mangler.MetadataName(owner.OriginalDefinition), out var extensions))
        {
            foreach (var extension in extensions)
                if (MatchDefinition(method.OriginalDefinition, extension) is IMethodSymbol extra)
                {
                    result = method.IsGenericMethod && !method.IsDefinition ? extra.Construct([.. method.TypeArguments]) : extra;
                    break;
                }
        }
        _cache[method] = result;
        return result;
    }

    public IPropertySymbol Map(IPropertySymbol property)
    {
        if (!HasShadows || !IsLibrary(property)) return property;
        if (_cache.TryGetValue(property, out var cached)) return (IPropertySymbol)cached;
        var result = property;
        if (property.ContainingType is { } type && TryMap(type) is { } shadowType
            && MatchDefinition(property.OriginalDefinition, shadowType.OriginalDefinition) is IPropertySymbol match)
            result = InConstructed(match, shadowType);
        _cache[property] = result;
        return result;
    }

    public IFieldSymbol Map(IFieldSymbol field)
    {
        if (!HasShadows || !IsLibrary(field)) return field;
        if (_cache.TryGetValue(field, out var cached)) return (IFieldSymbol)cached;
        var result = field;
        if (field.ContainingType is { } type && TryMap(type) is { } shadowType
            && shadowType.GetMembers(field.Name).OfType<IFieldSymbol>().FirstOrDefault() is { } match)
            result = match;
        _cache[field] = result;
        return result;
    }

    private static T InConstructed<T>(T definitionMember, INamedTypeSymbol constructedType) where T : class, ISymbol =>
        constructedType.IsDefinition
            ? definitionMember
            : constructedType.GetMembers(definitionMember.Name).OfType<T>()
                .First(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, definitionMember));

    private ISymbol? MatchDefinition(ISymbol real, INamedTypeSymbol shadow)
    {
        if (_definitionMatch.TryGetValue((real, shadow), out var known)) return known;
        var key = Signature(real);
        var name = real.MetadataName;
        var match = shadow.GetMembers().FirstOrDefault(m => m.Kind == real.Kind && m.MetadataName == name
            && m.DeclaredAccessibility != Accessibility.Private && Signature(m) == key);
        return _definitionMatch[(real, shadow)] = match;
    }

    // ======================================================================================
    // Canonical signatures (shadow types spelled with their real names)
    // ======================================================================================

    private string Signature(ISymbol member) => member switch
    {
        IMethodSymbol m => $"{(m.IsStatic ? "static " : "")}{m.MetadataName}`{m.Arity}({string.Join(",", m.Parameters.Select(Param))})",
        IPropertySymbol p => $"{(p.IsStatic ? "static " : "")}{p.MetadataName}[{string.Join(",", p.Parameters.Select(Param))}]",
        _ => member.MetadataName,
    };

    private string Param(IParameterSymbol p) => (p.RefKind == RefKind.None ? "" : p.RefKind + " ") + Canon(p.Type);

    private string Canon(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } tp => "!!" + tp.Ordinal,
        ITypeParameterSymbol tp => "!" + (tp.ContainingType is { } owner ? FullName(owner) : "") + ":" + tp.Ordinal,
        IArrayTypeSymbol a => Canon(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]",
        IPointerTypeSymbol p => Canon(p.PointedAtType) + "*",
        INamedTypeSymbol n => FullName(n) + (AllTypeArguments(n) is { Count: > 0 } args ? "<" + string.Join(",", args.Select(Canon)) + ">" : ""),
        _ => type.ToDisplayString(),
    };

    private string FullName(INamedTypeSymbol type) => RealNameOf(type) ?? Mangler.MetadataName(type.OriginalDefinition);

    private static List<ITypeSymbol> AllTypeArguments(INamedTypeSymbol type)
    {
        List<ITypeSymbol> args = [];
        for (var t = type; t is not null; t = t.ContainingType) args.InsertRange(0, t.TypeArguments);
        return args;
    }
}
