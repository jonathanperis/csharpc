using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Member reflection as NativeAOT does it (docs/adr/0018-reflection-metadata.md): metadata for
/// constructors, methods, their parameters and custom attributes is generated at compile time for the
/// types that reach a [DynamicallyAccessedMembers] location — a generic parameter so annotated
/// instantiated with the type, or `typeof(T)` passed straight to an annotated Type parameter — and only
/// when the program can read it (a Type.GetConstructors()-like member is lowered: the table global is
/// requested). Invocation goes through a generated stub per member. Open generic registrations
/// (`Singleton(typeof(IOptions&lt;&gt;), typeof(OptionsManager&lt;&gt;))`) get the closed instantiations the
/// program can request (the service types it has method tables for), so MakeGenericType finds them.
/// The tables are csrt_ReflectionInfo &amp; co. (src/runtime/csrt.h), read by corelib/Reflection/Members.cs.
/// </summary>
public sealed partial class ModuleCodeGen
{
    // DynamicallyAccessedMemberTypes values csharpc keeps metadata for.
    internal const int DamPublicParameterlessConstructor = 0x1, DamPublicConstructors = 0x3, DamNonPublicConstructors = 0x4,
        DamPublicMethods = 0x8, DamNonPublicMethods = 0x10, DamPublicFields = 0x20, DamNonPublicFields = 0x40,
        DamPublicProperties = 0x200, DamNonPublicProperties = 0x400;
    internal const int DamConstructors = DamPublicConstructors | DamNonPublicConstructors, DamMethods = DamPublicMethods | DamNonPublicMethods,
        DamProperties = DamPublicProperties | DamNonPublicProperties, DamFields = DamPublicFields | DamNonPublicFields;

    // CSRT_RI_* (csrt.h)
    private const uint RiPublicConstructors = 1, RiNonPublicConstructors = 2, RiPublicMethods = 4, RiNonPublicMethods = 8,
        RiPublicProperties = 32, RiNonPublicProperties = 64, RiPublicFields = 256, RiNonPublicFields = 512;

    private readonly Dictionary<ITypeSymbol, int> _damMarks = new(SymbolEqualityComparer.Default);
    private readonly Queue<ITypeSymbol> _damQueue = new();
    // typeof(X).GetMethod("Name") / GetProperty("Name"): only the members with that name (ILLink's rule).
    private readonly Dictionary<ITypeSymbol, List<(int Kinds, string Name)>> _damNames = new(SymbolEqualityComparer.Default);
    private readonly Queue<(ITypeSymbol Type, int Kinds, string Name)> _damNamedQueue = new();
    private readonly HashSet<ISymbol> _damInstantiations = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, List<(int Kinds, string Name)>> _openNamed = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, ReflectionEntry> _reflectionEntries = new(SymbolEqualityComparer.Default);
    private readonly List<ReflectionEntry> _reflectionOrder = [];
    // Open generic registrations: service definition -> implementation definitions (with what they keep).
    private readonly Dictionary<INamedTypeSymbol, List<(INamedTypeSymbol Implementation, int Kinds)>> _openGenericPairs = new(SymbolEqualityComparer.Default);
    private int _closedServicesScanned;
    private LLVMValueRef _reflectionTable, _delegateMethodTable, _delegateSignatureTable;
    // Delegate types the program names (typeof(D)): MethodInfo.CreateDelegate checks a method against their Invoke.
    private readonly HashSet<INamedTypeSymbol> _delegateSignatureTypes = new(SymbolEqualityComparer.Default);
    private readonly Queue<INamedTypeSymbol> _delegateSignatureQueue = new();
    private readonly List<(LLVMValueRef Mt, LLVMValueRef Return, List<LLVMValueRef> Parameters)> _delegateSignatures = [];
    private readonly Dictionary<IMethodSymbol, MethodRecordData> _methodRecords = new(SymbolEqualityComparer.Default);
    // By function name: an IL method that fails to compile is replaced by a stub of the same name (its old value is deleted).
    private readonly Dictionary<nint, (LLVMValueRef Fn, IMethodSymbol Method, bool SourceLambda, string Name)> _delegateCreations = [];
    private readonly Dictionary<nint, (string Fn, MethodRecordData Record)> _delegateMethods = [];
    private readonly HashSet<nint> _delegateDescribed = [];
    private SyntaxNode? _reflectionSyntax;

    private sealed class ReflectionEntry
    {
        public required ITypeSymbol Type;
        public required LLVMValueRef Mt;
        public uint Described;
        public uint Complete; // the Described kinds listed in full (the others: only members asked for by name)
        public HashSet<ISymbol> Listed = new(SymbolEqualityComparer.Default); // the constructors and methods listed so far
        public List<MethodRecordData> Constructors = [], Methods = [];
        public List<AttributeRecordData> Attributes = [];
        public List<PropertyRecordData> Properties = [];
        public List<FieldRecordData> Fields = [];
    }

    /// <summary>A field: accessor stubs (object Get(object), void Set(object, object)), attributes and FieldAttributes.</summary>
    internal sealed record FieldRecordData(LLVMValueRef Name, LLVMValueRef Declaring, LLVMValueRef Type, LLVMValueRef Get, LLVMValueRef Set,
        List<AttributeRecordData> Attributes, uint FieldAttributes);

    /// <summary>A property: its accessors' method records (null: none), attributes, and CSRT_PROPERTY_* flags.</summary>
    internal sealed record PropertyRecordData(IPropertySymbol Property, LLVMValueRef Name, LLVMValueRef Type, MethodRecordData? Getter, MethodRecordData? Setter,
        List<AttributeRecordData> Attributes, uint Flags);

    internal sealed class MethodRecordData
    {
        public required IMethodSymbol Method;
        public required LLVMValueRef Name, Declaring, ReturnType, Invoke;
        // The method's own function, for MethodInfo.CreateDelegate (instance methods of classes; else null).
        public LLVMValueRef Function;
        public required List<ParameterRecordData> Parameters;
        public required List<AttributeRecordData> Attributes;
        public required uint MethodAttributes, Flags;
    }

    internal sealed record ParameterRecordData(LLVMValueRef Name, LLVMValueRef Type, LLVMValueRef DefaultValue, List<AttributeRecordData> Attributes, uint Flags);
    internal sealed record AttributeRecordData(LLVMValueRef Type, LLVMValueRef Create, LLVMValueRef Data);

    /// <summary>A custom attribute as metadata stores it: the constructor, fixed and named arguments.</summary>
    internal sealed record AttributeInstance(IMethodSymbol Constructor, ImmutableArray<AttributeValue> Fixed, ImmutableArray<(string Name, bool IsField, AttributeValue Value)> Named);

    /// <summary>A typed attribute argument: primitives, strings and enums (underlying value), Types, arrays, null.</summary>
    internal sealed record AttributeValue(ITypeSymbol Type, object? Value);

    /// <summary>The program's csrt_ReflectionTable (filled in once codegen is done).</summary>
    internal LLVMValueRef ReflectionTable()
    {
        if (_reflectionTable.IsNull)
            _reflectionTable = Module.AddGlobal("csharpc.reflection", TypeTableHeader, constant: true, linkage: LLVMLinkage.Private);
        return _reflectionTable;
    }

    /// <summary>The delegate signatures behind MethodInfo.CreateDelegate (programs that call it have one).</summary>
    internal LLVMValueRef DelegateSignatureTable()
    {
        if (_delegateSignatureTable.IsNull)
            _delegateSignatureTable = Module.AddGlobal("csharpc.delegatesignatures", TypeTableHeader, constant: true, linkage: LLVMLinkage.Private);
        return _delegateSignatureTable;
    }

    /// <summary>typeof(D) for a delegate type D: its Invoke signature may be needed by CreateDelegate.</summary>
    internal void NoteDelegateType(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod is not null && !TypeSubstitution.IsOpen(type) && _delegateSignatureTypes.Add(type))
            _delegateSignatureQueue.Enqueue(type);
    }

    private bool DescribeDelegateSignatures()
    {
        var any = false;
        while (_delegateSignatureQueue.TryDequeue(out var type))
        {
            any = true;
            var invoke = type.DelegateInvokeMethod!;
            if (invoke.Parameters.Any(p => p.RefKind != RefKind.None || p.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol || p.Type.IsRefLikeType)
                || invoke.RefKind != RefKind.None || invoke.ReturnType.IsRefLikeType || invoke.ReturnType is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
                continue; // by-ref and pointer signatures: CreateDelegate reports them as not supported
            try
            {
                _delegateSignatures.Add((MetadataMethodTable(type), invoke.ReturnsVoid ? LlvmContext.Null(Llvm.Ptr) : MetadataMethodTable(invoke.ReturnType),
                    invoke.Parameters.Select(p => MetadataMethodTable(p.Type)).ToList()));
            }
            catch (CompileError) { }
        }
        return any;
    }

    /// <summary>The csrt_DelegateMethodTable behind Delegate.Method (only programs that read it have one).</summary>
    internal LLVMValueRef DelegateMethodTable()
    {
        if (_delegateMethodTable.IsNull)
            _delegateMethodTable = Module.AddGlobal("csharpc.delegatemethods", TypeTableHeader, constant: true, linkage: LLVMLinkage.Private);
        return _delegateMethodTable;
    }

    // ======================================================================================
    // Which types are reflected over: the [DynamicallyAccessedMembers] dataflow
    // ======================================================================================

    /// <summary>The DynamicallyAccessedMemberTypes an attribute list asks for (0: none).</summary>
    private static readonly bool s_traceDam = Environment.GetEnvironmentVariable("CSHARPC_TRACE_REFLECTION") == "1";

    internal static int DamKindsOf(ImmutableArray<AttributeData> attributes)
    {
        foreach (var a in attributes)
            if (a.AttributeClass is { Name: "DynamicallyAccessedMembersAttribute" } c
                && c.ContainingNamespace?.ToDisplayString() == "System.Diagnostics.CodeAnalysis"
                && a.ConstructorArguments is [{ Value: int kinds }])
                return kinds;
        return 0;
    }

    /// <summary>Keeps metadata for the members of <paramref name="type"/> that <paramref name="kinds"/> names.</summary>
    internal void MarkDynamicallyAccessed(ITypeSymbol type, int kinds, string? name = null)
    {
        kinds &= DamConstructors | DamMethods | DamProperties | DamFields;
        if (s_traceDam) Console.Error.WriteLine($"[csharpc] keep {kinds:x} {(name is null ? "(all)" : name)} of {type}");
        if (kinds == 0 || type is ITypeParameterSymbol || type.TypeKind == TypeKind.Error) return;
        if (type is INamedTypeSymbol named && (named.IsUnboundGenericType || TypeSubstitution.IsOpen(named)))
        {
            // typeof(Impl<>): the closed instantiations the program has keep the same members.
            if (named.IsUnboundGenericType || named.IsDefinition) type = named.OriginalDefinition;
            else return;
            // typeof(Impl<>).GetMethod("Name"): the instantiations keep just that member, as ILLink does
            // (typeof(Expression<>).GetMethod("Compile") must not keep Expression's 400 static factories
            // for every Expression<TDelegate>).
            if (name is not null)
            {
                if (!_openNamed.TryGetValue(type, out var openNames)) _openNamed[type] = openNames = [];
                if (!openNames.Contains((kinds, name)))
                {
                    openNames.Add((kinds, name));
                    _closedServicesScanned = 0;
                }
            }
        }
        var previous = _damMarks.GetValueOrDefault(type);
        if (name is not null)
        {
            if ((previous | kinds) == previous) return;
            if (!_damNames.TryGetValue(type, out var names)) _damNames[type] = names = [];
            if (names.Contains((kinds, name))) return;
            names.Add((kinds, name));
            _damNamedQueue.Enqueue((type, kinds, name));
            return;
        }
        if ((previous | kinds) == previous) return;
        _damMarks[type] = previous | kinds;
        _damQueue.Enqueue(type);
        if (type is INamedTypeSymbol { IsGenericType: true, IsDefinition: true }) _closedServicesScanned = 0; // its instantiations too
    }

    /// <summary>
    /// `Type.GetType("Ns.Name")` / `assembly.GetType("Ns.Name")` with a constant name: the type is kept (it gets a
    /// method table, so the run-time lookup finds it), as ILLink keeps it for NativeAOT. Generic and
    /// assembly-qualified names resolve as metadata names; a name that resolves to nothing keeps nothing.
    /// </summary>
    internal void NoteTypeByName(string name)
    {
        var comma = name.IndexOf(',');
        if (comma >= 0 && name.IndexOf('[') < 0) name = name[..comma];
        if (name.IndexOfAny(['[', '*', '&']) >= 0) return;
        if (Compilation.GetTypeByMetadataName(name.Trim()) is not { } type || type.IsGenericType || type.TypeKind == TypeKind.Error) return;
        try { MetadataMethodTable(type); }
        catch (CompileError) { }
    }

    /// <summary>A generic method or type instantiation: its annotated type parameters keep members of their arguments.</summary>
    internal void NoteDynamicallyAccessedInstantiation(ISymbol symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol { IsGenericMethod: true } method when !method.IsDefinition && !TypeSubstitution.IsOpen(method):
                if (!_damInstantiations.Add(method)) break;
                for (var i = 0; i < method.TypeParameters.Length; i++)
                    if (DamKindsOf(method.OriginalDefinition.TypeParameters[i].GetAttributes()) is var kinds and not 0)
                        MarkDynamicallyAccessed(method.TypeArguments[i], kinds);
                if (method.ContainingType is { } owner) NoteDynamicallyAccessedInstantiation(owner);
                break;
            case IMethodSymbol { ContainingType: { } declaring }:
                NoteDynamicallyAccessedInstantiation(declaring);
                break;
            case INamedTypeSymbol { IsGenericType: true } type when !type.IsDefinition && !type.IsUnboundGenericType && !TypeSubstitution.IsOpen(type):
                for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
                {
                    if (!_damInstantiations.Add(t)) break;
                    for (var i = 0; i < t.TypeParameters.Length; i++)
                        if (DamKindsOf(t.OriginalDefinition.TypeParameters[i].GetAttributes()) is var kinds and not 0)
                            MarkDynamicallyAccessed(t.TypeArguments[i], kinds);
                }
                break;
        }
    }

    /// <summary>
    /// A call site: a `typeof(T)` argument for a parameter annotated with [DynamicallyAccessedMembers] keeps
    /// T's members; two open-generic typeof arguments, one of them annotated, register an implementation
    /// for a service (DI's ServiceDescriptor(typeof(IOptions&lt;&gt;), typeof(OptionsManager&lt;&gt;))).
    /// </summary>
    internal void NoteDynamicallyAccessedArguments(IMethodSymbol method, Func<int, ITypeSymbol?> typeofArgument)
    {
        INamedTypeSymbol? openArgument = null;
        (INamedTypeSymbol Type, int Kinds)? openAnnotated = null;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (typeofArgument(i) is not { } type) continue;
            var kinds = DamKindsOf(method.OriginalDefinition.Parameters[i].GetAttributes());
            if (type is INamedTypeSymbol { IsGenericType: true } open && (open.IsUnboundGenericType || open.IsDefinition))
            {
                if (kinds != 0 && openAnnotated is null) openAnnotated = (open.OriginalDefinition, kinds);
                else openArgument ??= open.OriginalDefinition;
            }
            if (kinds != 0) MarkDynamicallyAccessed(type, kinds);
        }
        if (openArgument is { } service && openAnnotated is { } implementation && service.Arity == implementation.Type.Arity
            && !SymbolEqualityComparer.Default.Equals(service, implementation.Type))
        {
            if (!_openGenericPairs.TryGetValue(service, out var list)) _openGenericPairs[service] = list = [];
            if (!list.Any(p => SymbolEqualityComparer.Default.Equals(p.Implementation, implementation.Type)))
            {
                list.Add(implementation);
                _closedServicesScanned = 0; // rescan the services seen so far for this new pair
            }
        }
    }

    /// <summary>
    /// Closes over the instantiations the program can ask for: a closed instantiation of a marked open
    /// definition keeps the definition's members; one of a registered service definition gets the registered
    /// implementation instantiated with the same arguments, when they satisfy its constraints (DI's
    /// implementationType.MakeGenericType(serviceType.GenericTypeArguments)).
    /// </summary>
    private bool CloseOpenGenericRegistrations()
    {
        var any = false;
        for (; _closedServicesScanned < _typeMts.Count; _closedServicesScanned++)
        {
            if (_typeMts[_closedServicesScanned].Type is not INamedTypeSymbol { IsGenericType: true } closedType
                || closedType.IsDefinition || TypeSubstitution.IsOpen(closedType))
                continue;
            if (_damMarks.TryGetValue(closedType.OriginalDefinition, out var definitionKinds))
            {
                MarkDynamicallyAccessed(closedType, definitionKinds);
                any = true;
            }
            if (_openNamed.TryGetValue(closedType.OriginalDefinition, out var openNames))
                foreach (var (namedKinds, name) in openNames.ToList())
                {
                    MarkDynamicallyAccessed(closedType, namedKinds, name);
                    any = true;
                }
            if (!_openGenericPairs.TryGetValue(closedType.OriginalDefinition, out var implementations)) continue;
            foreach (var (implementation, kinds) in implementations)
            {
                if (!SatisfiesConstraints(implementation, closedType.TypeArguments)) continue;
                // A bounded nesting depth keeps a registration from instantiating itself forever.
                if (closedType.TypeArguments.Any(a => ReflectionNestingDepth(a) > 4)) continue;
                var closed = implementation.Construct([.. closedType.TypeArguments]);
                try
                {
                    TypeMethodTable(closed, null!);
                    MarkDynamicallyAccessed(closed, kinds);
                    any = true;
                }
                catch (CompileError) { } // not representable: MakeGenericType throws NotSupportedException at run time
            }
        }
        return any;
    }

    private static int ReflectionNestingDepth(ITypeSymbol type) => type is INamedTypeSymbol { IsGenericType: true } n
        ? 1 + n.TypeArguments.Select(ReflectionNestingDepth).DefaultIfEmpty(0).Max() : type is IArrayTypeSymbol a ? ReflectionNestingDepth(a.ElementType) : 0;

    /// <summary>The CLR's constraint checks (class, struct, new(), base types and interfaces), as MakeGenericType applies them.</summary>
    private bool SatisfiesConstraints(INamedTypeSymbol definition, ImmutableArray<ITypeSymbol> arguments)
    {
        var subst = TypeSubstitution.Identity.With(definition.TypeParameters, arguments, Compilation);
        for (var i = 0; i < definition.TypeParameters.Length; i++)
        {
            var p = definition.TypeParameters[i];
            var a = arguments[i];
            if (p.HasReferenceTypeConstraint && !a.IsReferenceType) return false;
            if (p.HasValueTypeConstraint && (!a.IsValueType || TypeMapper.IsNullable(a))) return false;
            if (p.HasUnmanagedTypeConstraint && !a.IsUnmanagedType) return false;
            if (p.HasConstructorConstraint && !a.IsValueType
                && (a.IsAbstract || a is not INamedTypeSymbol { InstanceConstructors: var ctors } || !ctors.Any(c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public)))
                return false;
            foreach (var constraint in p.ConstraintTypes)
            {
                var conversion = ((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)Compilation).ClassifyConversion(a, subst.Apply(constraint));
                if (!(conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing)) return false;
            }
        }
        return true;
    }

    // ======================================================================================
    // Describing the marked types
    // ======================================================================================

    /// <summary>Describes newly marked types; true when anything was created (the worklists must drain again).</summary>
    private bool EmitReflectionMetadata()
    {
        if (_reflectionTable.IsNull && _delegateMethodTable.IsNull) return DescribeDelegateSignatures();
        var any = CloseOpenGenericRegistrations() | DescribeDelegateMethods() | DescribeDelegateSignatures();
        if (_reflectionTable.IsNull) return any;
        any |= DescribeTypeAttributes();
        while (_damQueue.TryDequeue(out var type))
        {
            any = true;
            try { Describe(type, _damMarks[type]); }
            catch (CompileError error) { Errors.Add(error); }
        }
        while (_damNamedQueue.TryDequeue(out var named))
        {
            any = true;
            try { Describe(named.Type, named.Kinds, named.Name); }
            catch (CompileError error) { Errors.Add(error); }
        }
        return any;
    }

    private void Describe(ITypeSymbol type, int kinds)
    {
        // Members listed by name first: list everything again in RuntimeType's order, then the named ones not
        // covered (a GetMethod("Name") on kinds still described by name only).
        if (_reflectionEntries.TryGetValue(type, out var entry) && entry.Described != entry.Complete)
        {
            entry.Constructors.Clear();
            entry.Methods.Clear();
            entry.Properties.Clear();
            entry.Fields.Clear();
            entry.Listed.Clear();
            entry.Described = entry.Complete = 0;
            Describe(type, kinds, null);
            foreach (var (namedKinds, name) in _damNames.GetValueOrDefault(type) ?? []) Describe(type, namedKinds, name);
            return;
        }
        Describe(type, kinds, null);
    }

    // ======================================================================================
    // A type's own custom attributes (Type.GetCustomAttributes/GetCustomAttributesData/IsDefined)
    // ======================================================================================
    // Once the program asks a Type for its attributes, the types reflection can see get them
    // (CSRT_RI_ATTRIBUTES, possibly an empty list): the program's own types, the types kept for
    // [DynamicallyAccessedMembers], and every generic parameter (dependency injection reads the annotations
    // of open generics' parameters; a parameter without an entry has none). Other types have no entry and
    // the query throws NotSupportedException, as their attribute classes aren't compiled in. Where csharpc
    // can't know the exact list .NET returns, the entry says so (CSRT_RI_ATTRIBUTES_UNKNOWN) and the query
    // throws rather than answering with a different list.

    private const uint RiAttributes = 16, RiAttributesUnknown = 128;
    private bool _typeAttributesQueried;
    private int _typeAttributesScanned;

    internal void NoteTypeAttributesQueried()
    {
        _typeAttributesQueried = true;
        ReflectionTable();
    }

    private bool DescribeTypeAttributes()
    {
        if (!_typeAttributesQueried) return false;
        var any = false;
        for (; _typeAttributesScanned < _typeMts.Count; _typeAttributesScanned++)
        {
            var (mt, described) = _typeMts[_typeAttributesScanned];
            ITypeSymbol key;
            List<AttributeInstance>? attributes;
            switch (described)
            {
                case INamedTypeSymbol named when DescribesOwnAttributes(named) && named.OriginalDefinition.Locations.Any(l => l.IsInSource):
                    key = named;
                    attributes = Decoded(() => TypeAttributeInstances(named.OriginalDefinition));
                    break;
                case GenericParameter parameter:
                    key = parameter.Parameter;
                    attributes = Decoded(() => GenericParameterAttributeInstances(parameter.Parameter));
                    break;
                default:
                    continue;
            }
            if (attributes is { Count: 0 } && key is ITypeParameterSymbol) continue;
            if (!_reflectionEntries.TryGetValue(key, out var entry))
            {
                entry = new ReflectionEntry { Type = key, Mt = mt };
                _reflectionEntries[key] = entry;
                _reflectionOrder.Add(entry);
            }
            SetTypeAttributes(entry, attributes);
            any = true;
        }
        // Types kept for [DynamicallyAccessedMembers] (whenever their entry appeared).
        for (var i = 0; i < _reflectionOrder.Count; i++)
        {
            var entry = _reflectionOrder[i];
            if ((entry.Described & (RiAttributes | RiAttributesUnknown)) != 0 || entry.Type is not INamedTypeSymbol named || !DescribesOwnAttributes(named)) continue;
            SetTypeAttributes(entry, Decoded(() => TypeAttributeInstances(named.OriginalDefinition)));
            any = true;
        }
        return any;
    }

    private static bool DescribesOwnAttributes(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate && !type.IsAnonymousType && !type.IsTupleType;

    private void SetTypeAttributes(ReflectionEntry entry, List<AttributeInstance>? attributes)
    {
        if (attributes is null) entry.Described |= RiAttributesUnknown;
        else
        {
            entry.Attributes = Attributes(attributes);
            entry.Described |= RiAttributes;
        }
    }

    /// <summary>An attribute csharpc can't decode yet (a typeof argument it can't represent...) makes the list unknown.</summary>
    private static List<AttributeInstance>? Decoded(Func<List<AttributeInstance>?> decode)
    {
        try { return decode(); }
        catch (CompileError) { return null; }
    }

    /// <summary>A type definition's attributes as RuntimeType reports them; null when csharpc can't know them exactly.</summary>
    private List<AttributeInstance>? TypeAttributeInstances(INamedTypeSymbol definition)
    {
        var il = definition.MetadataToken != 0 ? Il.OwnerOf(definition) : null;
        if (il is not null)
        {
            // [Serializable] and [ComImport] are pseudo-attributes CoreCLR adds to the list.
            if ((il.AttributesOf(definition) & (System.Reflection.TypeAttributes.Serializable | System.Reflection.TypeAttributes.Import)) != 0) return null;
            return DecodeAttributes(il, il.TypeDefinitionOf(definition).GetCustomAttributes(), IlGenericContext.For(definition)).ToList();
        }
        // Reference-assembly types (the BCL): Roslyn hides the attributes the compiler decodes, and reference
        // assemblies differ from the implementation .NET loads.
        if (definition.MetadataToken != 0 || !definition.Locations.Any(l => l.IsInSource)) return null;
        // Source types: Roslyn's symbols omit the attributes the compiler synthesizes into metadata.
        if (SynthesizesTypeAttributes(definition)) return null;
        return definition.GetAttributes().Select(FromAttributeData).OfType<AttributeInstance>().ToList();
    }

    /// <summary>Whether Roslyn emits attributes for a source type that its symbol doesn't list (Nullable, DefaultMember, Extension, IsReadOnly...).</summary>
    private bool SynthesizesTypeAttributes(INamedTypeSymbol type)
    {
        if (type.IsRecord || type.IsReadOnly || type.IsRefLikeType || type.IsSerializable) return true;
        if (type.GetMembers().Any(m => m is IPropertySymbol { IsIndexer: true } or IMethodSymbol { IsExtensionMethod: true }
                or IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            return true;
        // Nullable annotations in the declaration become [NullableContext]/[Nullable] attributes.
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            var model = Compilation.GetSemanticModel(reference.SyntaxTree);
            if (model.GetNullableContext(syntax.SpanStart).AnnotationsEnabled() || model.GetNullableContext(syntax.Span.End).AnnotationsEnabled()
                || syntax.DescendantTrivia().Any(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullableDirectiveTrivia)))
                return true;
        }
        return false;
    }

    /// <summary>A generic parameter's attributes; null when csharpc can't know them exactly.</summary>
    private List<AttributeInstance>? GenericParameterAttributeInstances(ITypeParameterSymbol parameter)
    {
        var owner = parameter.ContainingSymbol.OriginalDefinition;
        var il = owner.MetadataToken != 0 ? Il.OwnerOf(owner) : null;
        if (il is not null)
        {
            var handles = owner is INamedTypeSymbol type ? il.TypeDefinitionOf(type).GetGenericParameters() : il.MethodDefinitionOf((IMethodSymbol)owner).GetGenericParameters();
            var row = il.Reader.GetGenericParameter(handles[parameter.Ordinal]);
            return DecodeAttributes(il, row.GetCustomAttributes(), owner is INamedTypeSymbol t ? IlGenericContext.For(t) : IlGenericContext.For((IMethodSymbol)owner)).ToList();
        }
        if (owner.MetadataToken != 0 || !owner.Locations.Any(l => l.IsInSource)) return null;
        // Source: [Nullable] on constrained or annotated parameters is synthesized.
        if (parameter.HasNotNullConstraint || parameter.ConstraintTypes.Length > 0
            || (owner is INamedTypeSymbol ownerType ? SynthesizesTypeAttributes(ownerType) : owner.ContainingType is { } c && SynthesizesTypeAttributes(c)))
            return null;
        return parameter.GetAttributes().Select(FromAttributeData).OfType<AttributeInstance>().ToList();
    }

    /// <summary>Lists the members <paramref name="kinds"/> names; with <paramref name="name"/>, only those called that.</summary>
    private void Describe(ITypeSymbol type, int kinds, string? name)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind is TypeKind.Error) return;
        var real = RealType(named) as INamedTypeSymbol ?? named;
        if (!_reflectionEntries.TryGetValue(type, out var entry))
        {
            entry = new ReflectionEntry { Type = type, Mt = MetadataMethodTable(type) };
            _reflectionEntries[type] = entry;
            _reflectionOrder.Add(entry);
        }
        // Open definitions (typeof(Impl<>)) list no members: their instantiations do.
        if (named.IsDefinition && named.IsGenericType) return;
        uint wanted = ((kinds & DamPublicConstructors) != 0 ? RiPublicConstructors : 0) | ((kinds & DamNonPublicConstructors) != 0 ? RiNonPublicConstructors : 0)
            | ((kinds & DamPublicMethods) != 0 ? RiPublicMethods : 0) | ((kinds & DamNonPublicMethods) != 0 ? RiNonPublicMethods : 0)
            | ((kinds & DamPublicProperties) != 0 ? RiPublicProperties : 0) | ((kinds & DamNonPublicProperties) != 0 ? RiNonPublicProperties : 0)
            | ((kinds & DamPublicFields) != 0 ? RiPublicFields : 0) | ((kinds & DamNonPublicFields) != 0 ? RiNonPublicFields : 0);
        var added = wanted & ~entry.Complete;
        if (name is null) added &= ~entry.Described;
        if (added == 0) return;
        entry.Described |= added;
        if (name is null) entry.Complete |= added;
        bool Wanted(ISymbol member, uint publicBit, uint nonPublicBit) =>
            (added & (member.DeclaredAccessibility == Accessibility.Public ? publicBit : nonPublicBit)) != 0
            && (name is null || member.MetadataName == name) && !entry.Listed.Contains(member);
        foreach (var ctor in real.InstanceConstructors)
        {
            if (ctor.IsImplicitlyDeclared && real.IsValueType) continue; // a struct's default constructor isn't metadata
            if (name is null && Wanted(ctor, RiPublicConstructors, RiNonPublicConstructors) && entry.Listed.Add(ctor))
                entry.Constructors.Add(MethodRecord(ctor, invoker: true));
        }
        if ((added & (RiPublicProperties | RiNonPublicProperties)) != 0) DescribeProperties(entry, real, added, name);
        if ((added & (RiPublicFields | RiNonPublicFields)) != 0) DescribeFields(entry, real, added, name);
        if ((added & (RiPublicMethods | RiNonPublicMethods)) == 0) return;
        // Declared methods, then inherited ones not overridden (RuntimeType's order: most derived first).
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var listed in entry.Methods) seen.Add(ClassLayout.RootOf(listed.Method.OriginalDefinition));
        for (INamedTypeSymbol? t = real; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
            {
                if (m.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet
                        or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.UserDefinedOperator or MethodKind.Conversion
                        or MethodKind.ExplicitInterfaceImplementation or MethodKind.DelegateInvoke))
                    continue;
                if (!SymbolEqualityComparer.Default.Equals(t, real) && m.DeclaredAccessibility == Accessibility.Private) continue;
                if (!Wanted(m, RiPublicMethods, RiNonPublicMethods)) continue;
                var root = ClassLayout.RootOf(m.OriginalDefinition);
                if ((m.IsVirtual || m.IsOverride || m.IsAbstract) && !seen.Add(root)) continue; // overridden lower down
                entry.Listed.Add(m);
                entry.Methods.Add(MethodRecord(m, invoker: true));
            }
        }
    }

    /// <summary>Declared properties, then inherited ones not overridden; public when an accessor is (RuntimePropertyInfo's rule).</summary>
    private void DescribeProperties(ReflectionEntry entry, INamedTypeSymbol real, uint added, string? name)
    {
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var listed in entry.Properties) seen.Add(PropertyRoot(listed.Property));
        for (INamedTypeSymbol? t = real; t is not null; t = t.BaseType)
        {
            foreach (var p in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (name is not null && p.MetadataName != name) continue;
                bool isPublic = p.GetMethod?.DeclaredAccessibility == Accessibility.Public || p.SetMethod?.DeclaredAccessibility == Accessibility.Public;
                if (!SymbolEqualityComparer.Default.Equals(t, real) && !isPublic
                    && (p.GetMethod?.DeclaredAccessibility ?? Accessibility.Private) == Accessibility.Private
                    && (p.SetMethod?.DeclaredAccessibility ?? Accessibility.Private) == Accessibility.Private)
                    continue; // inherited private properties aren't listed
                if ((added & (isPublic ? RiPublicProperties : RiNonPublicProperties)) == 0) continue;
                if (!seen.Add(PropertyRoot(p))) continue; // overridden lower down
                var definition = p.OriginalDefinition;
                var il = definition.MetadataToken != 0 ? Il.OwnerOf(definition) : null;
                var attributes = il is not null
                    ? DecodeAttributes(il, il.PropertyDefinitionOf(definition).GetCustomAttributes(), IlGenericContext.For(definition.ContainingType)).ToList()
                    : definition.GetAttributes().Select(FromAttributeData).OfType<AttributeInstance>().ToList();
                uint flags = (isPublic ? 1u : 0u) | (p.IsStatic ? 2u : 0u);
                entry.Properties.Add(new PropertyRecordData(p, CString(p.MetadataName), MetadataMethodTable(p.Type),
                    p.GetMethod is { } getter ? MethodRecord(getter, invoker: true) : null,
                    p.SetMethod is { } setter ? MethodRecord(setter, invoker: true) : null, Attributes(attributes), flags));
            }
        }
    }

    /// <summary>
    /// Declared fields, then the non-private fields of base types (RuntimeType's
    /// PopulateFields). A reference-assembly type in the chain hides its non-public fields, so those
    /// aren't listed at all: asking for them throws instead of answering with fewer fields than .NET.
    /// </summary>
    private void DescribeFields(ReflectionEntry entry, INamedTypeSymbol real, uint added, string? name)
    {
        for (INamedTypeSymbol? t = real; t is not null; t = t.BaseType)
        {
            if (t.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType or SpecialType.System_Enum) break;
            if (t.OriginalDefinition.MetadataToken != 0 && Il.OwnerOf(t.OriginalDefinition) is null) Unknown(RiNonPublicFields);
            var own = SymbolEqualityComparer.Default.Equals(t, real);
            if (own && t.TypeKind == TypeKind.Enum && (added & RiPublicFields) != 0 && (name is null || name == "value__"))
            {
                // The enum's instance field: Roslyn has no symbol for it.
                var underlying = t.EnumUnderlyingType!;
                var message = $"Reading 'value__' of '{t.ToDisplayString()}' through reflection is not supported by csharpc yet.";
                var stub = SyntheticFunction(Mangler.TypeSymbol(t, "value__$reflectget"), Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr), (g, _) => g.EmitThrowingFactory(message));
                var setStub = SyntheticFunction(Mangler.TypeSymbol(t, "value__$reflectset"), Llvm.FunctionType(Llvm.Void, Llvm.Ptr, Llvm.Ptr), (g, _) => g.EmitThrowingFactory(message));
                entry.Fields.Add(new FieldRecordData(CString("value__"), MetadataMethodTable(t), MetadataMethodTable(underlying), stub, setStub, [], 0x606));
            }
            // CoreCLR's order: instance fields, then static ones (its FieldDescs), then constants (metadata only).
            foreach (var f in t.GetMembers().OfType<IFieldSymbol>().OrderBy(f => f.IsConst ? 2 : f.IsStatic ? 1 : 0))
            {
                if (name is not null && f.MetadataName != name) continue;
                if (!own && f.DeclaredAccessibility == Accessibility.Private) continue;
                var bit = f.DeclaredAccessibility == Accessibility.Public ? RiPublicFields : RiNonPublicFields;
                if ((added & bit) == 0 || entry.Listed.Contains(f)) continue;
                FieldRecordData? record = null;
                if (!f.IsFixedSizeBuffer && !f.Type.IsRefLikeType && f.RefKind == RefKind.None && f.Type is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol))
                    try { record = FieldRecord(f); }
                    catch (CompileError) { }
                if (record is null)
                {
                    Unknown(bit); // a field csharpc can't describe: listing that kind throws instead of omitting it
                    continue;
                }
                entry.Listed.Add(f);
                entry.Fields.Add(record);
            }
        }

        void Unknown(uint bit)
        {
            entry.Described &= ~bit;
            entry.Complete &= ~bit;
            added &= ~bit;
        }
    }

    private FieldRecordData FieldRecord(IFieldSymbol field)
    {
        var definition = field.OriginalDefinition;
        var il = definition.MetadataToken != 0 ? Il.OwnerOf(definition) : null;
        uint attributes;
        List<AttributeInstance> instances;
        if (il is not null)
        {
            var row = il.FieldDefinitionOf(definition);
            attributes = (uint)row.Attributes;
            instances = DecodeAttributes(il, row.GetCustomAttributes(), IlGenericContext.For(definition.ContainingType)).ToList();
        }
        else
        {
            attributes = field.DeclaredAccessibility switch
            {
                Accessibility.Private => 1u, Accessibility.ProtectedAndInternal => 2u, Accessibility.Internal => 3u,
                Accessibility.Protected => 4u, Accessibility.ProtectedOrInternal => 5u, _ => 6u,
            };
            if (field.IsStatic) attributes |= 0x10;
            if (field.IsReadOnly) attributes |= 0x20;
            if (field.IsConst) attributes |= 0x40 | 0x8000; // Literal | HasDefault
            instances = definition.GetAttributes().Select(FromAttributeData).OfType<AttributeInstance>().ToList();
            // An auto-property's backing field: the attributes the compiler emits on it.
            if (field.IsImplicitlyDeclared && field.AssociatedSymbol is IPropertySymbol)
            {
                if (PseudoAttribute("System.Runtime.CompilerServices.CompilerGeneratedAttribute") is { } generated) instances.Add(generated);
                if (Compilation.GetTypeByMetadataName("System.Diagnostics.DebuggerBrowsableAttribute") is { } browsable
                    && Compilation.GetTypeByMetadataName("System.Diagnostics.DebuggerBrowsableState") is { } state
                    && browsable.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == 1) is { } ctor)
                    instances.Add(new AttributeInstance(ctor, [new AttributeValue(state, 0)], []));
            }
            else if (field.IsImplicitlyDeclared)
                throw CompileError.Unsupported($"reflection over the compiler-generated field '{field}'", field);
        }
        return new FieldRecordData(CString(field.MetadataName), MetadataMethodTable(field.ContainingType), MetadataMethodTable(field.Type),
            FieldAccessor(field, set: false), FieldAccessor(field, set: true), Attributes(instances), attributes);
    }

    /// <summary>object Get(object target) / void Set(object target, object value); one that throws when csharpc can't access the field.</summary>
    private LLVMValueRef FieldAccessor(IFieldSymbol field, bool set)
    {
        if (_fieldAccessors.TryGetValue((field, set), out var existing)) return existing;
        return _fieldAccessors[(field, set)] = NewFieldAccessor(field, set);
    }

    private readonly Dictionary<(IFieldSymbol, bool), LLVMValueRef> _fieldAccessors = new(FieldAccessorKey.Instance);

    private sealed class FieldAccessorKey : IEqualityComparer<(IFieldSymbol Field, bool Set)>
    {
        public static readonly FieldAccessorKey Instance = new();
        public bool Equals((IFieldSymbol Field, bool Set) x, (IFieldSymbol Field, bool Set) y) => x.Set == y.Set && SymbolEqualityComparer.Default.Equals(x.Field, y.Field);
        public int GetHashCode((IFieldSymbol Field, bool Set) key) => SymbolEqualityComparer.Default.GetHashCode(key.Field) * 2 + (key.Set ? 1 : 0);
    }

    private LLVMValueRef NewFieldAccessor(IFieldSymbol field, bool set)
    {
        var name = Mangler.TypeSymbol(field.ContainingType, field.Name + (set ? "$reflectset" : "$reflectget"));
        var type = set ? Llvm.FunctionType(Llvm.Void, Llvm.Ptr, Llvm.Ptr) : Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr);
        try { return SyntheticFunction(name, type, (g, syntax) => g.EmitFieldAccessor(field, set, syntax)); }
        catch (CompileError)
        {
            var message = $"{(set ? "Setting" : "Reading")} the field '{field.ToDisplayString()}' through reflection is not supported by csharpc yet.";
            return SyntheticFunction(name, type, (g, _) => g.EmitThrowingFactory(message));
        }
    }

    private static ISymbol PropertyRoot(IPropertySymbol p)
    {
        while (p.OverriddenProperty is { } overridden) p = overridden;
        return p.OriginalDefinition;
    }

    internal MethodRecordData MethodRecord(IMethodSymbol method, bool invoker)
    {
        if (_methodRecords.TryGetValue(method, out var existing) && (!invoker || !existing.Invoke.IsNull)) return existing;
        var none = LlvmContext.Null(Llvm.Ptr);
        var parameters = new List<ParameterRecordData>();
        var definition = method.OriginalDefinition;
        var il = definition.MetadataToken != 0 ? Il.OwnerOf(definition) : null;
        var ilParameters = il is not null ? IlParameters(il, definition) : null;
        foreach (var p in method.Parameters)
        {
            (IlAssembly, Parameter)? row = ilParameters is not null && ilParameters.TryGetValue(p.Ordinal + 1, out var r) ? r : null;
            uint flags = ParameterAttributesOf(p, row);
            if (p.RefKind != RefKind.None) flags |= 1u << 17; // CSRT_PARAM_BYREF
            var defaultValue = none;
            if (p.HasExplicitDefaultValue)
            {
                flags |= 1u << 16; // CSRT_PARAM_HAS_DEFAULT
                defaultValue = DefaultValueFactory(p);
            }
            var attributes = Attributes(ParameterAttributeInstances(p, row));
            parameters.Add(new ParameterRecordData(p.Name.Length == 0 ? none : CString(p.Name), MetadataMethodTable(p.Type), defaultValue, attributes, flags));
        }
        var invokable = invoker
            && method.Parameters.All(p => p.RefKind == RefKind.None && p.Type is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol) && !p.Type.IsRefLikeType)
            && method.ReturnType is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol) && !method.ReturnType.IsRefLikeType && method.RefKind == RefKind.None
            && !method.IsAbstract && !(method.MethodKind == MethodKind.Constructor && method.ContainingType.IsAbstract)
            && !TypeSubstitution.IsOpen(method);
        var record = new MethodRecordData
        {
            Method = method,
            Name = CString(method.MethodKind == MethodKind.Constructor ? ".ctor" : method.MetadataName),
            Declaring = MetadataMethodTable(method.ContainingType),
            ReturnType = method.ReturnsVoid || method.MethodKind == MethodKind.Constructor ? none : MetadataMethodTable(method.ReturnType),
            Invoke = invokable ? ReflectionInvoker(method) : none,
            // An instance method of a class, called as the delegate calls its function: (target, args...).
            Function = invokable && !method.IsStatic && method.MethodKind == MethodKind.Ordinary && method.ContainingType.IsReferenceType
                && !TypeSubstitution.IsOpen(method.ContainingType) && IsUserMethod(method) ? GetFunction(method).Fn
                // A static method, through the thunk with the delegate calling convention (an ignored target first).
                : invokable && method is { IsStatic: true, MethodKind: MethodKind.Ordinary } && IsUserMethod(method) ? StaticThunk(method)
                : none,
            Parameters = parameters,
            Attributes = Attributes(MemberAttributeInstances(method, il)),
            MethodAttributes = MethodAttributesOf(method),
            Flags = method.IsOverride && InheritsNoAttributes(method) ? 2u /* CSRT_METHOD_NO_INHERITED_ATTRIBUTES */ : 0,
        };
        // The reflection stub is a root of the program: what it calls is reachable (IL failures there are errors).
        if (invokable) NoteReference(method, virtualCall: method.MethodKind != MethodKind.Constructor);
        return _methodRecords[method] = record;
    }

    /// <summary>
    /// GetCustomAttributes(inherit: true) of an override adds the inheritable attributes of the methods it
    /// overrides: when none of them has one, the answer is the method's own list.
    /// </summary>
    private bool InheritsNoAttributes(IMethodSymbol method)
    {
        for (var m = method.OverriddenMethod; m is not null; m = m.OverriddenMethod)
        {
            var definition = m.OriginalDefinition;
            var il = definition.MetadataToken != 0 ? Il.OwnerOf(definition) : null;
            IEnumerable<AttributeInstance> attributes;
            try { attributes = MemberAttributeInstances(m, il); }
            catch (CompileError) { return false; }
            if (attributes.Any(a => IsInheritedAttribute(a.Constructor.ContainingType))) return false;
        }
        return true;
    }

    /// <summary>[AttributeUsage(Inherited = ...)] of an attribute class (true when not stated, as in .NET).</summary>
    private static bool IsInheritedAttribute(INamedTypeSymbol attribute)
    {
        for (var t = attribute; t is not null; t = t.BaseType)
            if (t.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute") is { } usage)
                return usage.NamedArguments.FirstOrDefault(n => n.Key == "Inherited").Value.Value as bool? ?? true;
        return true;
    }

    /// <summary>The method's MethodAttributes: from metadata for IL methods, as Roslyn emits them for source.</summary>
    private uint MethodAttributesOf(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        if (definition.MetadataToken != 0 && Il.OwnerOf(definition) is { } il)
            return (uint)il.MethodDefinitionOf(definition).Attributes;
        uint access = definition.DeclaredAccessibility switch
        {
            Accessibility.Private => 1, Accessibility.ProtectedAndInternal => 2, Accessibility.Internal => 3,
            Accessibility.Protected => 4, Accessibility.ProtectedOrInternal => 5, Accessibility.Public => 6, _ => 1,
        };
        uint flags = access | 0x80; // C# emits hidebysig
        if (definition.IsStatic) flags |= 0x10;
        var implementsInterface = !definition.IsStatic && definition.MethodKind != MethodKind.Constructor
            && (definition.ExplicitInterfaceImplementations.Length > 0 || ImplementsInterfaceMember(definition));
        if (definition.IsVirtual || definition.IsAbstract || definition.IsOverride || implementsInterface)
        {
            flags |= 0x40;
            if (!definition.IsOverride) flags |= 0x100; // newslot
            if (definition.IsSealed || (implementsInterface && !definition.IsVirtual && !definition.IsAbstract && !definition.IsOverride)) flags |= 0x20; // final
        }
        if (definition.IsAbstract) flags |= 0x400;
        if (definition.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor) flags |= 0x800 | 0x1000;
        else if (definition.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove
                 or MethodKind.UserDefinedOperator or MethodKind.Conversion) flags |= 0x800;
        if (definition.GetDllImportData() is not null) flags |= 0x2000;
        return flags;
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        foreach (var itf in method.ContainingType.AllInterfaces)
            foreach (var member in itf.GetMembers().OfType<IMethodSymbol>())
                if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method)) return true;
        return false;
    }

    /// <summary>The parameter rows of an IL method by sequence number (0: the return value).</summary>
    private static Dictionary<int, (IlAssembly Assembly, Parameter Row)> IlParameters(IlAssembly il, IMethodSymbol method)
    {
        var rows = new Dictionary<int, (IlAssembly, Parameter)>();
        foreach (var handle in il.MethodDefinitionOf(method).GetParameters())
        {
            var row = il.Reader.GetParameter(handle);
            rows[row.SequenceNumber] = (il, row);
        }
        return rows;
    }

    private static uint ParameterAttributesOf(IParameterSymbol p, (IlAssembly Assembly, Parameter Row)? row)
    {
        if (row is { } r) return (uint)r.Row.Attributes & 0xFFFF;
        uint flags = 0;
        if (p.RefKind == RefKind.Out) flags |= 0x2;                                // Out
        if (p.RefKind is RefKind.In or RefKind.RefReadOnlyParameter) flags |= 0x1; // In
        if (p.IsOptional) flags |= 0x10;                                           // Optional
        if (p.HasExplicitDefaultValue && p.Type.SpecialType is not (SpecialType.System_Decimal or SpecialType.System_DateTime)) flags |= 0x1000; // HasDefault
        return flags;
    }

    // ======================================================================================
    // Custom attributes
    // ======================================================================================

    private List<AttributeRecordData> Attributes(IEnumerable<AttributeInstance> instances)
    {
        var list = new List<AttributeRecordData>();
        if (Compilation.GetTypeByMetadataName("System.Attribute") is { } attribute) ArrayMethodTable(attribute); // Attribute.GetCustomAttributes' Attribute[]
        foreach (var instance in instances)
        {
            var type = instance.Constructor.ContainingType;
            // GetCustomAttributes(typeof(A), ...) returns an A[]: the program needs that array type.
            ArrayMethodTable(type);
            var key = AttributeKey(instance);
            if (!_attributeFactories.TryGetValue(key, out var factories))
                _attributeFactories[key] = factories = (AttributeFactory(instance), AttributeDataFactory(instance));
            list.Add(new AttributeRecordData(MetadataMethodTable(type), factories.Create, factories.Data));
        }
        return list;
    }

    // Identical applications (the compiler's [Nullable(1)] on hundreds of members) share their factories:
    // each call still creates a new attribute object, as GetCustomAttributes does.
    private readonly Dictionary<string, (LLVMValueRef Create, LLVMValueRef Data)> _attributeFactories = new(StringComparer.Ordinal);

    private static string AttributeKey(AttributeInstance instance)
    {
        var key = new System.Text.StringBuilder(Mangler.Method(instance.Constructor));
        foreach (var value in instance.Fixed) AppendAttributeValue(key.Append('|'), value);
        foreach (var (name, isField, value) in instance.Named) AppendAttributeValue(key.Append('|').Append(isField ? 'F' : 'P').Append(name).Append('='), value);
        return key.ToString();
    }

    private static void AppendAttributeValue(System.Text.StringBuilder key, AttributeValue value)
    {
        key.Append(value.Type.ToDisplayString()).Append('@').Append(value.Type.ContainingAssembly?.Name).Append(':');
        switch (value.Value)
        {
            case null: key.Append('~'); break;
            case ImmutableArray<AttributeValue> array:
                key.Append('[').Append(array.Length).Append(']');
                foreach (var element in array) AppendAttributeValue(key.Append(','), element);
                break;
            case ITypeSymbol type: key.Append('T').Append(type.ToDisplayString()).Append('@').Append(type.ContainingAssembly?.Name); break;
            case string text: key.Append('"').Append(text.Length).Append(':').Append(text); break;
            case IFormattable number: key.Append(value.Value.GetType().Name).Append(number.ToString(null, System.Globalization.CultureInfo.InvariantCulture)); break;
            default: key.Append(value.Value.GetType().Name).Append(value.Value); break;
        }
    }

    /// <summary>
    /// The custom attributes of a member as reflection returns them. IL members: read from metadata (Roslyn's
    /// symbols hide the compiler's own attributes, such as NullableAttribute, which .NET returns). Source
    /// members: the declared attributes.
    /// </summary>
    private IEnumerable<AttributeInstance> MemberAttributeInstances(IMethodSymbol method, IlAssembly? il)
    {
        var definition = method.OriginalDefinition;
        if (il is not null)
            return DecodeAttributes(il, il.MethodDefinitionOf(definition).GetCustomAttributes(), IlGenericContext.For(definition)).ToList();
        var list = definition.GetAttributes().Select(FromAttributeData).OfType<AttributeInstance>().ToList();
        // Roslyn reports [Extension] as IsExtensionMethod, not among the attributes; the metadata has it
        // (ExpressionStringBuilder prints `xs.Where(...)` for an extension method's call because of it).
        if (definition.IsExtensionMethod && PseudoAttribute("System.Runtime.CompilerServices.ExtensionAttribute") is { } extension
            && !list.Any(a => a.Constructor.ContainingType.Name == "ExtensionAttribute"))
            list.Insert(0, extension);
        return list;
    }

    private IEnumerable<AttributeInstance> ParameterAttributeInstances(IParameterSymbol p, (IlAssembly Assembly, Parameter Row)? row)
    {
        var list = new List<AttributeInstance>();
        if (row is { } r)
        {
            list.AddRange(DecodeAttributes(r.Assembly, r.Row.GetCustomAttributes(), IlGenericContext.For(p.ContainingSymbol.OriginalDefinition)));
            // Pseudo-custom attributes come last, as CoreCLR lists them: [In], [Out], [Optional].
            var attributes = r.Row.Attributes;
            if ((attributes & System.Reflection.ParameterAttributes.In) != 0 && PseudoAttribute("System.Runtime.InteropServices.InAttribute") is { } inAttribute) list.Add(inAttribute);
            if ((attributes & System.Reflection.ParameterAttributes.Out) != 0 && PseudoAttribute("System.Runtime.InteropServices.OutAttribute") is { } outAttribute) list.Add(outAttribute);
            if ((attributes & System.Reflection.ParameterAttributes.Optional) != 0 && PseudoAttribute("System.Runtime.InteropServices.OptionalAttribute") is { } optional) list.Add(optional);
            if ((attributes & System.Reflection.ParameterAttributes.HasFieldMarshal) != 0)
                throw CompileError.Unsupported($"reflection over the [MarshalAs] parameter '{p.Name}' of '{p.ContainingSymbol}'", p);
            return list;
        }
        foreach (var a in p.GetAttributes())
            if (FromAttributeData(a) is { } instance) list.Add(instance);
        var flags = ParameterAttributesOf(p, null);
        if ((flags & 0x1) != 0 && PseudoAttribute("System.Runtime.InteropServices.InAttribute") is { } inSource) list.Add(inSource);
        if ((flags & 0x2) != 0 && PseudoAttribute("System.Runtime.InteropServices.OutAttribute") is { } outSource) list.Add(outSource);
        if ((flags & 0x10) != 0 && PseudoAttribute("System.Runtime.InteropServices.OptionalAttribute") is { } optionalSource) list.Add(optionalSource);
        return list;
    }

    private AttributeInstance? PseudoAttribute(string name) =>
        Compilation.GetTypeByMetadataName(name)?.InstanceConstructors.FirstOrDefault(c => c.Parameters.IsEmpty) is { } ctor
            ? new AttributeInstance(ctor, [], []) : null;

    private static AttributeInstance? FromAttributeData(AttributeData data)
    {
        if (data.AttributeConstructor is not { } ctor) return null;
        AttributeValue Value(TypedConstant c, ITypeSymbol declared) => c.Kind == TypedConstantKind.Array
            ? new AttributeValue(c.Type ?? declared, c.IsNull ? null : c.Values.Select(v => Value(v, ((IArrayTypeSymbol)(c.Type ?? declared)).ElementType)).ToImmutableArray())
            : new AttributeValue(c.Type ?? declared, c.Value);
        var fixedArgs = data.ConstructorArguments.Select((c, i) => Value(c, ctor.Parameters[Math.Min(i, ctor.Parameters.Length - 1)].Type)).ToImmutableArray();
        var named = data.NamedArguments.Select(n =>
        {
            var member = data.AttributeClass!.GetMembers(n.Key).FirstOrDefault();
            var type = member switch { IPropertySymbol prop => prop.Type, IFieldSymbol f => f.Type, _ => n.Value.Type! };
            return (n.Key, member is IFieldSymbol, Value(n.Value, type));
        }).ToImmutableArray();
        return new AttributeInstance(ctor, fixedArgs, named);
    }

    /// <summary>Decodes metadata custom attribute rows (constructor + value blob) into AttributeInstances.</summary>
    private IEnumerable<AttributeInstance> DecodeAttributes(IlAssembly il, CustomAttributeHandleCollection handles, IlGenericContext context)
    {
        var provider = new AttributeTypeProvider(this, il);
        foreach (var handle in handles)
        {
            var row = il.Reader.GetCustomAttribute(handle);
            if (il.ResolveMember(MetadataTokens.GetToken(row.Constructor), context) is not IMethodSymbol ctor) continue;
            CustomAttributeValue<ITypeSymbol> value;
            try { value = row.DecodeValue(provider); }
            catch (Exception e) when (e is BadImageFormatException or InvalidOperationException or NotSupportedException)
            {
                throw CompileError.Unsupported($"custom attribute '{ctor.ContainingType}' in {il.Name}.dll: {e.Message}", ctor);
            }
            yield return new AttributeInstance(ctor,
                [.. value.FixedArguments.Select(a => ToAttributeValue(a.Type, a.Value))],
                [.. value.NamedArguments.Select(n => (n.Name!, n.Kind == CustomAttributeNamedArgumentKind.Field, ToAttributeValue(n.Type, n.Value)))]);
        }
    }

    private static AttributeValue ToAttributeValue(ITypeSymbol type, object? value) => value switch
    {
        ImmutableArray<CustomAttributeTypedArgument<ITypeSymbol>> array =>
            new AttributeValue(type, array.Select(e => ToAttributeValue(e.Type, e.Value)).ToImmutableArray()),
        _ => new AttributeValue(type, value),
    };

    /// <summary>System.Reflection.Metadata's attribute blob decoding in Roslyn's type symbols.</summary>
    private sealed class AttributeTypeProvider(ModuleCodeGen m, IlAssembly il) : ICustomAttributeTypeProvider<ITypeSymbol>
    {
        public ITypeSymbol GetPrimitiveType(PrimitiveTypeCode typeCode) => il.GetPrimitiveType(typeCode).Type;
        public ITypeSymbol GetSystemType() => m.Compilation.GetTypeByMetadataName("System.Type")!;
        public ITypeSymbol GetSZArrayType(ITypeSymbol elementType) => m.Compilation.CreateArrayTypeSymbol(elementType);
        public ITypeSymbol GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => il.GetTypeFromDefinition(reader, handle, rawTypeKind).Type;
        public ITypeSymbol GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => il.GetTypeFromReference(reader, handle, rawTypeKind).Type;
        public ITypeSymbol GetTypeFromSerializedName(string name) =>
            ResolveSerialized(name) ?? throw new NotSupportedException($"type '{name}' in a custom attribute");
        public PrimitiveTypeCode GetUnderlyingEnumType(ITypeSymbol type) => ((INamedTypeSymbol)type).EnumUnderlyingType!.SpecialType switch
        {
            SpecialType.System_SByte => PrimitiveTypeCode.SByte, SpecialType.System_Byte => PrimitiveTypeCode.Byte,
            SpecialType.System_Int16 => PrimitiveTypeCode.Int16, SpecialType.System_UInt16 => PrimitiveTypeCode.UInt16,
            SpecialType.System_Int32 => PrimitiveTypeCode.Int32, SpecialType.System_UInt32 => PrimitiveTypeCode.UInt32,
            SpecialType.System_Int64 => PrimitiveTypeCode.Int64, SpecialType.System_UInt64 => PrimitiveTypeCode.UInt64,
            _ => throw new NotSupportedException($"enum '{type}' with underlying type {((INamedTypeSymbol)type).EnumUnderlyingType}"),
        };
        public bool IsSystemType(ITypeSymbol type) => type.ToDisplayString() == "System.Type";

        // "Ns.Type, Assembly, ..." / "Ns.Outer+Inner": the type by its CLR name.
        private ITypeSymbol? ResolveSerialized(string name)
        {
            var comma = name.IndexOf(',');
            var typeName = (comma < 0 ? name : name[..comma]).Trim();
            if (typeName.Contains('[')) return null;
            return m.Compilation.GetTypeByMetadataName(typeName) ?? il.Compilation.GetTypeByMetadataName(typeName);
        }
    }

    // ======================================================================================
    // Generated functions: invokers, attribute and default-value factories
    // ======================================================================================

    private SyntaxNode ReflectionSyntax => _reflectionSyntax ??=
        Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.Create(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("Reflection"), path: "<reflection>").GetRoot();

    private LLVMValueRef SyntheticFunction(string name, LLVMTypeRef type, Action<FunctionCodeGen, SyntaxNode> body)
    {
        var fn = Module.AddFunction(name, type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "uwtable", 2);
        try { body(new FunctionCodeGen(this, null, new LoweredFunction(fn, type)), ReflectionSyntax); }
        catch
        {
            // This function, not an earlier one of the same name (several attribute instances share a
            // constructor: LLVM suffixes the later names).
            Native.LLVMDeleteFunction(fn);
            throw;
        }
        return fn;
    }

    /// <summary>object Invoke(object target, object[] args): the corelib has checked the argument count and types.</summary>
    /// <summary>
    /// The method's invoker, or, when csharpc can't call it yet (a library method without an implementation,
    /// an argument it can't unbox), one that throws NotSupportedException: invoking that member fails
    /// explicitly at run time instead of the whole program failing to compile, as reading it keeps working.
    /// </summary>
    private LLVMValueRef ReflectionInvoker(IMethodSymbol method)
    {
        var name = Mangler.Method(method) + "$invoke";
        var type = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
        try { return SyntheticFunction(name, type, (g, syntax) => g.EmitReflectionInvoker(method, syntax)); }
        catch (CompileError error)
        {
            if (Environment.GetEnvironmentVariable("CSHARPC_TRACE") == "1") Console.Error.WriteLine($"[csharpc] no reflection invoker for {method}: {error.Message}");
            var message = $"Invoking '{method.ToDisplayString()}' through reflection is not supported by csharpc yet.";
            return SyntheticFunction(name, type, (g, _) => g.EmitThrowingFactory(message));
        }
    }

    /// <summary>
    /// The attribute's factory, or, when csharpc can't construct the attribute class yet (a library attribute
    /// without a corelib implementation), one that throws NotSupportedException: reading that member's
    /// attributes fails explicitly at run time instead of the whole program failing to compile.
    /// </summary>
    private LLVMValueRef AttributeFactory(AttributeInstance instance)
    {
        var name = Mangler.Method(instance.Constructor) + "$attribute";
        try { return SyntheticFunction(name, Llvm.FunctionType(Llvm.Ptr), (g, syntax) => g.EmitAttributeFactory(instance, syntax)); }
        catch (CompileError)
        {
            var message = $"Creating the custom attribute '{instance.Constructor.ContainingType}' is not supported by csharpc yet.";
            return SyntheticFunction(name, Llvm.FunctionType(Llvm.Ptr), (g, _) => g.EmitThrowingFactory(message));
        }
    }

    private LLVMValueRef AttributeDataFactory(AttributeInstance instance) =>
        SyntheticFunction(Mangler.Method(instance.Constructor) + "$attributedata", Llvm.FunctionType(Llvm.Ptr),
            (g, syntax) => g.EmitAttributeDataFactory(instance, syntax));

    private LLVMValueRef DefaultValueFactory(IParameterSymbol p) =>
        SyntheticFunction(Mangler.Method((IMethodSymbol)p.ContainingSymbol) + "$default" + p.Ordinal, Llvm.FunctionType(Llvm.Ptr),
            (g, syntax) => g.EmitDefaultValueFactory(p, syntax));

    // ======================================================================================
    // Delegate.Method
    // ======================================================================================

    /// <summary>A delegate over <paramref name="fn"/> was created: Delegate.Method can answer for it, if asked.</summary>
    internal void NoteDelegateMethod(LLVMValueRef fn, IMethodSymbol method, bool sourceLambda) =>
        _delegateCreations.TryAdd(fn.Handle, (fn, method, sourceLambda, Native.LLVMGetValueName(fn)));

    /// <summary>The records of the delegate targets (once Delegate.Method is lowered).</summary>
    private bool DescribeDelegateMethods()
    {
        if (_delegateMethodTable.IsNull) return false;
        var any = false;
        foreach (var (handle, (_, method, lambda, name)) in _delegateCreations.ToList())
        {
            if (!_delegateDescribed.Add(handle)) continue;
            any = true;
            try
            {
                var record = MethodRecord(method, invoker: false);
                if (lambda)
                    record = new MethodRecordData
                    {
                        Method = record.Method, Name = record.Name, Declaring = record.Declaring, ReturnType = record.ReturnType,
                        Invoke = LlvmContext.Null(Llvm.Ptr), Parameters = record.Parameters, Attributes = [], MethodAttributes = record.MethodAttributes,
                        Flags = 1, // CSRT_METHOD_SOURCE_LAMBDA
                    };
                _delegateMethods[handle] = (name, record);
            }
            catch (CompileError error) { Errors.Add(error); }
        }
        return any;
    }

    // ======================================================================================
    // The tables
    // ======================================================================================

    private LLVMTypeRef AttributeRecordType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
    private LLVMTypeRef ParameterRecordType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32);
    private LLVMTypeRef MethodRecordType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.Ptr);
    private LLVMTypeRef DelegateSignatureType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32);
    private LLVMTypeRef ReflectionInfoType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.Ptr);
    private LLVMTypeRef FieldRecordType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32);
    private LLVMTypeRef PropertyRecordType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32);

    private LLVMValueRef PropertyRecordConstant(PropertyRecordData p)
    {
        LLVMValueRef Accessor(MethodRecordData? m) => m is null ? LlvmContext.Null(Llvm.Ptr)
            : Module.AddGlobal("csharpc.reflection.accessor", MethodRecordType, MethodRecordConstant(m), constant: true, linkage: LLVMLinkage.Private);
        return Llvm.ConstStruct(p.Name, Checked(p.Type, "property type"), Accessor(p.Getter), Accessor(p.Setter), AttributeArray(p.Attributes),
            Llvm.ConstInt(Llvm.I32, p.Attributes.Count), Llvm.ConstUInt(Llvm.I32, p.Flags));
    }

    private LLVMValueRef ConstTable(string name, LLVMTypeRef entryType, IReadOnlyList<LLVMValueRef> entries)
    {
        if (entries.Count == 0) return LlvmContext.Null(Llvm.Ptr);
        var init = LlvmContext.ConstArray(entryType, [.. entries]);
        return Module.AddGlobal(name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
    }

    private LLVMValueRef AttributeArray(List<AttributeRecordData> attributes) =>
        ConstTable("csharpc.reflection.attributes", AttributeRecordType, attributes.Select(a => Llvm.ConstStruct(Checked(a.Type, "attribute type"), Checked(a.Create, "attribute factory"), Checked(a.Data, "attribute data"))).ToList());

    private static LLVMValueRef Checked(LLVMValueRef value, string what) =>
        value.IsNull ? throw new InvalidOperationException("reflection metadata: missing " + what) : value;

    private LLVMValueRef MethodRecordConstant(MethodRecordData m)
    {
        foreach (var (value, what) in new[] { (m.Name, "name"), (m.Declaring, "declaring type"), (m.ReturnType, "return type"), (m.Invoke, "invoker") })
            if (value.IsNull) throw new InvalidOperationException($"reflection record of '{m.Method}' has no {what}");
        foreach (var p in m.Parameters)
            if (p.Name.IsNull || p.Type.IsNull || p.DefaultValue.IsNull) throw new InvalidOperationException($"reflection record of '{m.Method}' has an incomplete parameter");
        var parameters = ConstTable("csharpc.reflection.parameters", ParameterRecordType, m.Parameters.Select(p => Llvm.ConstStruct(
            p.Name, p.Type, p.DefaultValue, AttributeArray(p.Attributes), Llvm.ConstInt(Llvm.I32, p.Attributes.Count), Llvm.ConstUInt(Llvm.I32, p.Flags))).ToList());
        return Llvm.ConstStruct(m.Name, m.Declaring, m.ReturnType, m.Invoke, parameters, AttributeArray(m.Attributes),
            Llvm.ConstInt(Llvm.I32, m.Parameters.Count), Llvm.ConstInt(Llvm.I32, m.Attributes.Count),
            Llvm.ConstUInt(Llvm.I32, m.MethodAttributes), Llvm.ConstUInt(Llvm.I32, m.Flags), m.Function.IsNull ? LlvmContext.Null(Llvm.Ptr) : m.Function,
            MemberIdentity(m.Method));
    }

    private readonly Dictionary<ISymbol, LLVMValueRef> _memberIdentities = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// A unique address per member (a writable byte, so LLVM never merges two): the corelib keeps one
    /// MemberInfo per identity, so the records of one method in different tables compare equal.
    /// </summary>
    internal LLVMValueRef MemberIdentity(ISymbol member)
    {
        if (_memberIdentities.TryGetValue(member, out var identity)) return identity;
        identity = Module.AddGlobal("csharpc.reflection.id", Llvm.I8, Llvm.ConstInt(Llvm.I8, 0), constant: false, linkage: LLVMLinkage.Private);
        return _memberIdentities[member] = identity;
    }

    private void FinishReflectionMetadata()
    {
        FinishMemberRecords();
        LLVMValueRef Header(LLVMValueRef array, int count) => Llvm.ConstStruct(Llvm.ConstInt(Llvm.I64, count), array);
        if (!_reflectionTable.IsNull)
        {
            var entries = new List<LLVMValueRef>();
            foreach (var e in _reflectionOrder)
            {
                var ctors = ConstTable("csharpc.reflection.ctors", MethodRecordType, e.Constructors.Select(MethodRecordConstant).ToList());
                var methods = ConstTable("csharpc.reflection.methods", MethodRecordType, e.Methods.Select(MethodRecordConstant).ToList());
                var properties = ConstTable("csharpc.reflection.properties", PropertyRecordType, e.Properties.Select(PropertyRecordConstant).ToList());
                var fields = ConstTable("csharpc.reflection.fields", FieldRecordType, e.Fields.Select(f => Llvm.ConstStruct(f.Name, Checked(f.Declaring, "field declaring type"),
                    Checked(f.Type, "field type"), f.Get, f.Set, AttributeArray(f.Attributes), Llvm.ConstInt(Llvm.I32, f.Attributes.Count), Llvm.ConstUInt(Llvm.I32, f.FieldAttributes))).ToList());
                entries.Add(Llvm.ConstStruct(Checked(e.Mt, "method table of " + e.Type), ctors, methods, AttributeArray(e.Attributes), properties,
                    Llvm.ConstInt(Llvm.I32, e.Constructors.Count), Llvm.ConstInt(Llvm.I32, e.Methods.Count), Llvm.ConstInt(Llvm.I32, e.Attributes.Count),
                    Llvm.ConstUInt(Llvm.I32, e.Described), Llvm.ConstInt(Llvm.I32, e.Properties.Count), Llvm.ConstInt(Llvm.I32, e.Fields.Count), fields));
            }
            Native.LLVMSetInitializer(_reflectionTable, Header(ConstTable("csharpc.reflection.entries", ReflectionInfoType, entries), entries.Count));
        }
        if (!_delegateSignatureTable.IsNull)
        {
            var entries = _delegateSignatures.Select(s => Llvm.ConstStruct(s.Mt, s.Return,
                ConstTable("csharpc.delegatesignature.parameters", Llvm.Ptr, s.Parameters), Llvm.ConstInt(Llvm.I32, s.Parameters.Count), Llvm.ConstInt(Llvm.I32, 0))).ToList();
            Native.LLVMSetInitializer(_delegateSignatureTable, Header(ConstTable("csharpc.delegatesignatures.entries", DelegateSignatureType, entries), entries.Count));
        }
        if (!_delegateMethodTable.IsNull)
        {
            var entries = new List<LLVMValueRef>();
            foreach (var (fn, record) in _delegateMethods.Values)
            {
                var global = Module.AddGlobal("csharpc.reflection.method", MethodRecordType, MethodRecordConstant(record), constant: true, linkage: LLVMLinkage.Private);
                entries.Add(Llvm.ConstStruct(Checked(Module.GetFunction(fn), "delegate function of " + record.Method), global));
            }
            Native.LLVMSetInitializer(_delegateMethodTable, Header(ConstTable("csharpc.delegatemethods.entries", Llvm.Struct(Llvm.Ptr, Llvm.Ptr), entries), entries.Count));
        }
    }
}
