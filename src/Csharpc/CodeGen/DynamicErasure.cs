using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// `dynamic` is `object` at run time: List&lt;dynamic&gt; and List&lt;object&gt; are one type on .NET (same Type,
/// same statics, `is` either). Roslyn keeps them apart, so every symbol codegen reads (CoreLib.Map) is erased
/// to its object form first, giving them one layout, method table and set of functions.
/// </summary>
internal static class DynamicErasure
{
    public static bool Mentions(ITypeSymbol type) => type switch
    {
        IDynamicTypeSymbol => true,
        IArrayTypeSymbol a => Mentions(a.ElementType),
        INamedTypeSymbol { IsGenericType: true } n => AnyMentions(n.TypeArguments) || (n.ContainingType is { } c && Mentions(c)),
        INamedTypeSymbol { ContainingType: { } outer } => Mentions(outer),
        IPointerTypeSymbol p => Mentions(p.PointedAtType),
        _ => false,
    };

    private static bool AnyMentions(System.Collections.Immutable.ImmutableArray<ITypeSymbol> types)
    {
        foreach (var t in types) if (Mentions(t)) return true;
        return false;
    }

    public static ITypeSymbol Erase(ITypeSymbol type, Compilation compilation)
    {
        if (!Mentions(type)) return type;
        switch (type)
        {
            case IDynamicTypeSymbol:
                return compilation.GetSpecialType(SpecialType.System_Object);
            case IArrayTypeSymbol array:
                return compilation.CreateArrayTypeSymbol(Erase(array.ElementType, compilation), array.Rank);
            case IPointerTypeSymbol pointer:
                return compilation.CreatePointerTypeSymbol(Erase(pointer.PointedAtType, compilation));
            case INamedTypeSymbol named:
            {
                var definition = named.OriginalDefinition;
                if (named.ContainingType is { } outer && Mentions(outer))
                {
                    var erasedOuter = (INamedTypeSymbol)Erase(outer, compilation);
                    definition = erasedOuter.GetTypeMembers(named.Name, named.Arity).First();
                }
                if (named.TypeArguments.Length == 0) return definition;
                return definition.Construct([.. named.TypeArguments.Select(a => Erase(a, compilation))]);
            }
            default:
                return type;
        }
    }

    public static bool Mentions(IMethodSymbol method) =>
        (method.ContainingType is { } t && Mentions(t)) || (method.IsGenericMethod && AnyMentions(method.TypeArguments));

    public static IMethodSymbol Erase(IMethodSymbol method, Compilation compilation)
    {
        if (!Mentions(method)) return method;
        var result = method;
        if (method.ContainingType is { } owner && Mentions(owner))
        {
            var erasedOwner = (INamedTypeSymbol)Erase(owner, compilation);
            var definition = method.IsGenericMethod ? method.ConstructedFrom.OriginalDefinition : method.OriginalDefinition;
            result = erasedOwner.GetMembers(method.Name).OfType<IMethodSymbol>()
                .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, definition)) ?? method;
        }
        if (method.IsGenericMethod && !method.IsDefinition)
        {
            var arguments = method.TypeArguments.Select(a => Erase(a, compilation)).ToArray();
            result = (ReferenceEquals(result, method) ? method.ConstructedFrom : result.ConstructedFrom).Construct(arguments);
        }
        return result;
    }

    public static T EraseMember<T>(T member, Compilation compilation) where T : class, ISymbol
    {
        if (member.ContainingType is not { } owner || !Mentions(owner)) return member;
        var erasedOwner = (INamedTypeSymbol)Erase(owner, compilation);
        return erasedOwner.GetMembers(member.Name).OfType<T>()
            .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, member.OriginalDefinition)) ?? member;
    }
}
