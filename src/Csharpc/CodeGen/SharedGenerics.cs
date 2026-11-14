using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Shared generic code (ADR 0026): canonical forms, which methods are compiled once per canonical form, and the generic dictionary each
/// canonical body needs. A canonical form names every reference-type type argument by a positional marker, CsharpcCoreLib.__Canon0,
/// __Canon1, ...: the marker of a type parameter is its position in the flattened parameter list of its class and the classes that contain
/// it (stable: it does not depend on which other arguments are reference types, so C&lt;string, int&gt; is C&lt;__Canon0, int&gt; and
/// C&lt;int, string&gt; is C&lt;int, __Canon1&gt;). A canonical body can therefore tell List&lt;TKey&gt; from List&lt;TValue&gt;, which a single
/// __Canon could not, and ask its dictionary for the right one. Markers are internal, never instantiated and never observable.
/// </summary>
/// <summary>
/// Why a definition stays exact. Formatted only when the report asks: the analysis runs for every generic definition the program reaches,
/// and display strings of symbols are most of what it would otherwise allocate.
/// </summary>
internal sealed class SharingReason(string text, ISymbol? subject = null, string? suffix = null, SharingReason? inner = null)
{
    public static implicit operator SharingReason(string text) => new(text);
    public override string ToString() => text + inner + subject?.ToDisplayString() + suffix;
}

internal sealed class SharedGenerics(Compilation compilation, IlAssemblySet? il, Func<IMethodSymbol, IMethodSymbol>? redirect = null)
{
    /// <summary>The most type parameters (flattened) a shared class may have; classes with more stay exact.</summary>
    public const int MarkerCount = 8;

    private readonly INamedTypeSymbol?[] _markers = [.. Enumerable.Range(0, MarkerCount).Select(i => compilation.GetTypeByMetadataName(CoreLib.Namespace + ".__Canon" + i))];

    private bool HasMarkers => _markers[0] is not null;

    private bool? _active;

    /// <summary>Does this compile share anything? Not when the corelib has no markers (then the flag costs nothing and changes nothing).</summary>
    public bool Active => _active ??= HasMarkers;

    /// <summary>Is this type one of the canonical markers?</summary>
    public bool IsMarker(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { Arity: 0, Name: ['_', '_', 'C', 'a', 'n', 'o', 'n', ..] } named) return false;
        foreach (var marker in _markers)
            if (SymbolEqualityComparer.Default.Equals(marker, named)) return true;
        return false;
    }

    /// <summary>
    /// The canonical form of a class instantiation, or null when it has none: an argument is a generic value type over reference types
    /// (KeyValuePair&lt;string, int&gt;: its exact layout and method table would have to come from a dictionary, ADR 0026 step 4), or the class
    /// has more type parameters than there are markers.
    /// </summary>
    public INamedTypeSymbol? CanonicalizeArguments(INamedTypeSymbol type)
    {
        // (asked for every method of every instantiation: Construct allocates, so the answer is kept per type)
        if (_canonicalTypes.TryGetValue(type, out var known)) return known;
        var index = 0;
        return _canonicalTypes[type] = CanonicalizeArguments(type, ref index);
    }

    private readonly ConcurrentDictionary<INamedTypeSymbol, INamedTypeSymbol?> _canonicalTypes = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, IMethodSymbol> _canonicalMethods = new(SymbolEqualityComparer.Default);

    private INamedTypeSymbol? CanonicalizeArguments(INamedTypeSymbol type, ref int index)
    {
        INamedTypeSymbol? outer = null;
        if (type.ContainingType is { } containing && (outer = CanonicalizeArguments(containing, ref index)) is null) return null;
        var definition = outer is null ? type.OriginalDefinition
            : outer.GetTypeMembers(type.Name, type.Arity).First(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, type.OriginalDefinition));
        if (type.Arity == 0) return definition;
        var arguments = new ITypeSymbol[type.Arity];
        for (var i = 0; i < arguments.Length; i++, index++)
        {
            var argument = type.TypeArguments[i];
            if (argument is ITypeParameterSymbol) arguments[i] = argument;
            else if (argument.IsReferenceType)
            {
                if (index >= MarkerCount || _markers[index] is not { } marker || !IsPlainReference(argument)) return null;
                arguments[i] = marker;
            }
            else if (MentionsReferenceArgument(argument)) return null;
            else arguments[i] = argument;
        }
        return definition.Construct(arguments);
    }

    /// <summary>
    /// Reference types a shared body can test, cast to and allocate arrays of through one method table from its dictionary
    /// (csrt_isinst_class): classes, interfaces and single-dimension arrays. Delegates (variance is decided by a test function per delegate
    /// type, System.Delegate by a flag) and multi-dimensional arrays keep their instantiations exact.
    /// </summary>
    private static bool IsPlainReference(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => array.IsSZArray,
        { TypeKind: TypeKind.Delegate } or { SpecialType: SpecialType.System_Delegate or SpecialType.System_MulticastDelegate } => false,
        _ => true,
    };

    /// <summary>The type arguments of a class and of the classes that contain it, outermost first: marker N stands for argument N.</summary>
    public static List<ITypeSymbol> FlatArguments(INamedTypeSymbol type)
    {
        var arguments = type.ContainingType is { } outer ? FlatArguments(outer) : [];
        arguments.AddRange(type.TypeArguments);
        return arguments;
    }

    /// <summary>The exact type a canonical type stands for in one instantiation: each marker replaced by that instantiation's argument.</summary>
    public ITypeSymbol Instantiate(ITypeSymbol canonical, List<ITypeSymbol> arguments) => canonical switch
    {
        _ when !MentionsMarker(canonical) => canonical,
        IArrayTypeSymbol array => compilation.CreateArrayTypeSymbol(Instantiate(array.ElementType, arguments), array.Rank),
        INamedTypeSymbol named when IsMarker(named) => arguments[Array.FindIndex(_markers, m => SymbolEqualityComparer.Default.Equals(m, named))],
        INamedTypeSymbol named => InstantiateNamed(named, arguments),
        _ => throw new InvalidOperationException($"shared generics: no exact form of '{canonical}'"),
    };

    private INamedTypeSymbol InstantiateNamed(INamedTypeSymbol named, List<ITypeSymbol> arguments)
    {
        var definition = named.OriginalDefinition;
        if (named.ContainingType is { } outer && (outer.IsGenericType || outer.ContainingType is not null))
            definition = InstantiateNamed(outer, arguments).GetTypeMembers(named.Name, named.Arity)
                .First(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, named.OriginalDefinition));
        return named.Arity == 0 ? definition : definition.Construct([.. named.TypeArguments.Select(a => Instantiate(a, arguments))]);
    }

    /// <summary>A value type with a reference type somewhere in its type arguments (the temporary limit of step 3: such instantiations stay exact).</summary>
    public static bool MentionsReferenceArgument(ITypeSymbol type) =>
        type is INamedTypeSymbol named && (AnyArgument(named.TypeArguments, static a => a.IsReferenceType || MentionsReferenceArgument(a))
            || (named.ContainingType is { } outer && MentionsReferenceArgument(outer)));

    // (LINQ over an ImmutableArray boxes it: these run for every type the analysis looks at)
    private static bool AnyArgument(System.Collections.Immutable.ImmutableArray<ITypeSymbol> arguments, Func<ITypeSymbol, bool> test)
    {
        foreach (var argument in arguments)
            if (test(argument)) return true;
        return false;
    }

    /// <summary>
    /// The canonical form of a method of a generic class (its own type arguments are kept: generic methods are not shared yet), or the
    /// method itself when its class has no canonical form.
    /// </summary>
    public IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        if (_canonicalMethods.TryGetValue(method, out var known)) return known;
        return _canonicalMethods[method] = ComputeCanonical(method);
    }

    private IMethodSymbol ComputeCanonical(IMethodSymbol method)
    {
        if (!HasMarkers || method.ContainingType is not { } owner || CanonicalizeArguments(owner) is not { } canonicalOwner
            || SymbolEqualityComparer.Default.Equals(canonicalOwner, owner)) return method;
        var inOwner = canonicalOwner.GetMembers(method.Name).OfType<IMethodSymbol>().First(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, method.OriginalDefinition));
        return method.IsGenericMethod ? inOwner.ConstructedFrom.Construct([.. method.TypeArguments]) : inOwner;
    }

    /// <summary>
    /// Why an instantiation is not shared code, for the report: the method's own kind, or which of the canonical form's limits its type
    /// arguments hit (null: it is a shared class).
    /// </summary>
    public string? NotSharedReason(IMethodSymbol method)
    {
        if (method.IsStatic) return "static";
        if (method.ContainingType is not { } owner) return "no class";
        if (owner.TypeKind != TypeKind.Class) return owner.TypeKind == TypeKind.Struct ? "struct (step 4)" : owner.TypeKind.ToString().ToLowerInvariant();
        if (IsSharedType(owner)) return null;
        var arguments = FlatArguments(owner);
        if (arguments.Count > MarkerCount) return "more than " + MarkerCount + " type parameters";
        foreach (var argument in arguments)
        {
            if (argument.IsValueType && MentionsReferenceArgument(argument)) return "value type over a reference type (step 4)";
            if (argument.IsReferenceType && !IsPlainReference(argument)) return argument is IArrayTypeSymbol ? "multi-dimensional array argument" : "delegate argument";
        }
        return arguments.Any(a => a.IsReferenceType) ? "not a shared class" : "value-type arguments only";
    }

    /// <summary>Is this class (or a containing class) a generic instantiation whose canonical form is shared code?</summary>
    public bool IsSharedType(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class && Active && type.TypeKind == TypeKind.Class && (type.IsGenericType || type.ContainingType is not null)
        && CanonicalizeArguments(type) is { } canonical && MentionsMarker(canonical);

    /// <summary>Is this method's canonical form shared code, i.e. does it stand for more than its own exact instantiation?</summary>
    public bool IsShared(IMethodSymbol canonical) => HasMarkers && canonical.ContainingType is { } owner && MentionsMarker(owner);

    /// <summary>Does the type name a canonical marker (as itself, a type argument, a containing type's argument or an array element)?</summary>
    public bool MentionsMarker(ITypeSymbol type)
    {
        // (on the method-table paths of every compile with the flag on: no allocation)
        switch (type)
        {
            case INamedTypeSymbol named:
                if (IsMarker(named)) return true;
                foreach (var argument in named.TypeArguments)
                    if (MentionsMarker(argument)) return true;
                return named.ContainingType is { } outer && MentionsMarker(outer);
            case IArrayTypeSymbol array: return MentionsMarker(array.ElementType);
            case IPointerTypeSymbol pointer: return MentionsMarker(pointer.PointedAtType);
            default: return false;
        }
    }

    private readonly ConcurrentDictionary<IMethodSymbol, int> _slots = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The number of generic-dictionary slots the canonical body of this definition would need (ADR 0026, section 2): the distinct
    /// type-parameter-dependent types, methods and static fields its body names (for `new`, casts, `typeof`, boxing, arrays, statics,
    /// calls into other generic code). -1 when the body cannot be read (no IL, no source).
    /// </summary>
    public int DictionarySlots(IMethodSymbol method) => _slots.GetOrAdd(method.OriginalDefinition, CountSlots);

    private int CountSlots(IMethodSymbol definition) => SlotKeys(definition)?.Count ?? -1;

    /// <summary>An IL instruction of a definition's body with its operand resolved in the definition's generic context (null: no token).</summary>
    private readonly record struct IlStep(ILOpCode OpCode, ISymbol? Symbol);

    private sealed record IlCode(IlAssembly Owner, MethodBodyBlock Body, IlGenericContext Context, List<IlStep> Steps);

    // The analyses of one definition (ExactReason: calls, slots, unsupported needs, then the callees' own) read its body several times in a
    // row: the last few decoded bodies are kept, and nothing else, so the analysis decodes each body about once and holds no per-body data.
    private readonly (IMethodSymbol Definition, IlCode? Code)[] _recentIl = new (IMethodSymbol, IlCode?)[4];
    private int _recentIlNext;

    private IlCode? IlCodeOf(IMethodSymbol definition)
    {
        foreach (var (d, code) in _recentIl)
            if (d is not null && SymbolEqualityComparer.Default.Equals(d, definition)) return code;
        IlCode? result = null;
        if (il?.OwnerOf(definition) is { } owner && owner.Body(definition) is { } body)
        {
            // The body as lowering decodes it (IlMethodBody.For: once per definition), in the same generic context, so the tokens
            // resolved here are the ones lowering finds in the assembly's resolution cache, not a second set of symbols.
            var decoded = IlMethodBody.For(owner, definition, body);
            var context = decoded.Context;
            var steps = new List<IlStep>(decoded.Instructions.Length);
            foreach (var instruction in decoded.Instructions)
            {
                ISymbol? symbol = null;
                if (instruction.Token != 0 && instruction.OpCode is not ILOpCode.Ldstr)
                {
                    var handle = MetadataTokens.EntityHandle(instruction.Token);
                    try
                    {
                        symbol = handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                            ? owner.ResolveType(instruction.Token, context)
                            : owner.ResolveMember(instruction.Token, context);
                    }
                    catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { }
                }
                steps.Add(new IlStep(instruction.OpCode, symbol));
            }
            result = new IlCode(owner, body, context, steps);
        }
        _recentIl[_recentIlNext] = (definition, result);
        _recentIlNext = (_recentIlNext + 1) % _recentIl.Length;
        return result;
    }

    private readonly (IMethodSymbol Definition, IOperation[]? Operations)[] _recentSource = new (IMethodSymbol, IOperation[]?)[4];
    private int _recentSourceNext;

    /// <summary>
    /// The bound body of a source definition (the same few-entry reuse as <see cref="IlCodeOf"/>). An instance constructor also runs
    /// its class's instance field and property initializers, which are not part of its body's syntax: they are analysed with it.
    /// </summary>
    private IOperation[]? SourceOf(IMethodSymbol definition)
    {
        foreach (var (d, operations) in _recentSource)
            if (d is not null && SymbolEqualityComparer.Default.Equals(d, definition)) return operations;
        IOperation[]? result = null;
        if (definition.DeclaringSyntaxReferences.FirstOrDefault() is { } reference
            && compilation.GetSemanticModel(reference.SyntaxTree).GetOperation(reference.GetSyntax()) is { } body)
        {
            List<IOperation> operations = [body];
            if (definition.MethodKind == MethodKind.Constructor && !definition.IsStatic)
                foreach (var member in definition.ContainingType.GetMembers())
                {
                    if (member.IsStatic || member is not (IFieldSymbol or IPropertySymbol)) continue;
                    foreach (var declaration in member.DeclaringSyntaxReferences)
                    {
                        var syntax = declaration.GetSyntax();
                        var initializer = syntax switch
                        {
                            Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax v => (SyntaxNode?)v.Initializer,
                            Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax p => p.Initializer,
                            _ => null,
                        };
                        if (initializer is not null && compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(initializer) is { } init)
                            operations.Add(init);
                    }
                }
            result = [.. operations];
        }
        _recentSource[_recentSourceNext] = (definition, result);
        _recentSourceNext = (_recentSourceNext + 1) % _recentSource.Length;
        return result;
    }

    private bool HasReadableBody(IMethodSymbol definition) => IlCodeOf(definition) is not null || SourceOf(definition) is not null;

    /// <summary>The first symbol the body names that a dictionary would have to supply (null: none), without listing them all.</summary>
    private SharingReason? FirstDependent(IMethodSymbol definition)
    {
        if (IlCodeOf(definition) is { } code)
        {
            foreach (var (op, symbol) in code.Steps)
                if (symbol is not null && Dependent(symbol, op)) return new SharingReason(op + ":", symbol);
            return null;
        }
        return SlotKeys(definition) is { Count: > 0 } keys ? new SharingReason(keys.Min(StringComparer.Ordinal)!) : null;
    }

    /// <summary>The type-parameter-dependent symbols a definition's body names (null: body not readable).</summary>
    private HashSet<string>? SlotKeys(IMethodSymbol definition)
    {
        if (IlCodeOf(definition) is { } code) return IlSlotKeys(code);
        return SourceOf(definition) is { } operations ? SourceSlotKeys(operations) : null;
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

    private static HashSet<string> IlSlotKeys(IlCode code)
    {
        var (owner, body, context, steps) = code;
        var needed = new HashSet<string>(StringComparer.Ordinal);
        // `catch (T)`: the handler's type test is against the exact exception type.
        foreach (var region in body.ExceptionRegions)
        {
            if (region.Kind != ExceptionRegionKind.Catch || region.CatchType.IsNil) continue;
            try { if (MentionsTypeParameter(owner.ResolveType(MetadataTokens.GetToken(region.CatchType), context))) needed.Add("catch"); }
            catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { needed.Add("catch"); }
        }
        foreach (var (op, symbol) in steps)
            if (symbol is not null && Dependent(symbol, op)) needed.Add(op + ":" + symbol.ToDisplayString());
        return needed;
    }

    private static HashSet<string> SourceSlotKeys(IOperation[] bodies)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in bodies.SelectMany(b => b.DescendantsAndSelf()))
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
                // (an element of an array over T: a store or a writable reference checks the array's exact element type)
                IArrayElementReferenceOperation { ArrayReference.Type: IArrayTypeSymbol array } => array,
                ICatchClauseOperation c => c.ExceptionType,
                // (foreach over IEnumerable<T>: the shared body dispatches on IEnumerator<T>, an interface the dictionary supplies)
                IForEachLoopOperation loop when IsInterfaceEnumeration(loop) => EnumeratorOf(loop).ReturnType,
                _ => null,
            };
            if (symbol is not null && Dependent(symbol, ILOpCode.Nop)) needed.Add(op.Kind + ":" + symbol.ToDisplayString());
        }
        return needed;
    }

    /// <summary>Does the symbol depend on a type parameter in a way only a dictionary can supply in shared code?</summary>
    private static bool Dependent(ISymbol symbol, ILOpCode opCode) => symbol switch
    {
        // In shared code every type parameter is a reference type: element loads, initobj, ldobj/stobj and sizeof of T (or of a generic
        // value type over it, whose canonical layout is the same) need no type handle, and boxing a reference type is the identity.
        // An element store does: a T[] can be a covariant array (a string[] behind an object[] when T is object), and stelem checks the
        // value against the array's exact element type, so does ldelema.
        ITypeSymbol type when opCode is ILOpCode.Ldelem or ILOpCode.Initobj or ILOpCode.Ldobj or ILOpCode.Stobj
            or ILOpCode.Cpobj or ILOpCode.Sizeof => false,
        ITypeSymbol type when opCode is ILOpCode.Box => MentionsTypeParameter(type) && type is not ITypeParameterSymbol && type.IsValueType,
        ITypeSymbol type => MentionsTypeParameter(type),
        // Array.Copy, CopyTo and SetValue check (or cast) each element against the arrays' exact element types.
        IMethodSymbol method when IsCheckedArrayCopy(method) => true,
        IMethodSymbol method => method.TypeArguments.Any(MentionsTypeParameter)
            // an instance method of the generic type itself is reached through `this`; a static one (or another type's) needs the dictionary
            || (method.ContainingType is { } owner && MentionsTypeParameter(owner) && (method.IsStatic || opCode is ILOpCode.Newobj or ILOpCode.Ldftn)),
        IFieldSymbol field => field.IsStatic && MentionsTypeParameter(field.ContainingType),
        _ => false,
    };

    private IMethodSymbol Redirected(IMethodSymbol method)
    {
        if (redirect is null) return method;
        try { return redirect(method) is { ContainingType: not null } mapped ? mapped : method; }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { return method; }
    }

    private static bool IsCheckedArrayCopy(IMethodSymbol method) =>
        method is { Name: "Copy" or "ConstrainedCopy" or "CopyTo" or "SetValue", ContainingType.SpecialType: SpecialType.System_Array };

    private static bool MentionsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        INamedTypeSymbol named => AnyArgument(named.TypeArguments, MentionsTypeParameter) || (named.ContainingType is { } outer && MentionsTypeParameter(outer)),
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

    private readonly ConcurrentDictionary<IMethodSymbol, SharingReason?> _exactReasons = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The canonical form this exact method is compiled as, or null when it is compiled exact (its own instantiation). Shared: a
    /// non-generic instance method of a generic class whose canonical form is shared, whose definition needs nothing a dictionary would
    /// have to supply (step 3a) and whose calls into other generic code reach only methods that are shared too.
    /// </summary>
    public IMethodSymbol? ShareAs(IMethodSymbol method)
    {
        if (!Active || method.IsStatic || method.IsGenericMethod || method.ContainingType is not { TypeKind: TypeKind.Class } owner
            || !IsSharedType(owner) || ExactReasonOf(method.OriginalDefinition) is not null || !SameSignature(method)) return null;
        return Canonicalize(method);
    }

    /// <summary>
    /// Do the exact method and its canonical form pass and return the same LLVM types? A generic value type over reference types
    /// (KeyValuePair&lt;Exception, Action&gt; as T) is a struct type of its own per instantiation, so such a signature stays exact.
    /// </summary>
    private static bool SameSignature(IMethodSymbol method)
    {
        static bool Same(ITypeSymbol t) => t.IsReferenceType || t is ITypeParameterSymbol || !MentionsReferenceArgument(t);
        if (!Same(method.ReturnType)) return false;
        foreach (var parameter in method.Parameters)
            if (!Same(parameter.Type)) return false;
        return true;
    }

    /// <summary>Why a definition's instantiations stay exact (null: they are shared). For the report (CSHARPC_GENERIC_REPORT).</summary>
    public string? ExactReason(IMethodSymbol definition) => ExactReasonOf(definition)?.ToString();

    private readonly Dictionary<IMethodSymbol, int> _inProgress = new(SymbolEqualityComparer.Default);
    private int _lowestCycle = int.MaxValue;

    /// <summary>
    /// Calls form cycles (mutual recursion, an iterator's MoveNext and its finally blocks): a definition reached again while its own reason
    /// is being computed is taken as shared for now. A result that leaned on such an assumption about a caller further up is kept only once
    /// that caller has its answer: if the caller turns out exact, whatever assumed otherwise is computed again, and sees it. A whole cycle
    /// with no exact member is shared (the greatest consistent answer).
    /// </summary>
    private SharingReason? ExactReasonOf(IMethodSymbol definition)
    {
        definition = definition.OriginalDefinition;
        if (_exactReasons.TryGetValue(definition, out var known)) return known;
        if (_inProgress.TryGetValue(definition, out var depth))
        {
            _lowestCycle = Math.Min(_lowestCycle, depth);
            return null;
        }
        var myDepth = _inProgress.Count;
        _inProgress[definition] = myDepth;
        var outer = _lowestCycle;
        _lowestCycle = int.MaxValue;
        SharingReason? reason;
        try { reason = ComputeExactReason(definition); }
        finally { _inProgress.Remove(definition); }
        var leanedOn = _lowestCycle; // the shallowest caller still in progress that this answer assumed shared
        if (reason is not null || leanedOn >= myDepth) _exactReasons[definition] = reason;
        _lowestCycle = Math.Min(outer, leanedOn >= myDepth ? int.MaxValue : leanedOn);
        return reason;
    }

    /// <summary>
    /// CSHARPC_SHARED_GENERICS_EXCLUDE=&lt;a,b,...&gt;: definitions whose display name contains one of these substrings stay exact. A bisection
    /// tool for shared-code bugs (which definitions, when compiled exact again, make a program right); it changes nothing when unset.
    /// </summary>
    private static readonly string[] s_excluded = (Environment.GetEnvironmentVariable("CSHARPC_SHARED_GENERICS_EXCLUDE") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private SharingReason? ComputeExactReason(IMethodSymbol definition)
    {
        if (s_excluded.Length > 0 && s_excluded.Any(definition.ToDisplayString().Contains)) return "excluded (CSHARPC_SHARED_GENERICS_EXCLUDE)";
        if (definition.IsStatic) return "static";
        if (definition.IsGenericMethod) return "generic method";
        if (definition.IsAbstract || definition.IsExtern) return "no body";
        if (definition.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.ExplicitInterfaceImplementation or MethodKind.EventAdd or MethodKind.EventRemove))
            return "method kind " + definition.MethodKind;
        // A constructor (and any member of a struct) runs its type's static constructor first, and a canonical form has no statics of its
        // own to initialize (step 3c).
        if ((definition.MethodKind == MethodKind.Constructor || definition.ContainingType.IsValueType) && definition.ContainingType.StaticConstructors.Length > 0)
            return "class initializer";
        for (var t = definition.ContainingType; t is not null; t = t.ContainingType)
            if (s_exactDefinitions.Contains(MetadataNameOf(t.OriginalDefinition))) return "hot path " + MetadataNameOf(t.OriginalDefinition);
        // The signature must lower identically for every instantiation: T itself and reference types are pointers; a generic value type
        // over T (KeyValuePair<T, int>) has a layout of its own per instantiation.
        foreach (var type in definition.Parameters.Select(p => p.Type).Append(definition.ReturnType))
            if (MentionsTypeParameter(type) && type is not ITypeParameterSymbol && !type.IsReferenceType) return new SharingReason("signature ", type);
        if (definition.Parameters.Any(p => p.RefKind != RefKind.None && MentionsTypeParameter(p.Type))) return "by-ref over T";
        if (Calls(definition, dependentOnly: false).FirstOrDefault(IsMethodObserver) is { } observer)
            return "observes its own method: " + observer.ContainingType.Name + "." + observer.Name;
        if (!HasReadableBody(definition)) return "body not readable";
        // Step 3b-1: an instance method of a class reads type handles (casts, typeof, new T[], new C<T>) from the dictionary its `this`
        // carries. A struct's method has no dictionary (step 4), so it is shared only when it needs none.
        if (definition.ContainingType.TypeKind != TypeKind.Class && FirstDependent(definition) is { } need) return new SharingReason("needs dictionary (no dictionary in a struct): ", inner: need);
        if (UnsupportedNeed(definition) is { } unsupported) return new SharingReason("needs ", inner: unsupported);
        foreach (var named in Calls(definition, dependentOnly: true))
        {
            // What lowering calls: a library member served by the corelib (List<T> from the framework's IL, a shadow) is that code.
            var callee = Redirected(named);
            // Step 3b-2: a call through an interface over T dispatches on the interface its dictionary names. A generic interface method
            // (a closed-world type switch per exact instantiation) and an interface with no canonical form stay exact.
            if (callee.ContainingType.TypeKind == TypeKind.Interface)
            {
                if (callee.IsGenericMethod || callee.IsStatic) return new SharingReason("calls ", callee);
                if (!HasCanonicalFormOverParameters(callee.ContainingType)) return new SharingReason("interface over T with no canonical form: ", callee.ContainingType);
                continue;
            }
            if (callee.IsGenericMethod || callee.IsStatic) return new SharingReason("calls ", callee);
            // The callee's own class must have a canonical form whatever T is: List<KeyValuePair<T, int>> or Wrapper<Func<T>> would be
            // compiled over the markers themselves, with no dictionary behind them.
            if (callee.ContainingType.TypeKind == TypeKind.Class && !HasCanonicalFormOverParameters(callee.ContainingType))
                return new SharingReason("calls into ", callee.ContainingType, " (no canonical form)");
            if (callee.ContainingType.TypeKind == TypeKind.Delegate) return new SharingReason("delegate over T: ", callee.ContainingType);
            // (the callee's own reason is on its own line of the report: nesting it here would grow with the call chain)
            if (ExactReasonOf(callee.OriginalDefinition) is not null) return new SharingReason("calls exact ", callee.OriginalDefinition);
        }
        return null;
    }

    private readonly ConcurrentDictionary<IMethodSymbol, HashSet<ITypeSymbol>> _named = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The types a canonical method's body names, in canonical terms: the operands of its instructions, the types of the members, locals
    /// and parameters it uses, and what those are made of (element types, type arguments). A canonical body may ask its dictionary only for
    /// the handle of one of these: what the eligibility analysis read and what lowering asks for come from the same body.
    /// </summary>
    public HashSet<ITypeSymbol> NamedTypes(IMethodSymbol canonicalMethod) => _named.GetOrAdd(canonicalMethod, method =>
    {
        var named = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var substitution = TypeSubstitution.For(method, compilation);
        void Add(ITypeSymbol? type)
        {
            if (type is null || !named.Add(type)) return;
            switch (type)
            {
                case IArrayTypeSymbol array: Add(array.ElementType); break;
                case IPointerTypeSymbol pointer: Add(pointer.PointedAtType); break;
                case INamedTypeSymbol n:
                    foreach (var argument in n.TypeArguments) Add(argument);
                    Add(n.ContainingType);
                    break;
            }
        }
        void AddSymbol(ISymbol? symbol)
        {
            switch (symbol)
            {
                case ITypeSymbol type: Add(substitution.Apply(type)); break;
                case IFieldSymbol field: AddSymbol(field.Type); AddSymbol(field.ContainingType); break;
                case IPropertySymbol property: AddSymbol(property.Type); AddSymbol(property.ContainingType); break;
                case ILocalSymbol local: AddSymbol(local.Type); break;
                case IParameterSymbol parameter: AddSymbol(parameter.Type); break;
                case IMethodSymbol m:
                    AddSymbol(m.ContainingType);
                    AddSymbol(m.ReturnType);
                    foreach (var parameter in m.Parameters) AddSymbol(parameter.Type);
                    foreach (var argument in m.TypeArguments) AddSymbol(argument);
                    break;
            }
        }
        var definition = method.OriginalDefinition;
        AddSymbol(definition);
        for (var t = definition.ContainingType; t is not null; t = t.BaseType)
            foreach (var field in t.GetMembers().OfType<IFieldSymbol>()) AddSymbol(field.Type);
        if (IlCodeOf(definition) is { } code)
        {
            foreach (var step in code.Steps) AddSymbol(step.Symbol);
        }
        else if (SourceOf(definition) is { } operations)
            foreach (var op in operations.SelectMany(o => o.DescendantsAndSelf()))
            {
                AddSymbol(op.Type);
                AddSymbol(op switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IObjectCreationOperation { Constructor: { } constructor } => constructor,
                    IMemberReferenceOperation member => member.Member,
                    ILocalReferenceOperation local => local.Local,
                    IParameterReferenceOperation parameter => parameter.Parameter,
                    ITypeOfOperation typeOf => typeOf.TypeOperand,
                    IIsTypeOperation isType => isType.TypeOperand,
                    IPatternOperation pattern => pattern.NarrowedType,
                    IVariableDeclaratorOperation declarator => declarator.Symbol,
                    IForEachLoopOperation loop when IsInterfaceEnumeration(loop) => EnumeratorOf(loop),
                    _ => null,
                });
            }
        return named;
    });

    /// <summary>Does a class named in terms of type parameters canonicalize for every (shareable) choice of them?</summary>
    private static bool HasCanonicalFormOverParameters(INamedTypeSymbol type)
    {
        var arguments = FlatArguments(type);
        return arguments.Count <= MarkerCount && arguments.All(a => a is ITypeParameterSymbol || !MentionsTypeParameter(a) || (a.IsReferenceType && IsPlainReference(a)));
    }

    /// <summary>
    /// A class or interface over T, named by a body (as an operand, a member's owner, a field's or a call's type), that has no canonical form
    /// (TreeSet&lt;KeyValuePair&lt;TKey, TValue&gt;&gt;): lowering would devirtualize or allocate through it over the markers themselves, with no
    /// dictionary behind them. Null when there is none.
    /// </summary>
    private static ITypeSymbol? WithoutCanonicalForm(ISymbol symbol)
    {
        static ITypeSymbol? Check(ITypeSymbol? type) => type switch
        {
            IArrayTypeSymbol array => Check(array.ElementType),
            INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface } named when MentionsTypeParameter(named) && !HasCanonicalFormOverParameters(named) => named,
            _ => null,
        };
        switch (symbol)
        {
            case ITypeSymbol type: return Check(type);
            case IFieldSymbol field: return Check(field.Type) ?? Check(field.ContainingType);
            case IMethodSymbol method:
                if ((Check(method.ContainingType) ?? Check(method.ReturnType)) is { } found) return found;
                foreach (var parameter in method.Parameters)
                    if (Check(parameter.Type) is { } p) return p;
                return null;
            default: return null;
        }
    }

    /// <summary>
    /// `constrained. P; callvirt I&lt;Q&gt;.M` with Q another type parameter: when P is a struct in a canonical form that shares Q (Roslyn's
    /// OverloadResolution.&lt;&gt;c__102&lt;TMemberResolution, TMember&gt;), the call must reach the struct's own implementation of the exact
    /// I&lt;X&gt;, which a body over the marker cannot name. An interface over P alone is exact whenever P is a struct, and shared code
    /// dispatches it when P is a reference type; a reference-type constraint on P makes every receiver an object.
    /// </summary>
    private static bool ConstrainedThroughOtherParameter(ITypeParameterSymbol constrainedOn, INamedTypeSymbol calledInterface)
    {
        if (constrainedOn.IsReferenceType) return false; // (a class or base-class constraint)
        static bool Other(ITypeSymbol type, ITypeParameterSymbol p) => type switch
        {
            ITypeParameterSymbol t => !SymbolEqualityComparer.Default.Equals(t, p),
            INamedTypeSymbol named => named.TypeArguments.Any(a => Other(a, p)) || (named.ContainingType is { } outer && Other(outer, p)),
            IArrayTypeSymbol array => Other(array.ElementType, p),
            IPointerTypeSymbol pointer => Other(pointer.PointedAtType, p),
            _ => false,
        };
        return Other(calledInterface, constrainedOn);
    }

    private static IOperation Receiver(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion) operation = conversion.Operand;
        return operation;
    }

    private static bool HasFinalizer(INamedTypeSymbol type)
    {
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            if (t.GetMembers("Finalize").OfType<IMethodSymbol>().Any(m => m.Parameters.IsEmpty)) return true;
        return false;
    }

    /// <summary>
    /// What a definition's body needs that step 3b-1's dictionaries do not supply yet (null: nothing). Supplied: the method tables for casts
    /// and type tests against reference types over T, typeof (IL bodies), arrays of types over T and `new` of classes over T. Not yet:
    /// boxing and unboxing generic value types over T, delegates over T, statics of generic types and method addresses (step 3c), classes
    /// with finalizers, catch clauses over T, and in source bodies closures, dynamic operations and typeof (lowered against the static type).
    /// </summary>
    private SharingReason? UnsupportedNeed(IMethodSymbol definition)
    {
        if (IlCodeOf(definition) is { } code)
        {
            var (owner, body, context, steps) = code;
            foreach (var region in body.ExceptionRegions)
                if (region.Kind == ExceptionRegionKind.Filter) return "an exception filter"; // (a filter is a function of its own, without `this`)
                else if (region.Kind == ExceptionRegionKind.Catch && !region.CatchType.IsNil)
                {
                    try { if (MentionsTypeParameter(owner.ResolveType(MetadataTokens.GetToken(region.CatchType), context))) return "catch over T"; }
                    catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { return "catch type not readable"; }
                }
            // An interface call on an array over T is lowered through the array helpers, generic over the element type
            // (ArrayChecks.CopyToUntyped<T>): in a canonical body that would be one of them over a marker, with no dictionary behind it.
            // The receiver's type is not in the token, so a body that holds such an array and calls through an interface stays exact.
            var holdsArrayOverT = definition.Parameters.Any(p => p.Type is IArrayTypeSymbol a && MentionsTypeParameter(a))
                || steps.Any(st => st.Symbol is IArrayTypeSymbol a && MentionsTypeParameter(a)
                    || st.Symbol is IFieldSymbol { Type: IArrayTypeSymbol fa } && MentionsTypeParameter(fa)
                    || st.Symbol is IMethodSymbol { ReturnType: IArrayTypeSymbol ra } && MentionsTypeParameter(ra));
            if (holdsArrayOverT && steps.Any(st => st.Symbol is IMethodSymbol { ContainingType.TypeKind: TypeKind.Interface }))
                return "interface call where an array over T may be the receiver";
            for (var i = 0; i + 1 < steps.Count; i++)
                if (steps[i] is { OpCode: ILOpCode.Constrained, Symbol: ITypeParameterSymbol constrainedOn }
                    && steps[i + 1].Symbol is IMethodSymbol { ContainingType: { TypeKind: TypeKind.Interface } calledInterface }
                    && ConstrainedThroughOtherParameter(constrainedOn, calledInterface))
                    return new SharingReason("constrained call on a type parameter through ", calledInterface);
            foreach (var (op, symbol) in steps)
            {
                if (op is ILOpCode.Mkrefany or ILOpCode.Refanyval or ILOpCode.Refanytype) return "typed references";
                if (symbol is null) continue;
                // `constrained. S<T>; callvirt I.M`: lowering calls S<T>'s own method, which the token does not name (the call names I.M).
                if (op is ILOpCode.Constrained && symbol is ITypeSymbol { IsValueType: true } constrainedType && MentionsTypeParameter(constrainedType))
                    return new SharingReason("constrained call on ", constrainedType);
                if (WithoutCanonicalForm(symbol) is { } uncanonical) return new SharingReason("uses ", uncanonical, " (no canonical form)");
                switch (symbol)
                {
                    case ITypeSymbol type when MentionsTypeParameter(type):
                        if (op is ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Castclass or ILOpCode.Isinst)
                        {
                            if (type.IsValueType) return new SharingReason("boxing of ", type);
                            if (type is not ITypeParameterSymbol && !IsPlainReference(type)) return new SharingReason("cast to ", type);
                        }
                        break;
                    case IFieldSymbol field when field.IsStatic && MentionsTypeParameter(field.ContainingType): return new SharingReason("static field ", field);
                    // (Array.Copy to a System.Array, CopyTo and SetValue are lowered as a generic helper over the source element type: not yet)
                    case IMethodSymbol copy when IsCheckedArrayCopy(copy): return "array copy";
                    case IFieldSymbol field when op is ILOpCode.Ldtoken && MentionsTypeParameter(field.ContainingType): return "field handle";
                    case IMethodSymbol method when CallsDependent(method):
                        if (op is ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Ldtoken) return new SharingReason("address of ", method);
                        if (op is ILOpCode.Newobj && method.ContainingType.TypeKind == TypeKind.Delegate) return new SharingReason("delegate ", method.ContainingType);
                        if (op is ILOpCode.Newobj && method.ContainingType.TypeKind == TypeKind.Class && HasFinalizer(method.ContainingType))
                            return new SharingReason("finalizable ", method.ContainingType);
                        break;
                }
            }
            return null;
        }
        if (SourceOf(definition) is { } operations)
            foreach (var op in operations.SelectMany(o => o.DescendantsAndSelf()))
            {
                if (op is IAnonymousFunctionOperation or ILocalFunctionOperation or IDelegateCreationOperation) return "closure or delegate";
                if (op.Type is { } opType && WithoutCanonicalForm(opType) is { } uncanonical) return new SharingReason("uses ", uncanonical, " (no canonical form)");
                if (op is IInvocationOperation { TargetMethod.ContainingType: { TypeKind: TypeKind.Interface } invokedInterface, Instance: { } typed }
                    && Receiver(typed).Type is ITypeParameterSymbol constrainedOn && ConstrainedThroughOtherParameter(constrainedOn, invokedInterface))
                    return new SharingReason("constrained call on a type parameter through ", invokedInterface);
                if (op is IPropertyReferenceOperation { Property.ContainingType: { TypeKind: TypeKind.Interface } propertyInterface, Instance: { } typedProperty }
                    && Receiver(typedProperty).Type is ITypeParameterSymbol propertyOn && ConstrainedThroughOtherParameter(propertyOn, propertyInterface))
                    return new SharingReason("constrained call on a type parameter through ", propertyInterface);
                if (op is IInvocationOperation { TargetMethod.ContainingType.TypeKind: TypeKind.Interface, Instance: { } receiver }
                    && Receiver(receiver).Type is IArrayTypeSymbol receiverArray && MentionsTypeParameter(receiverArray))
                    return "interface call on an array over T";
                if (op.Kind is OperationKind.DynamicInvocation or OperationKind.DynamicMemberReference or OperationKind.DynamicObjectCreation
                    or OperationKind.DynamicIndexerAccess) return "dynamic operation";
                switch (op)
                {
                    case ITypeOfOperation t when MentionsTypeParameter(t.TypeOperand): return "typeof in a source body";
                    case IInvocationOperation copy when IsCheckedArrayCopy(copy.TargetMethod): return "array copy";
                    case IFieldReferenceOperation { Field.IsStatic: true } f when MentionsTypeParameter(f.Field.ContainingType): return new SharingReason("static field ", f.Field);
                    case IObjectCreationOperation { Type: INamedTypeSymbol created } when MentionsTypeParameter(created) && created.TypeKind == TypeKind.Class && HasFinalizer(created):
                        return new SharingReason("finalizable ", created);
                    case IAnonymousObjectCreationOperation or ITupleOperation when op.Type is { } t && MentionsTypeParameter(t): return "anonymous type or tuple over T";
                    case ICatchClauseOperation c when MentionsTypeParameter(c.ExceptionType): return "catch over T";
                    case IConversionOperation c when c.Type is { } to && c.Operand.Type is { } from && (MentionsTypeParameter(to) || MentionsTypeParameter(from)):
                        if (c.OperatorMethod is not null) return "user-defined conversion over T";
                        if ((to.IsValueType && to is not ITypeParameterSymbol) != (from.IsValueType && from is not ITypeParameterSymbol)) return "boxing of a value type over T";
                        if (to is not ITypeParameterSymbol && to.IsReferenceType && !IsPlainReference(to)) return new SharingReason("cast to ", to);
                        break;
                    case IIsTypeOperation i when MentionsTypeParameter(i.TypeOperand) && i.TypeOperand is not ITypeParameterSymbol && (i.TypeOperand.IsValueType || !IsPlainReference(i.TypeOperand)):
                        return new SharingReason("type test against ", i.TypeOperand);
                    case IPatternOperation p when p.NarrowedType is { } narrowed && MentionsTypeParameter(narrowed) && narrowed is not ITypeParameterSymbol
                        && (narrowed.IsValueType || !IsPlainReference(narrowed)) && !SymbolEqualityComparer.Default.Equals(narrowed, p.InputType):
                        return new SharingReason("pattern over ", narrowed);
                    // Step 3b-2: foreach over an IEnumerable<X> interface whose element is the loop variable's own type is GetEnumerator,
                    // MoveNext, Current and Dispose through interfaces, which shared code dispatches on its dictionary. Other shapes (a
                    // struct enumerator over T, a cast per element, deconstruction) stay exact.
                    case IForEachLoopOperation f when IsInterfaceEnumeration(f):
                        break;
                    case IForEachLoopOperation or IUsingOperation or IUsingDeclarationOperation or IAwaitOperation or IInterpolatedStringOperation or IRangeOperation
                        or IDeconstructionAssignmentOperation or IWithOperation or ICollectionExpressionOperation or ISpreadOperation or IEventAssignmentOperation
                        when op.DescendantsAndSelf().Any(o => o.Type is { } t && MentionsTypeParameter(t)):
                        return op.Kind + " over T in a source body";
                }
            }
        return null;
    }

    /// <summary>IEnumerable&lt;X&gt;.GetEnumerator of an interface enumeration (IsInterfaceEnumeration): its return type is IEnumerator&lt;X&gt;.</summary>
    private static IMethodSymbol EnumeratorOf(IForEachLoopOperation loop) =>
        ((INamedTypeSymbol)loop.Collection.Type!).GetMembers("GetEnumerator").OfType<IMethodSymbol>().First(m => m.Parameters.IsEmpty);

    private static bool IsInterfaceEnumeration(IForEachLoopOperation loop) =>
        loop is { IsAsynchronous: false, LoopControlVariable: IVariableDeclaratorOperation { Symbol: { RefKind: RefKind.None } variable } }
        && loop.Collection.Type is INamedTypeSymbol { TypeKind: TypeKind.Interface, IsGenericType: true } enumerable
        && enumerable.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
        && SymbolEqualityComparer.Default.Equals(enumerable.TypeArguments[0], variable.Type);

    // (types served from the framework's IL are named as their real types: the slice's namespaces carry a prefix, Frontend.UpstreamSlice)
    private static string MetadataNameOf(INamedTypeSymbol type) =>
        (type.ContainingNamespace is { IsGlobalNamespace: false } ns ? Frontend.UpstreamSlice.RealNamespace(ns.ToDisplayString()) + "." : "") + type.MetadataName;

    /// <summary>
    /// MethodBase.GetCurrentMethod() reports the method it is called from, exactly (`Holder`1[System.String].Get`); a shared body would report
    /// its canonical form, so a method that calls it stays exact (ADR 0026: __Canon is never observable).
    /// </summary>
    private static bool IsMethodObserver(IMethodSymbol method) =>
        method is { Name: "GetCurrentMethod", ContainingType: { Name: "MethodBase", ContainingNamespace: { Name: "Reflection", ContainingNamespace.Name: "System" } } };

    // Frames: StackFrame and StackTrace objects are not supported by csharpc (their constructors throw NotSupportedException), so no
    // program can read a frame's method; whoever implements them must report a shared frame's exact instantiation (or, as .NET does, the
    // definition's). Delegates: Delegate.Method answers from the function a delegate holds, and a delegate to a shared method holds an
    // exact entry of its own (ModuleCodeGen.DelegateEntry).
    /// <summary>The methods a definition's body calls (dependentOnly: only those of type-parameter-dependent types or with dependent type arguments).</summary>
    private List<IMethodSymbol> Calls(IMethodSymbol definition, bool dependentOnly)
    {
        var callees = new List<IMethodSymbol>();
        if (IlCodeOf(definition) is { } code)
        {
            foreach (var (op, symbol) in code.Steps)
                if (op is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
                    && symbol is IMethodSymbol m && (!dependentOnly || CallsDependent(m)))
                    callees.Add(m);
        }
        else if (SourceOf(definition) is { } operations)
        {
            foreach (var op in operations.SelectMany(o => o.DescendantsAndSelf()))
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
