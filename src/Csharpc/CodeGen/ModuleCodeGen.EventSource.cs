using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// EventSource.Write&lt;T&gt;'s payload graph. EventSource itself is the framework's (CoreLib's IL, ADR 0023): its manifest is built by
/// reflection over the event source class, whose members the annotation on EventSource keeps (ModuleCodeGen.NoteAnnotatedHierarchy,
/// ModuleCodeGen.NestedTypes.cs); a self-describing payload is read through the public properties of T, kept by the annotation on
/// Write&lt;T&gt;, and of the types those properties return, kept here as ILLink's analysis of TraceLogging's type walk keeps them.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>EventSource.Write&lt;T&gt; (the framework's, from CoreLib's IL): a payload read by reflection.</summary>
    private static bool IsEventSourceWrite(IMethodSymbol method) =>
        method.Name == "Write" && method.ContainingType is { Name: "EventSource" } owner
        && Frontend.UpstreamSlice.RealNamespace(owner.ContainingNamespace?.ToDisplayString() ?? "") == "System.Diagnostics.Tracing";

    /// <summary>
    /// The metadata a Write&lt;T&gt; payload needs beyond T's own properties (which the annotation keeps): the public
    /// properties of the types those properties return, through nullable, array and nested payload types and enumerables' elements.
    /// Types of the framework (Guid, DateTime, ...) are scalars the corelib knows without metadata.
    /// </summary>
    private void MarkEventPayloadGraph(ITypeSymbol type)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<ITypeSymbol>();
        pending.Push(type);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            // An enumerable's elements are payloads too (TraceLogging's EnumerableTypeInfo): a Dictionary's KeyValuePair, whose Key and Value
            // TraceLogging reads as properties.
            if (current is INamedTypeSymbol enumerable)
                foreach (var itf in enumerable.AllInterfaces.Prepend(enumerable))
                    if (itf is { Name: "IEnumerable", TypeArguments: [var element] } && itf.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
                    {
                        if (element is INamedTypeSymbol { Name: "KeyValuePair", TypeArguments.Length: 2 } pair
                            && pair.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
                        {
                            MarkDynamicallyAccessed(pair, DamPublicProperties);
                            foreach (var argument in pair.TypeArguments) pending.Push(argument);
                        }
                        else
                        {
                            if (element is INamedTypeSymbol { TypeKind: not TypeKind.Enum, SpecialType: SpecialType.None } elementType
                                && elementType.ContainingNamespace?.ToDisplayString() != "System"
                                && !(elementType.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("System.", StringComparison.Ordinal))
                                MarkDynamicallyAccessed(elementType, DamPublicProperties);
                            pending.Push(element);
                        }
                    }
            for (var t = current as INamedTypeSymbol; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var property in t.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public) continue;
                    var propertyType = property.Type;
                    while (true)
                    {
                        if (propertyType is IArrayTypeSymbol array) propertyType = array.ElementType;
                        else if (propertyType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) propertyType = nullable.TypeArguments[0];
                        else break;
                    }
                    if (propertyType is not INamedTypeSymbol named || named.TypeKind is TypeKind.Enum || named.SpecialType != SpecialType.None
                        || named.ContainingNamespace?.ToDisplayString() is { } ns && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)))
                        continue;
                    MarkDynamicallyAccessed(named, DamPublicProperties);
                    pending.Push(named);
                }
        }
    }
}
