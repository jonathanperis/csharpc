using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Shared generic code (ADR 0026), step 2a/2b: canonical forms and the generic dictionary each canonical body would need. Nothing here
/// changes code generation yet; the report (CSHARPC_GENERIC_REPORT) prints what it finds so the dictionaries' cost is known before
/// they are emitted.
/// </summary>
internal sealed class SharedGenerics(Compilation compilation, IlAssemblySet? il)
{
    /// <summary>CsharpcCoreLib.__Canon: what every reference-type type argument becomes in a canonical form.</summary>
    public INamedTypeSymbol? Canon { get; } = compilation.GetTypeByMetadataName(CoreLib.Namespace + ".__Canon");

    /// <summary>The canonical form of a type: reference types are __Canon, generic value types canonicalize their arguments.</summary>
    public ITypeSymbol Canonicalize(ITypeSymbol type)
    {
        if (Canon is null || type is ITypeParameterSymbol) return type;
        if (type.IsReferenceType) return Canon;
        return type is INamedTypeSymbol { IsGenericType: true } named ? CanonicalizeArguments(named) : type;
    }

    private INamedTypeSymbol CanonicalizeArguments(INamedTypeSymbol type)
    {
        var outer = type.ContainingType is { } containing ? CanonicalizeArguments(containing) : null;
        var definition = outer is null ? type.OriginalDefinition
            : outer.GetTypeMembers(type.Name, type.Arity).First(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, type.OriginalDefinition));
        return type.Arity == 0 ? definition : definition.Construct([.. type.TypeArguments.Select(Canonicalize)]);
    }

    /// <summary>
    /// The canonical form of a method (its containing types and its own type arguments canonicalized), or the method itself when it is
    /// not a generic instantiation. Reference-type arguments only: a form with no __Canon is its own exact code.
    /// </summary>
    public IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        if (Canon is null) return method;
        var owner = method.ContainingType is { IsGenericType: true } or { ContainingType: not null } ? CanonicalizeArguments(method.ContainingType) : method.ContainingType;
        var inOwner = SymbolEqualityComparer.Default.Equals(owner, method.ContainingType) ? method
            : owner.GetMembers(method.Name).OfType<IMethodSymbol>().First(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, method.OriginalDefinition));
        return method.IsGenericMethod ? inOwner.ConstructedFrom.Construct([.. method.TypeArguments.Select(Canonicalize)]) : inOwner;
    }

    /// <summary>Is this class (or a containing class) a generic instantiation whose canonical form is shared code?</summary>
    public bool IsSharedType(INamedTypeSymbol type) =>
        Canon is not null && type.TypeKind == TypeKind.Class && (type.IsGenericType || type.ContainingType is not null) && MentionsCanon(CanonicalizeArguments(type));

    /// <summary>Is this method's canonical form shared code, i.e. does it stand for more than its own exact instantiation?</summary>
    public bool IsShared(IMethodSymbol canonical) => Canon is not null && MentionsCanon(canonical);

    private bool MentionsCanon(IMethodSymbol method) =>
        method.TypeArguments.Any(MentionsCanon) || (method.ContainingType is { } owner && MentionsCanon(owner));

    private bool MentionsCanon(ITypeSymbol type) => type switch
    {
        _ when SymbolEqualityComparer.Default.Equals(type, Canon) => true,
        INamedTypeSymbol named => named.TypeArguments.Any(MentionsCanon) || (named.ContainingType is { } outer && MentionsCanon(outer)),
        IArrayTypeSymbol array => MentionsCanon(array.ElementType),
        _ => false,
    };

    private readonly ConcurrentDictionary<IMethodSymbol, int> _slots = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The number of generic-dictionary slots the canonical body of this definition would need (ADR 0026, section 2): the distinct
    /// type-parameter-dependent types, methods and static fields its body names (for `new`, casts, `typeof`, boxing, arrays, statics,
    /// calls into other generic code). -1 when the body cannot be read (no IL, no source).
    /// </summary>
    public int DictionarySlots(IMethodSymbol method) => _slots.GetOrAdd(method.OriginalDefinition, CountSlots);

    private int CountSlots(IMethodSymbol definition) => SlotKeys(definition)?.Count ?? -1;

    /// <summary>The type-parameter-dependent symbols a definition's body names (null: body not readable).</summary>
    private HashSet<string>? SlotKeys(IMethodSymbol definition)
    {
        if (il?.OwnerOf(definition) is { } owner && owner.Body(definition) is { } body) return IlSlotKeys(owner, definition, body);
        if (definition.DeclaringSyntaxReferences.FirstOrDefault() is { } reference
            && compilation.GetSemanticModel(reference.SyntaxTree).GetOperation(reference.GetSyntax()) is { } operation)
            return SourceSlotKeys(operation);
        return null;
    }

    private readonly ConcurrentDictionary<INamedTypeSymbol, int> _classSlots = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The class-level dictionary of a generic class definition: the union of what its instance methods' bodies need (the slots a shared
    /// instance method reaches through `this`). Static and generic methods get dictionaries of their own (ADR 0026, step 4).
    /// </summary>
    public int ClassDictionarySlots(INamedTypeSymbol definition) => _classSlots.GetOrAdd(definition.OriginalDefinition, d =>
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in d.GetMembers().OfType<IMethodSymbol>())
            if (!method.IsStatic && !method.IsGenericMethod && SlotKeys(method) is { } own) keys.UnionWith(own);
        return keys.Count;
    });

    private static HashSet<string> IlSlotKeys(IlAssembly owner, IMethodSymbol definition, MethodBodyBlock body)
    {
        var context = IlGenericContext.For(definition);
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instruction in IlDecoder.Decode(body.GetILReader()))
        {
            if (instruction.Token == 0 || instruction.OpCode is ILOpCode.Ldstr) continue;
            var handle = MetadataTokens.EntityHandle(instruction.Token);
            ISymbol? symbol;
            try
            {
                symbol = handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    ? owner.ResolveType(instruction.Token, context)
                    : owner.ResolveMember(instruction.Token, context);
            }
            catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { continue; }
            if (symbol is not null && Dependent(symbol, instruction.OpCode)) needed.Add(instruction.OpCode + ":" + symbol.ToDisplayString());
        }
        return needed;
    }

    private static HashSet<string> SourceSlotKeys(IOperation body)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in body.DescendantsAndSelf())
        {
            ISymbol? symbol = op switch
            {
                ITypeOfOperation t => t.TypeOperand,
                IObjectCreationOperation c => c.Type,
                IArrayCreationOperation a => a.Type,
                IIsTypeOperation i => i.TypeOperand,
                IConversionOperation { Operand.Type: ITypeParameterSymbol } c => c.Type,
                IConversionOperation { Type: ITypeParameterSymbol } c => c.Type,
                IInvocationOperation inv => inv.TargetMethod,
                IFieldReferenceOperation { Field.IsStatic: true } f => f.Field,
                IDefaultValueOperation d => d.Type,
                _ => null,
            };
            if (symbol is not null && Dependent(symbol, ILOpCode.Nop)) needed.Add(op.Kind + ":" + symbol.ToDisplayString());
        }
        return needed;
    }

    /// <summary>Does the symbol depend on a type parameter in a way only a dictionary can supply in shared code?</summary>
    private static bool Dependent(ISymbol symbol, ILOpCode opCode) => symbol switch
    {
        ITypeSymbol type => MentionsTypeParameter(type),
        IMethodSymbol method => method.TypeArguments.Any(MentionsTypeParameter)
            // an instance method of the generic type itself is reached through `this`; a static one (or another type's) needs the dictionary
            || (method.ContainingType is { } owner && MentionsTypeParameter(owner) && (method.IsStatic || opCode is ILOpCode.Newobj or ILOpCode.Ldftn)),
        IFieldSymbol field => field.IsStatic && MentionsTypeParameter(field.ContainingType),
        _ => false,
    };

    private static bool MentionsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        INamedTypeSymbol named => named.TypeArguments.Any(MentionsTypeParameter) || (named.ContainingType is { } outer && MentionsTypeParameter(outer)),
        IArrayTypeSymbol array => MentionsTypeParameter(array.ElementType),
        IPointerTypeSymbol pointer => MentionsTypeParameter(pointer.PointedAtType),
        _ => false,
    };
}
