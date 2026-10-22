using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    // Shadow declarations are discovered from syntax (no binding: hello world must not pay for the
    // corelib) and resolved to symbols lazily, by metadata name.
    private readonly Dictionary<string, string> _shadowNameByReal = [];      // real -> shadow metadata name
    private readonly Dictionary<string, string> _realByShadowName = [];      // shadow metadata name -> real
    private readonly Dictionary<string, List<string>> _memberShadowNames = []; // real -> [ShadowMembers] classes
    private readonly Dictionary<string, INamedTypeSymbol?> _resolved = [];
    private readonly Dictionary<ISymbol, ISymbol> _cache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(ISymbol Real, ISymbol Shadow), ISymbol?> _definitionMatch = [];
    private INamedTypeSymbol? _intrinsics;
    private bool _intrinsicsResolved;

    /// <summary>CsharpcCoreLib.CoreIntrinsics: extern helpers whose bodies codegen synthesizes per instantiation.</summary>
    public INamedTypeSymbol? IntrinsicsType
    {
        get
        {
            if (!_intrinsicsResolved) { _intrinsics = Resolve(Namespace + ".CoreIntrinsics"); _intrinsicsResolved = true; }
            return _intrinsics;
        }
    }

    public CoreLib(Compilation compilation)
    {
        _compilation = compilation;
        foreach (var tree in compilation.SyntaxTrees)
            if (tree.FilePath.StartsWith(TreePrefix, StringComparison.Ordinal))
                Scan(tree.GetRoot(), "");
    }

    private void Scan(SyntaxNode node, string prefix)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    Scan(ns, prefix + ns.Name + ".");
                    break;
                case BaseTypeDeclarationSyntax type:
                {
                    var arity = type is TypeDeclarationSyntax { TypeParameterList: { } tps } ? tps.Parameters.Count : 0;
                    var metadataName = prefix + type.Identifier.ValueText + (arity > 0 ? "`" + arity : "");
                    foreach (var attribute in type.AttributeLists.SelectMany(l => l.Attributes))
                    {
                        var name = attribute.Name.ToString();
                        if (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax { Token.Value: string real })
                            continue;
                        if (name is "Shadows" or "ShadowsAttribute")
                        {
                            _shadowNameByReal[real] = metadataName;
                            _realByShadowName[metadataName] = real;
                        }
                        else if (name is "ShadowMembers" or "ShadowMembersAttribute")
                        {
                            if (!_memberShadowNames.TryGetValue(real, out var list)) _memberShadowNames[real] = list = [];
                            list.Add(metadataName);
                        }
                    }
                    if (type is TypeDeclarationSyntax nested) Scan(nested, metadataName + "+");
                    break;
                }
            }
        }
    }

    private INamedTypeSymbol? Resolve(string metadataName)
    {
        if (_resolved.TryGetValue(metadataName, out var symbol)) return symbol;
        return _resolved[metadataName] = _compilation.GetTypeByMetadataName(metadataName);
    }

    public bool HasShadows => _shadowNameByReal.Count > 0 || _memberShadowNames.Count > 0;

    /// <summary>True for any symbol declared in the corelib sources.</summary>
    public static bool IsCoreLib(ISymbol symbol)
    {
        var ns = symbol as INamespaceSymbol ?? symbol.ContainingNamespace;
        while (ns is { ContainingNamespace.IsGlobalNamespace: false }) ns = ns.ContainingNamespace;
        return ns is { Name: Namespace, ContainingNamespace.IsGlobalNamespace: true };
    }

    /// <summary>"System.Collections.Generic.List`1" for a shadow definition, else null.</summary>
    public string? RealNameOf(INamedTypeSymbol type) =>
        _realByShadowName.Count == 0 || !IsCoreLib(type) ? null : _realByShadowName.GetValueOrDefault(PlainMetadataName(type.OriginalDefinition));

    private static string PlainMetadataName(INamedTypeSymbol type) =>
        type.ContainingType is { } outer
            ? PlainMetadataName(outer) + "+" + type.MetadataName
            : type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.MetadataName : type.MetadataName;

    private bool IsLibrary(ISymbol symbol) =>
        !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly);

    // ======================================================================================
    // Types
    // ======================================================================================

    public ITypeSymbol Map(ITypeSymbol type) => type is INamedTypeSymbol named && TryMap(named) is { } mapped ? mapped : type;

    /// <summary>
    /// The inverse of <see cref="TryMap"/>, recursively: shadow types (anywhere in the type, e.g. as a type
    /// argument) become their real BCL types. Used to give types one canonical identity — e.g. an
    /// iterator implementing IAsyncEnumerable&lt;ListImpl&lt;int&gt;&gt; must match IAsyncEnumerable&lt;List&lt;int&gt;&gt;.
    /// </summary>
    public ITypeSymbol Unmap(ITypeSymbol type)
    {
        if (!HasShadows) return type;
        switch (type)
        {
            case IArrayTypeSymbol array:
            {
                var element = Unmap(array.ElementType);
                return ReferenceEquals(element, array.ElementType) ? array : _compilation.CreateArrayTypeSymbol(element, array.Rank);
            }
            case INamedTypeSymbol named:
            {
                var args = named.TypeArguments.Select(Unmap).ToArray();
                var definition = named.OriginalDefinition;
                var rawName = definition.ContainingNamespace is { IsGlobalNamespace: false } ns
                    ? ns.ToDisplayString() + "." + definition.MetadataName : definition.MetadataName;
                if (named.ContainingType is null && _realByShadowName.TryGetValue(rawName, out var realName)
                    && _compilation.GetTypeByMetadataName(realName) is { } real && real.Arity == definition.Arity)
                    return args.Length > 0 ? real.Construct(args) : real;
                if (args.Length > 0 && args.Where((a, i) => !ReferenceEquals(a, named.TypeArguments[i])).Any()
                    && named.ContainingType is null)
                    return definition.Construct(args);
                return named;
            }
            default:
                return type;
        }
    }

    public INamedTypeSymbol? TryMap(INamedTypeSymbol type)
    {
        if (!HasShadows || !IsLibrary(type)) return null;
        if (_cache.TryGetValue(type, out var cached)) return (INamedTypeSymbol)cached;
        var definition = type.OriginalDefinition;
        if (!_shadowNameByReal.TryGetValue(Mangler.MetadataName(definition), out var shadowName) || Resolve(shadowName) is not { } shadow)
            return null;

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
        // Hand-written intrinsics (src/runtime/*.c, LLVM intrinsics) take priority over C# shadows.
        if (Intrinsics.TryGet(method, out _) || (method.MethodKind == MethodKind.Constructor && Intrinsics.TryGetConstructor(method, out _)))
        { _cache[method] = method; return method; }
        if (method.ContainingType is { } type && TryMap(type) is { } shadowType
            && MatchDefinition(method.OriginalDefinition, shadowType.OriginalDefinition) is IMethodSymbol match)
        {
            result = InConstructed(match, shadowType);
            if (method.IsGenericMethod && !method.IsDefinition) result = result.Construct([.. method.TypeArguments]);
        }
        // An override the shadow doesn't redeclare (UTF8Encoding.GetPreamble): bind to the base member; the
        // call still dispatches virtually to whatever the shadow hierarchy overrides.
        else if (method.OverriddenMethod is { } overridden && method.ContainingType is { } derived && TryMap(derived) is not null)
            result = Map(overridden);
        else if (method.IsStatic && method.ContainingType is { } owner
                 && _memberShadowNames.TryGetValue(Mangler.MetadataName(owner.OriginalDefinition), out var extensions))
        {
            foreach (var extensionName in extensions)
                if (Resolve(extensionName) is { } extension && MatchDefinition(method.OriginalDefinition, extension) is IMethodSymbol extra)
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
        if (Intrinsics.TryGetProperty(property, out _)) { _cache[property] = property; return property; }
        if (property.ContainingType is { } type && TryMap(type) is { } shadowType
            && MatchDefinition(property.OriginalDefinition, shadowType.OriginalDefinition) is IPropertySymbol match)
            result = InConstructed(match, shadowType);
        else if (property.IsStatic && property.ContainingType is { } owner
                 && _memberShadowNames.TryGetValue(Mangler.MetadataName(owner.OriginalDefinition), out var extensions))
        {
            // [ShadowMembers] classes can supply static properties too (Console.Out, Environment.CurrentDirectory).
            foreach (var extensionName in extensions)
                if (Resolve(extensionName) is { } extension && MatchDefinition(property.OriginalDefinition, extension) is IPropertySymbol extra)
                {
                    result = extra;
                    break;
                }
        }
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
