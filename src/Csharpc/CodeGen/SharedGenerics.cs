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
            // Lambdas, local functions (closures and expression trees built over the exact members) and dynamic operations
            // (bound per instantiation) keep a source method exact.
            if (op is IAnonymousFunctionOperation or ILocalFunctionOperation or IDelegateCreationOperation
                || op.Kind is OperationKind.DynamicInvocation or OperationKind.DynamicMemberReference or OperationKind.DynamicObjectCreation
                    or OperationKind.DynamicIndexerAccess)
            {
                needed.Add(op.Kind.ToString());
                continue;
            }
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
        // In shared code every type parameter is a reference type: element loads and stores, initobj, ldobj/stobj and sizeof of T (or of a
        // generic value type over it, whose canonical layout is the same) need no type handle, and boxing a reference type is the identity.
        ITypeSymbol type when opCode is ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Initobj or ILOpCode.Ldobj or ILOpCode.Stobj
            or ILOpCode.Cpobj or ILOpCode.Sizeof => false,
        ITypeSymbol type when opCode is ILOpCode.Box => MentionsTypeParameter(type) && type is not ITypeParameterSymbol && type.IsValueType,
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

    // ======================================================================================
    // Step 3a: which instance methods of shared generic classes are compiled once, as their canonical form
    // ======================================================================================

    /// <summary>
    /// Definitions that stay exact whatever the arguments in step 3a: hot paths whose exact code is worth more than the copies (ADR 0026,
    /// section 7: the comparer family and the hash collections keep their per-instantiation devirtualization).
    /// </summary>
    private static readonly HashSet<string> s_exactDefinitions = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.EqualityComparer`1", "System.Collections.Generic.Comparer`1",
        "CsharpcCoreLib.EqualityComparerImpl`1", "CsharpcCoreLib.DefaultEqualityComparer`1", "CsharpcCoreLib.ComparerImpl`1", "CsharpcCoreLib.DefaultComparer`1",
    };

    private readonly ConcurrentDictionary<IMethodSymbol, string?> _exactReasons = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The canonical form this exact method is compiled as, or null when it is compiled exact (its own instantiation). Shared: a
    /// non-generic instance method of a generic class whose canonical form is shared, whose definition needs nothing a dictionary would
    /// have to supply (step 3a) and whose calls into other generic code reach only methods that are shared too.
    /// </summary>
    public IMethodSymbol? ShareAs(IMethodSymbol method)
    {
        if (Canon is null || ObservesFrames || method.IsStatic || method.IsGenericMethod || method.ContainingType is not { TypeKind: TypeKind.Class } owner
            || !IsSharedType(owner) || ExactReason(method.OriginalDefinition) is not null || !SameSignature(method)) return null;
        return Canonicalize(method);
    }

    /// <summary>
    /// Do the exact method and its canonical form pass and return the same LLVM types? A generic value type over reference types
    /// (KeyValuePair&lt;Exception, Action&gt; as T) is a struct type of its own per instantiation, so such a signature stays exact.
    /// </summary>
    private bool SameSignature(IMethodSymbol method) =>
        method.Parameters.Select(p => p.Type).Append(method.ReturnType).All(t => t.IsReferenceType || t is ITypeParameterSymbol
            || SymbolEqualityComparer.Default.Equals(Canonicalize(t), t));

    /// <summary>Why a definition's instantiations stay exact (null: they are shared). For the report (CSHARPC_GENERIC_REPORT).</summary>
    public string? ExactReason(IMethodSymbol definition)
    {
        definition = definition.OriginalDefinition;
        if (_exactReasons.TryGetValue(definition, out var known)) return known;
        _exactReasons[definition] = null; // a recursive call to itself (or a cycle of shared methods) does not make it exact
        var reason = ComputeExactReason(definition);
        _exactReasons[definition] = reason;
        return reason;
    }

    private string? ComputeExactReason(IMethodSymbol definition)
    {
        if (definition.IsStatic) return "static";
        if (definition.IsGenericMethod) return "generic method";
        if (definition.IsAbstract || definition.IsExtern) return "no body";
        if (definition.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.ExplicitInterfaceImplementation or MethodKind.EventAdd or MethodKind.EventRemove))
            return "method kind " + definition.MethodKind;
        for (var t = definition.ContainingType; t is not null; t = t.ContainingType)
            if (s_exactDefinitions.Contains(MetadataNameOf(t.OriginalDefinition))) return "hot path " + MetadataNameOf(t.OriginalDefinition);
        // The signature must lower identically for every instantiation: T itself and reference types are pointers; a generic value type
        // over T (KeyValuePair<T, int>) has a layout of its own per instantiation.
        foreach (var type in definition.Parameters.Select(p => p.Type).Append(definition.ReturnType))
            if (MentionsTypeParameter(type) && type is not ITypeParameterSymbol && !type.IsReferenceType) return "signature " + type.ToDisplayString();
        if (definition.Parameters.Any(p => p.RefKind != RefKind.None && MentionsTypeParameter(p.Type))) return "by-ref over T";
        if (Calls(definition, dependentOnly: false).FirstOrDefault(IsMethodObserver) is { } observer)
            return "observes its own method: " + observer.ContainingType.Name + "." + observer.Name;
        if (SlotKeys(definition) is not { } keys) return "body not readable";
        if (keys.Count > 0) return "needs dictionary: " + keys.OrderBy(k => k, StringComparer.Ordinal).First();
        foreach (var callee in Calls(definition, dependentOnly: true))
        {
            if (callee.ContainingType.TypeKind == TypeKind.Interface) return "interface over T: " + callee.ContainingType.ToDisplayString();
            if (callee.IsGenericMethod || callee.IsStatic) return "calls " + callee.ToDisplayString();
            if (ExactReason(callee.OriginalDefinition) is { } calleeReason) return "calls exact " + callee.OriginalDefinition.ToDisplayString() + " (" + calleeReason + ")";
        }
        return null;
    }

    // (types served from the framework's IL are named as their real types: the slice's namespaces carry a prefix, Frontend.UpstreamSlice)
    private static string MetadataNameOf(INamedTypeSymbol type) =>
        (type.ContainingNamespace is { IsGlobalNamespace: false } ns ? Frontend.UpstreamSlice.RealNamespace(ns.ToDisplayString()) + "." : "") + type.MetadataName;

    /// <summary>
    /// MethodBase.GetCurrentMethod() reports the method it is called from, exactly (`Holder`1[System.String].Get`); a shared body would report
    /// its canonical form, so a method that calls it stays exact (ADR 0026: __Canon is never observable).
    /// </summary>
    private static bool IsMethodObserver(IMethodSymbol method) =>
        method is { Name: "GetCurrentMethod", ContainingType: { Name: "MethodBase", ContainingNamespace: { Name: "Reflection", ContainingNamespace.Name: "System" } } };

    private bool? _observesFrames;

    /// <summary>
    /// Does the program read the methods of stack frames (StackFrame.GetMethod, StackTrace.GetFrame(s))? Any frame of shared code would then
    /// report its canonical form, so with --shared-generics such a program shares nothing (until frames carry their exact instantiation).
    /// </summary>
    public bool ObservesFrames => _observesFrames ??= ComputeObservesFrames();

    private bool ComputeObservesFrames()
    {
        static bool IsFrameApi(string type, string member) =>
            (type == "StackFrame" && member == "GetMethod") || (type == "StackTrace" && member is "GetFrame" or "GetFrames");
        if (il is not null)
            foreach (var assembly in il.Assemblies)
            {
                var reader = assembly.Reader;
                foreach (var handle in reader.MemberReferences)
                {
                    var reference = reader.GetMemberReference(handle);
                    if (reference.Parent.Kind != HandleKind.TypeReference) continue;
                    var type = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)reference.Parent).Name);
                    if (IsFrameApi(type, reader.GetString(reference.Name))) return true;
                }
            }
        foreach (var tree in compilation.SyntaxTrees)
        {
            var text = tree.ToString();
            if (!text.Contains("StackFrame", StringComparison.Ordinal) && !text.Contains("StackTrace", StringComparison.Ordinal)) continue;
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
                if (model.GetSymbolInfo(node).Symbol is IMethodSymbol { ContainingType.ContainingNamespace: { Name: "Diagnostics", ContainingNamespace.Name: "System" } } m
                    && IsFrameApi(m.ContainingType.Name, m.Name))
                    return true;
        }
        return false;
    }

    /// <summary>The methods a definition's body calls (dependentOnly: only those of type-parameter-dependent types or with dependent type arguments).</summary>
    private List<IMethodSymbol> Calls(IMethodSymbol definition, bool dependentOnly)
    {
        var callees = new List<IMethodSymbol>();
        if (il?.OwnerOf(definition) is { } owner && owner.Body(definition) is { } body)
        {
            var context = IlGenericContext.For(definition);
            foreach (var instruction in IlDecoder.Decode(body.GetILReader()))
            {
                if (instruction.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn)) continue;
                try
                {
                    if (owner.ResolveMember(instruction.Token, context) is IMethodSymbol m && (!dependentOnly || CallsDependent(m))) callees.Add(m);
                }
                catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { }
            }
        }
        else if (definition.DeclaringSyntaxReferences.FirstOrDefault() is { } reference
                 && compilation.GetSemanticModel(reference.SyntaxTree).GetOperation(reference.GetSyntax()) is { } operation)
        {
            foreach (var op in operation.DescendantsAndSelf())
            {
                var target = op switch
                {
                    IInvocationOperation inv => inv.TargetMethod,
                    IObjectCreationOperation { Constructor: { } ctor } => ctor,
                    IPropertyReferenceOperation { Property: var p } => p.GetMethod ?? p.SetMethod,
                    IMethodReferenceOperation r => r.Method,
                    _ => null,
                };
                if (target is not null && (!dependentOnly || CallsDependent(target))) callees.Add(target);
            }
        }
        return callees;
    }

    private static bool CallsDependent(IMethodSymbol method) =>
        method.TypeArguments.Any(MentionsTypeParameter) || (method.ContainingType is { } owner && MentionsTypeParameter(owner));
}
