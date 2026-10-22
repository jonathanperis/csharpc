using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Generics are compiled by monomorphization (like Rust and C++ templates): every distinct
/// instantiation (Box&lt;int&gt;, Box&lt;string&gt;, Max&lt;double&gt;) becomes its own LLVM function / layout /
/// method table. Roslyn only gives us the CFG of the generic *definition*, whose types mention
/// type parameters, so lowering an instantiation applies a <see cref="TypeSubstitution"/> to every
/// type and symbol it reads (docs/03-lowering.md#generics).
/// </summary>
public sealed class TypeSubstitution
{
    private readonly Dictionary<ITypeParameterSymbol, ITypeSymbol> _map = new(SymbolEqualityComparer.Default);
    private readonly Compilation _compilation;

    private TypeSubstitution(Compilation compilation) => _compilation = compilation;

    public static readonly TypeSubstitution Identity = new(null!);

    public bool IsIdentity => _map.Count == 0;

    /// <summary>Type parameter -> argument map for a constructed method (its own and all containing types' arguments).</summary>
    public static TypeSubstitution For(IMethodSymbol? method, Compilation compilation)
    {
        if (method is null) return Identity;
        var s = new TypeSubstitution(compilation);
        if (method.IsGenericMethod)
            Add(s, method.OriginalDefinition.TypeParameters, method.TypeArguments);
        for (var t = method.ContainingType; t is not null; t = t.ContainingType)
            if (t.IsGenericType)
                Add(s, t.OriginalDefinition.TypeParameters, t.TypeArguments);
        return s._map.Count == 0 ? Identity : s;
    }

    private static void Add(TypeSubstitution s, IReadOnlyList<ITypeParameterSymbol> parameters, IReadOnlyList<ITypeSymbol> arguments)
    {
        for (var i = 0; i < parameters.Count; i++)
            if (!SymbolEqualityComparer.Default.Equals(parameters[i], arguments[i]))
                s._map[parameters[i]] = arguments[i];
    }

    public string Key => string.Join(",", _map.Select(kv => kv.Key.Name + "=" + kv.Value.ToDisplayString()));

    public ITypeSymbol Apply(ITypeSymbol type)
    {
        if (IsIdentity) return type;
        switch (type)
        {
            case ITypeParameterSymbol tp:
                return _map.TryGetValue(tp, out var arg) ? arg : _map.TryGetValue((ITypeParameterSymbol)tp.OriginalDefinition, out arg) ? arg : tp;
            case IArrayTypeSymbol array:
            {
                var element = Apply(array.ElementType);
                return SymbolEqualityComparer.Default.Equals(element, array.ElementType)
                    ? array
                    : _compilation.CreateArrayTypeSymbol(element, array.Rank);
            }
            case INamedTypeSymbol named when named.IsGenericType || named.ContainingType is not null:
            {
                var container = named.ContainingType is { } ct ? (INamedTypeSymbol)Apply(ct) : null;
                var args = named.TypeArguments.Select(Apply).ToArray();
                var containerChanged = container is not null && !SymbolEqualityComparer.Default.Equals(container, named.ContainingType);
                var argsChanged = !args.SequenceEqual(named.TypeArguments, SymbolEqualityComparer.Default);
                if (!containerChanged && !argsChanged) return named;
                var definition = containerChanged
                    ? container!.GetTypeMembers(named.Name, named.Arity).First()
                    : named.OriginalDefinition;
                return args.Length > 0 ? definition.Construct(args) : definition;
            }
            default:
                return type;
        }
    }

    public IMethodSymbol Apply(IMethodSymbol method)
    {
        if (IsIdentity) return method;
        var result = method;
        if (method.ContainingType is { } ct && Apply(ct) is INamedTypeSymbol newType && !SymbolEqualityComparer.Default.Equals(newType, ct))
        {
            var definition = method.OriginalDefinition;
            result = newType.GetMembers(method.Name).OfType<IMethodSymbol>()
                .First(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, definition));
        }
        if (method.IsGenericMethod)
        {
            var args = method.TypeArguments.Select(Apply).ToArray();
            if (!ReferenceEquals(result, method) || !args.SequenceEqual(method.TypeArguments, SymbolEqualityComparer.Default))
                result = result.ConstructedFrom.Construct(args);
        }
        return result;
    }

    public IFieldSymbol Apply(IFieldSymbol field)
    {
        if (IsIdentity || field.ContainingType is not { } ct || Apply(ct) is not INamedTypeSymbol newType
            || SymbolEqualityComparer.Default.Equals(newType, ct)) return field;
        var definition = field.OriginalDefinition;
        return newType.GetMembers(field.Name).OfType<IFieldSymbol>()
            .First(f => SymbolEqualityComparer.Default.Equals(f.OriginalDefinition, definition));
    }

    public IPropertySymbol Apply(IPropertySymbol property)
    {
        if (IsIdentity || property.ContainingType is not { } ct || Apply(ct) is not INamedTypeSymbol newType
            || SymbolEqualityComparer.Default.Equals(newType, ct)) return property;
        var definition = property.OriginalDefinition;
        return newType.GetMembers(property.Name).OfType<IPropertySymbol>()
            .First(p => SymbolEqualityComparer.Default.Equals(p.OriginalDefinition, definition));
    }

    /// <summary>True if any type parameter remains (an open instantiation we can't compile).</summary>
    public static bool IsOpen(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol a => IsOpen(a.ElementType),
        INamedTypeSymbol n => n.TypeArguments.Any(IsOpen) || (n.ContainingType is { } c && IsOpen(c)),
        _ => false,
    };

    public static bool IsOpen(IMethodSymbol method) =>
        method.TypeArguments.Any(IsOpen) || (method.ContainingType is { } c && IsOpen(c));

    /// <summary>Methods whose code depends on type arguments (generic method, or member of a generic type).</summary>
    public static bool IsGenericContext(IMethodSymbol method) =>
        method.IsGenericMethod || (method.ContainingType is { } t && IsGenericContext(t));

    public static bool IsGenericContext(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
            if (t.IsGenericType) return true;
        return false;
    }
}

public sealed partial class FunctionCodeGen
{
    private readonly TypeSubstitution _subst = TypeSubstitution.Identity;

    internal TypeSubstitution Substitution => _subst;

    internal ITypeSymbol S(ITypeSymbol type) => _subst.Apply(type);
    internal IMethodSymbol S(IMethodSymbol method) => _subst.Apply(method);
    internal IFieldSymbol S(IFieldSymbol field) => _subst.Apply(field);
    internal IPropertySymbol S(IPropertySymbol property) => _subst.Apply(property);

    /// <summary>The (substituted) static type of an operation.</summary>
    internal ITypeSymbol? TypeOf(IOperation? op) => op?.Type is { } t ? _subst.Apply(t) : null;
}
