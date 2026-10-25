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
        DamPublicMethods = 0x8, DamNonPublicMethods = 0x10;
    private const int DamConstructors = DamPublicConstructors | DamNonPublicConstructors, DamMethods = DamPublicMethods | DamNonPublicMethods;

    // CSRT_RI_* (csrt.h)
    private const uint RiPublicConstructors = 1, RiNonPublicConstructors = 2, RiPublicMethods = 4, RiNonPublicMethods = 8;

    private readonly Dictionary<ITypeSymbol, int> _damMarks = new(SymbolEqualityComparer.Default);
    private readonly Queue<ITypeSymbol> _damQueue = new();
    private readonly HashSet<ISymbol> _damInstantiations = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, ReflectionEntry> _reflectionEntries = new(SymbolEqualityComparer.Default);
    private readonly List<ReflectionEntry> _reflectionOrder = [];
    // Open generic registrations: service definition -> implementation definitions (with what they keep).
    private readonly Dictionary<INamedTypeSymbol, List<(INamedTypeSymbol Implementation, int Kinds)>> _openGenericPairs = new(SymbolEqualityComparer.Default);
    private int _closedServicesScanned;
    private LLVMValueRef _reflectionTable, _delegateMethodTable;
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
        public List<MethodRecordData> Constructors = [], Methods = [];
        public List<AttributeRecordData> Attributes = [];
    }

    internal sealed class MethodRecordData
    {
        public required IMethodSymbol Method;
        public required LLVMValueRef Name, Declaring, ReturnType, Invoke;
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
    internal void MarkDynamicallyAccessed(ITypeSymbol type, int kinds)
    {
        kinds &= DamConstructors | DamMethods;
        if (kinds == 0 || type is ITypeParameterSymbol || type.TypeKind == TypeKind.Error) return;
        if (type is INamedTypeSymbol named && (named.IsUnboundGenericType || TypeSubstitution.IsOpen(named)))
        {
            // typeof(Impl<>): the closed instantiations the program has keep the same members.
            if (named.IsUnboundGenericType || named.IsDefinition) type = named.OriginalDefinition;
            else return;
        }
        var previous = _damMarks.GetValueOrDefault(type);
        if ((previous | kinds) == previous) return;
        _damMarks[type] = previous | kinds;
        _damQueue.Enqueue(type);
        if (type is INamedTypeSymbol { IsGenericType: true, IsDefinition: true }) _closedServicesScanned = 0; // its instantiations too
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
        if (_reflectionTable.IsNull && _delegateMethodTable.IsNull) return false;
        var any = CloseOpenGenericRegistrations() | DescribeDelegateMethods();
        if (_reflectionTable.IsNull) return any;
        while (_damQueue.TryDequeue(out var type))
        {
            any = true;
            try { Describe(type, _damMarks[type]); }
            catch (CompileError error) { Errors.Add(error); }
        }
        return any;
    }

    private void Describe(ITypeSymbol type, int kinds)
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
            | ((kinds & DamPublicMethods) != 0 ? RiPublicMethods : 0) | ((kinds & DamNonPublicMethods) != 0 ? RiNonPublicMethods : 0);
        var added = wanted & ~entry.Described;
        if (added == 0) return;
        entry.Described |= added;
        bool Wanted(ISymbol member, uint publicBit, uint nonPublicBit) =>
            (added & (member.DeclaredAccessibility == Accessibility.Public ? publicBit : nonPublicBit)) != 0;
        foreach (var ctor in real.InstanceConstructors)
        {
            if (ctor.IsImplicitlyDeclared && real.IsValueType) continue; // a struct's default constructor isn't metadata
            if (Wanted(ctor, RiPublicConstructors, RiNonPublicConstructors)) entry.Constructors.Add(MethodRecord(ctor, invoker: true));
        }
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
                        or MethodKind.ExplicitInterfaceImplementation))
                    continue;
                if (!SymbolEqualityComparer.Default.Equals(t, real) && m.DeclaredAccessibility == Accessibility.Private) continue;
                if (!Wanted(m, RiPublicMethods, RiNonPublicMethods)) continue;
                var root = ClassLayout.RootOf(m.OriginalDefinition);
                if ((m.IsVirtual || m.IsOverride || m.IsAbstract) && !seen.Add(root)) continue; // overridden lower down
                entry.Methods.Add(MethodRecord(m, invoker: true));
            }
        }
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
            && !method.IsGenericMethod && !method.IsAbstract && !(method.MethodKind == MethodKind.Constructor && method.ContainingType.IsAbstract)
            && !TypeSubstitution.IsOpen(method);
        var record = new MethodRecordData
        {
            Method = method,
            Name = CString(method.MethodKind == MethodKind.Constructor ? ".ctor" : method.MetadataName),
            Declaring = MetadataMethodTable(method.ContainingType),
            ReturnType = method.ReturnsVoid || method.MethodKind == MethodKind.Constructor ? none : MetadataMethodTable(method.ReturnType),
            Invoke = invokable ? ReflectionInvoker(method) : none,
            Parameters = parameters,
            Attributes = Attributes(MemberAttributeInstances(method, il)),
            MethodAttributes = MethodAttributesOf(method),
            Flags = 0,
        };
        // The reflection stub is a root of the program: what it calls is reachable (IL failures there are errors).
        if (invokable) NoteReference(method, virtualCall: method.MethodKind != MethodKind.Constructor);
        return _methodRecords[method] = record;
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
        foreach (var instance in instances)
        {
            var type = instance.Constructor.ContainingType;
            // GetCustomAttributes(typeof(A), ...) returns an A[]: the program needs that array type.
            ArrayMethodTable(type);
            list.Add(new AttributeRecordData(MetadataMethodTable(type), AttributeFactory(instance), AttributeDataFactory(instance)));
        }
        return list;
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
        return definition.GetAttributes().Select(FromAttributeData).OfType<AttributeInstance>().ToList();
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
        body(new FunctionCodeGen(this, null, new LoweredFunction(fn, type)), ReflectionSyntax);
        return fn;
    }

    /// <summary>object Invoke(object target, object[] args): the corelib has checked the argument count and types.</summary>
    private LLVMValueRef ReflectionInvoker(IMethodSymbol method) =>
        SyntheticFunction(Mangler.Method(method) + "$invoke", Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr),
            (g, syntax) => g.EmitReflectionInvoker(method, syntax));

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
            if (Module.GetFunction(name) is { IsNull: false } partial) Native.LLVMDeleteFunction(partial);
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
    private LLVMTypeRef MethodRecordType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.I32);
    private LLVMTypeRef ReflectionInfoType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.I32);

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
            Llvm.ConstUInt(Llvm.I32, m.MethodAttributes), Llvm.ConstUInt(Llvm.I32, m.Flags));
    }

    private void FinishReflectionMetadata()
    {
        LLVMValueRef Header(LLVMValueRef array, int count) => Llvm.ConstStruct(Llvm.ConstInt(Llvm.I64, count), array);
        if (!_reflectionTable.IsNull)
        {
            var entries = new List<LLVMValueRef>();
            foreach (var e in _reflectionOrder)
            {
                var ctors = ConstTable("csharpc.reflection.ctors", MethodRecordType, e.Constructors.Select(MethodRecordConstant).ToList());
                var methods = ConstTable("csharpc.reflection.methods", MethodRecordType, e.Methods.Select(MethodRecordConstant).ToList());
                entries.Add(Llvm.ConstStruct(Checked(e.Mt, "method table of " + e.Type), ctors, methods, AttributeArray(e.Attributes), Llvm.ConstInt(Llvm.I32, e.Constructors.Count),
                    Llvm.ConstInt(Llvm.I32, e.Methods.Count), Llvm.ConstInt(Llvm.I32, e.Attributes.Count), Llvm.ConstUInt(Llvm.I32, e.Described)));
            }
            Native.LLVMSetInitializer(_reflectionTable, Header(ConstTable("csharpc.reflection.entries", ReflectionInfoType, entries), entries.Count));
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
