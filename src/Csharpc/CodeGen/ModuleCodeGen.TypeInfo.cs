using Csharpc.Frontend;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>An assembly whose sources are part of the program (the root project and its project references).</summary>
public sealed record ProgramAssembly(string Name, string? Version, IReadOnlySet<string> Files, bool IsRoot)
{
    public static IReadOnlyList<ProgramAssembly> From(FrontendSettings settings, string assemblyName) =>
        settings.Projects.Count > 0
            ? [.. settings.Projects.Select(p => new ProgramAssembly(p.AssemblyName, p.AssemblyVersion,
                p.Files.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal), p.IsRoot))]
            : [new ProgramAssembly(assemblyName, settings.AssemblyVersion, new HashSet<string>(), IsRoot: true)];
}

/// <summary>
/// System.Type metadata generated at compile time (closed world, ADR 0006): a csrt_TypeInfo per
/// method table the program has (csrt.h), emitted only when the program uses a Type member that
/// needs it (IsGenericType, BaseType, IsAssignableFrom, Assembly, ...), plus the much smaller
/// FullName table for Type.FullName. Runtime functions take the table as an argument, so programs
/// that don't ask carry neither. See docs/12-corelib.md, "Type metadata".
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>The program's own assemblies, for Type.Assembly (Driver: ProgramAssembly.From).</summary>
    public IReadOnlyList<ProgramAssembly> ProgramAssemblies { get; init; } = [];

    /// <summary>A generic parameter of a type definition, as reflection sees it (owned by the innermost type).</summary>
    private sealed record GenericParameter(INamedTypeSymbol Owner, ITypeParameterSymbol Parameter, int Position);

    // Every method table and what it describes (an ITypeSymbol or a GenericParameter), in creation order.
    private readonly List<(LLVMValueRef Mt, object Type)> _typeMts = [];
    private readonly HashSet<nint> _typeMtSeen = [];
    private LLVMValueRef _typeInfoTable, _typeNameTable;
    private readonly List<TypeInfoRecord> _typeInfoRecords = [];
    private int _typeInfoDone;
    /// <summary>Array.CreateInstance appears: keep T[] for types with reflection member metadata.</summary>
    private bool _arrayCreateInstanceUsed;

    /// <summary>Called when lowering Array.CreateInstance so reflection-kept types also get T[].</summary>
    internal void NoteArrayCreateInstance() => _arrayCreateInstanceUsed = true;

    /// <summary>Type.GetInterfaces() appears: every described type lists all its interfaces, not only
    /// the ones the program converts to (those are all casts and type tests need).</summary>
    private bool _getInterfacesUsed;
    internal void NoteGetInterfaces() => _getInterfacesUsed = true;

    /// <summary>A csrt_TypeInfo under construction: everything but the interface list, chosen at the end.</summary>
    private sealed class TypeInfoRecord
    {
        public required LLVMValueRef[] Fields;                  // mt .. declaring, arguments (csrt_TypeInfo order)
        public required LLVMValueRef Namespace, Assembly;
        public required ulong Flags;
        public required int ArgumentCount, Rank, Variance;
        public List<INamedTypeSymbol> Interfaces = [];           // candidates: every interface the type implements
    }

    internal void RegisterTypeMethodTable(LLVMValueRef mt, object type)
    {
        if (_typeMtSeen.Add(mt.Handle)) _typeMts.Add((mt, type));
    }

    private LLVMTypeRef TypeTableHeader => Llvm.Struct(Llvm.I64, Llvm.Ptr);

    /// <summary>The program's csrt_TypeInfoTable (filled in once codegen is done).</summary>
    internal LLVMValueRef TypeInfoTable()
    {
        if (_typeInfoTable.IsNull)
            _typeInfoTable = Module.AddGlobal("csharpc.typeinfo", TypeTableHeader, constant: true, linkage: LLVMLinkage.Private);
        return _typeInfoTable;
    }

    /// <summary>The program's csrt_TypeNameTable (Type.FullName, AssemblyQualifiedName).</summary>
    internal LLVMValueRef TypeNameTable()
    {
        if (_typeNameTable.IsNull)
            _typeNameTable = Module.AddGlobal("csharpc.typenames", TypeTableHeader, constant: true, linkage: LLVMLinkage.Private);
        return _typeNameTable;
    }

    /// <summary>
    /// Describes the method tables created since the last call. Describing a type can create method
    /// tables (a base class never instantiated, a generic argument) and queue their vtable methods,
    /// so Generate() alternates this with draining the worklists until neither has work left.
    /// </summary>
    /// <summary>
    /// Array.CreateInstance looks up T[] in the TypeInfo table; binders then cast to IEnumerable&lt;T&gt;.
    /// Keep T[] for element types of closed array interfaces the program already has method tables for
    /// (DI open-generic casts close T at run time but the closed IEnumerable&lt;U&gt; MT exists) and
    /// requested array interfaces — not every TypeInfo entry. Reflection-kept types don't need one
    /// (an array of a reference type is synthesized at run time; GetCustomAttributes' arrays come with
    /// the attribute metadata), and keeping theirs added 0.35 MB to the TechEmpower app.
    /// </summary>
    private void EnsureCreateInstanceArrays()
    {
        if (!_arrayCreateInstanceUsed) return;
        void KeepArrayOf(ITypeSymbol element)
        {
            if (element is not INamedTypeSymbol named || TypeSubstitution.IsOpen(named)) return;
            if (!named.IsReferenceType || named.TypeKind == TypeKind.Delegate) return;
            ArrayMethodTable(named);
        }
        foreach (var itf in _arrayInterfaces)
        {
            if (itf.IsGenericType && itf.TypeArguments.Length == 1) KeepArrayOf(itf.TypeArguments[0]);
        }
        foreach (var (_, type) in _typeMts.ToList())
        {
            if (type is not INamedTypeSymbol { IsGenericType: true } named || TypeSubstitution.IsOpen(named)) continue;
            if (named.TypeArguments.Length != 1) continue;
            if (!s_arrayInterfaces.Contains(Mangler.MetadataName(named.OriginalDefinition))) continue;
            // Only reference-type U, like KeepArrayOf: CreateInstance makes no other U[] than the program's
            // own (whose casts request their interfaces), and every Dictionary's ICollection<KeyValuePair<K, V>>
            // and every enumerator's IEnumerable<Entry> would otherwise put IList<...> on KeyValuePair[] and Entry[].
            if (!named.TypeArguments[0].IsReferenceType) continue;
            KeepArrayOf(named.TypeArguments[0]);
            // So RefreshArrayInterfaceMaps puts IEnumerable<U> on U[] (open-generic binder casts).
            RequestArrayInterface(named);
        }
    }

    private bool EmitTypeMetadata()
    {
        if (_typeInfoTable.IsNull) return false;
        var any = false;
        while (true)
        {
            if (_typeInfoDone == _typeMts.Count)
            {
                RegisterKnownMethodTables();
                EnsureCreateInstanceArrays();
                if (_typeInfoDone == _typeMts.Count) SelectInterfaces();
                if (_typeInfoDone == _typeMts.Count) break;
            }
            var (mt, type) = _typeMts[_typeInfoDone++];
            any = true;
            // Open types other than definitions (scratch tables of generic code) have no Type objects.
            if (type is ITypeSymbol symbol && IsOpenInstance(symbol)) continue;
            // A type that can't be described is an error of its own; keep describing the rest (and
            // lowering what they reference) so one compile reports everything.
            try { _typeInfoRecords.Add(TypeInfoRecordFor(mt, type)); }
            catch (CompileError error) { Errors.Add(error); }
        }
        return any;
    }

    /// <summary>
    /// Interface lists only need the interfaces a Type object can exist for (the program's interface
    /// method tables: typeof, interface maps, generic arguments) — int alone implements ~30 generic-math
    /// interfaces nobody asks about. Variant interfaces are kept when the program has another
    /// instantiation of the same definition (IEnumerable&lt;string&gt; for IsAssignableFrom(IEnumerable&lt;object&gt;)),
    /// which gives them method tables, and so entries, of their own.
    /// </summary>
    private void SelectInterfaces()
    {
        var definitions = _interfaceTables.Keys.Where(i => i.IsGenericType).Select(i => i.OriginalDefinition)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var record in _typeInfoRecords.ToList())
            foreach (var itf in record.Interfaces)
                if (itf.IsGenericType && definitions.Contains(itf.OriginalDefinition)
                    && itf.OriginalDefinition.TypeParameters.Any(p => p.Variance != VarianceKind.None))
                    InterfaceMethodTable(itf);
        if (_getInterfacesUsed)
        {
            // Type.GetInterfaces() lists every interface of every described type. One the program has no
            // method table for is described lazily (csrt_LazyInterface, FinishTypeMetadata): only what its
            // description points at needs a method table — its definition, its arguments, and (the same
            // way) its own interfaces. int's ~30 generic-math interfaces and each array's IList<T> family
            // were a method table, a name and an entry each: ~190 KB of an expression-tree program.
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var work = new Queue<INamedTypeSymbol>(_typeInfoRecords.ToList().SelectMany(r => r.Interfaces));
            while (work.TryDequeue(out var itf))
            {
                var key = InterfaceKey(itf);
                if (!seen.Add(key)) continue;
                if (_interfaceTables.ContainsKey(key) || !CanDescribeLazily(key)) { InterfaceMethodTable(itf); continue; }
                if (key.IsGenericType)
                {
                    MetadataMethodTable(key.OriginalDefinition);
                    foreach (var argument in key.TypeArguments) MetadataMethodTable(argument);
                }
                foreach (var inner in ClrInterfaceOrder(key)) work.Enqueue(inner);
            }
        }
        RegisterKnownMethodTables();
    }

    /// <summary>
    /// Can Type.GetInterfaces() hand out this interface without a method table (csrt_LazyInterface)? csrt
    /// composes its name from its definition's and its arguments', so not nested types (their arguments
    /// span the declaring types) nor corelib-internal ones (their names aren't .NET's).
    /// </summary>
    private bool CanDescribeLazily(INamedTypeSymbol itf) =>
        itf.TypeKind == TypeKind.Interface && itf.ContainingType is null && !TypeSubstitution.IsOpen(itf)
        && !CoreLib.IsCoreLib(itf.OriginalDefinition) && !itf.AllInterfaces.Any(TypeSubstitution.IsOpen)
        && itf.TypeArguments.All(a => a is INamedTypeSymbol or IArrayTypeSymbol);

    private void FinishTypeMetadata()
    {
        LLVMValueRef Table(string name, LLVMTypeRef entryType, IReadOnlyList<LLVMValueRef> entries)
        {
            var init = LlvmContext.ConstArray(entryType, [.. entries]);
            var array = Module.AddGlobal(name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
            return Llvm.ConstStruct(Llvm.ConstInt(Llvm.I64, entries.Count), array);
        }
        if (!_typeInfoTable.IsNull)
        {
            // Interfaces without a method table that GetInterfaces lists: csrt_LazyInterface descriptions,
            // referenced as tagged pointers (index + 1 byte), numbered before anything points at them.
            var lazyIndex = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
            var lazyOrder = new List<INamedTypeSymbol>();
            // What a description points at must be described already (SelectInterfaces made sure of it while
            // types could still be added): one that isn't is left out, as interfaces without tables were before.
            var described = _typeMts.Select(e => e.Type).OfType<ITypeSymbol>().Select(RealType).ToHashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            bool IsLazy(INamedTypeSymbol key) => _getInterfacesUsed && !_interfaceTables.ContainsKey(key) && CanDescribeLazily(key)
                && (!key.IsGenericType || described.Contains(RealType(key.OriginalDefinition))) && key.TypeArguments.All(a => described.Contains(RealType(a)));
            void Collect(INamedTypeSymbol itf)
            {
                var key = InterfaceKey(itf);
                if (!IsLazy(key) || lazyIndex.ContainsKey(key)) return;
                lazyIndex[key] = lazyOrder.Count;
                lazyOrder.Add(key);
                foreach (var inner in ClrInterfaceOrder(key)) Collect(inner);
            }
            foreach (var record in _typeInfoRecords)
                foreach (var itf in record.Interfaces) Collect(itf);
            // csrt_TypeInfoTable: the count packs both lengths, and the lazy interfaces follow the entries in
            // one object { [entries], [lazies] } (csrt.h), so programs without them keep the same table.
            var lazyType = Llvm.Struct(TypeInfoEntryType, Llvm.Ptr);
            var entriesType = LlvmContext.Array(TypeInfoEntryType, (ulong)_typeInfoRecords.Count);
            var lazyArrayType = LlvmContext.Array(lazyType, (ulong)lazyOrder.Count);
            var tableType = lazyOrder.Count == 0 ? entriesType : Llvm.Struct(entriesType, lazyArrayType);
            var tableGlobal = Module.AddGlobal("csharpc.typeinfo.entries", tableType, constant: true, linkage: LLVMLinkage.Private);
            var lazyStart = (long)Types.Target.SizeOf(entriesType);
            var lazySize = (long)Types.Target.SizeOf(lazyType);
            LLVMValueRef? InterfaceRef(INamedTypeSymbol itf)
            {
                var key = InterfaceKey(itf);
                if (_interfaceTables.TryGetValue(key, out var mt)) return mt;
                if (!lazyIndex.TryGetValue(key, out var index)) return null;
                return Native.LLVMConstInBoundsGEP2(Llvm.I8, tableGlobal, [Llvm.ConstInt(Llvm.I64, lazyStart + index * lazySize + 1)], 1);
            }
            LLVMValueRef InterfaceList(IEnumerable<INamedTypeSymbol> interfaces, out int count)
            {
                var refs = interfaces.Select(InterfaceRef).OfType<LLVMValueRef>().ToList();
                count = refs.Count;
                return refs.Count == 0 ? LlvmContext.Null(Llvm.Ptr) : MtArray("ifaces", refs);
            }
            var entries = new List<LLVMValueRef>();
            foreach (var record in _typeInfoRecords)
            {
                var interfaces = InterfaceList(record.Interfaces, out var interfaceCount);
                entries.Add(Llvm.ConstStruct([.. record.Fields, interfaces,
                    record.Namespace, record.Assembly, Llvm.ConstUInt(Llvm.I64, record.Flags), Llvm.ConstInt(Llvm.I32, record.ArgumentCount),
                    Llvm.ConstInt(Llvm.I32, interfaceCount), Llvm.ConstInt(Llvm.I32, record.Rank), Llvm.ConstInt(Llvm.I32, record.Variance)]));
            }
            var lazies = new List<LLVMValueRef>();
            var none = LlvmContext.Null(Llvm.Ptr);
            foreach (var itf in lazyOrder)
            {
                var interfaces = InterfaceList(ClrInterfaceOrder(itf), out var interfaceCount);
                var arguments = itf.TypeArguments;
                var variance = 0;
                for (var k = 0; k < itf.TypeParameters.Length; k++)
                    variance |= itf.OriginalDefinition.TypeParameters[k].Variance switch { VarianceKind.Out => 1, VarianceKind.In => 2, _ => 0 } << (2 * k);
                var info = Llvm.ConstStruct(none, none, none, itf.IsGenericType ? MetadataMethodTable(itf.OriginalDefinition) : none, none,
                    arguments.Length == 0 ? none : MtArray("args", [.. arguments.Select(MetadataMethodTable)]), interfaces,
                    NamespaceOf(itf) is { } ns ? CString(ns) : none, AssemblyOf(itf) is { } assembly ? CString(assembly) : none,
                    Llvm.ConstUInt(Llvm.I64, TypeFlags(RealType(itf))), Llvm.ConstInt(Llvm.I32, arguments.Length), Llvm.ConstInt(Llvm.I32, interfaceCount),
                    Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, variance));
                lazies.Add(Llvm.ConstStruct(info, CString(Mangler.MetadataName(itf.IsGenericType ? itf.OriginalDefinition : itf))));
            }
            var entriesInit = LlvmContext.ConstArray(TypeInfoEntryType, [.. entries]);
            Native.LLVMSetInitializer(tableGlobal, lazyOrder.Count == 0 ? entriesInit
                : Llvm.ConstStruct(entriesInit, LlvmContext.ConstArray(lazyType, [.. lazies])));
            Native.LLVMSetInitializer(_typeInfoTable, Llvm.ConstStruct(
                Llvm.ConstInt(Llvm.I64, entries.Count | ((long)lazyOrder.Count << 32)), tableGlobal));
        }
        if (!_typeNameTable.IsNull)
        {
            RegisterKnownMethodTables();
            // csrt_TypeNameEntry { mt, name (a string or a csrt_TypeNameComposition), unknown, argument_count }
            var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32);
            var argumentType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
            var entries = new List<LLVMValueRef>();
            // The method table of each described type (by its .NET type), for composed names' arguments.
            var mtOf = new Dictionary<ITypeSymbol, LLVMValueRef>(SymbolEqualityComparer.Default);
            foreach (var (mt, type) in _typeMts)
                if (type is ITypeSymbol symbol && !IsOpenInstance(symbol)) mtOf.TryAdd(RealType(symbol), mt);
            foreach (var (mt, type) in _typeMts)
            {
                // Only types whose FullName differs from the method table's name (ToString's form).
                if (type is ITypeSymbol symbol && IsOpenInstance(symbol)) continue;
                if (type is DisplayClassType or RuntimeTypeMarker) continue; // FullName is the method table's name
                var real = type is GenericParameter ? null : RealType((ITypeSymbol)type);
                if (real is not null && !HasGenericArguments(real) && !IsCoreLibInternal(real)) continue;
                var unknown = false;
                var fullName = real is null ? null : FullNameOf(real, ref unknown);
                // A generic instantiation whose arguments all have method tables: the runtime composes its
                // name from the definition's and the arguments' (csrt.h), each long name stored once.
                if (!unknown && fullName is not null && real is INamedTypeSymbol named && !IsDef(named)
                    && ChainArguments(named) is { Count: > 0 and <= 64 } arguments
                    && arguments.All(a => mtOf.ContainsKey(a) && AssemblyOf(a) is not null))
                {
                    var parts = arguments.Select(a => Llvm.ConstStruct(mtOf[a], CString(AssemblyOf(a)!))).ToList();
                    var init = Llvm.ConstStruct(CString(Mangler.MetadataName(named.OriginalDefinition)), LlvmContext.ConstArray(argumentType, [.. parts]));
                    var composition = Module.AddGlobal("csharpc.typenames.args", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
                    entries.Add(Llvm.ConstStruct(mt, composition, Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, arguments.Count)));
                    continue;
                }
                // An unknown name is never read (csrt reports it as not supported): no string for it.
                entries.Add(Llvm.ConstStruct(mt, fullName is null || unknown ? LlvmContext.Null(Llvm.Ptr) : CString(fullName),
                    Llvm.ConstInt(Llvm.I32, unknown ? 1 : 0), Llvm.ConstInt(Llvm.I32, 0)));
            }
            Native.LLVMSetInitializer(_typeNameTable, Table("csharpc.typenames.entries", entryType, entries));
        }
    }

    /// <summary>
    /// A type's interfaces in CoreCLR's order (Type.GetInterfaces): the base class's first, then each
    /// interface in metadata order followed by the interfaces it inherits, each listed once
    /// (string: IComparable, IEnumerable, IConvertible, IEnumerable`1, ..., ISpanParsable`1, IParsable`1).
    /// </summary>
    private static List<INamedTypeSymbol> ClrInterfaceOrder(INamedTypeSymbol type)
    {
        var order = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        void Expand(INamedTypeSymbol itf)
        {
            if (!seen.Add(itf)) return;
            order.Add(itf);
            foreach (var inherited in itf.Interfaces) Expand(inherited);
        }
        var chain = new List<INamedTypeSymbol>();
        if (type.TypeKind != TypeKind.Interface)
            for (var t = type; t is not null; t = t.BaseType) chain.Insert(0, t);
        else chain.Add(type);
        foreach (var t in chain)
            foreach (var itf in ImplementationOrder(t)) Expand(itf);
        // Anything Roslyn reports that the walk missed (it shouldn't) still belongs to the set.
        foreach (var itf in type.AllInterfaces) Expand(itf);
        return order;
    }

    /// <summary>A type's declared interfaces; for a framework type, in its implementation assembly's order
    /// (the reference assembly Roslyn reads sorts them alphabetically).</summary>
    private static IEnumerable<INamedTypeSymbol> ImplementationOrder(INamedTypeSymbol type)
    {
        var declared = type.Interfaces;
        var definition = type.OriginalDefinition;
        if (declared.Length < 2 || definition.ContainingType is not null || !definition.DeclaringSyntaxReferences.IsEmpty
            || definition.ContainingAssembly is not { } assembly
            || Frontend.SharedFramework.InterfaceOrder(assembly.Name, definition.ContainingNamespace.ToDisplayString(), definition.MetadataName) is not { } order)
            return declared;
        int Rank(INamedTypeSymbol itf)
        {
            var d = itf.OriginalDefinition;
            var index = Array.IndexOf(order, d.ContainingNamespace.ToDisplayString() + "." + d.MetadataName);
            return index < 0 ? int.MaxValue : index;
        }
        return declared.Select((itf, i) => (itf, i)).OrderBy(x => Rank(x.itf)).ThenBy(x => x.i).Select(x => x.itf);
    }

    private LLVMTypeRef TypeInfoEntryType => Llvm.Struct(
        Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr,
        Llvm.I64, Llvm.I32, Llvm.I32, Llvm.I32, Llvm.I32);

    private LLVMValueRef MtArray(string suffix, IReadOnlyList<LLVMValueRef> tables)
    {
        var init = LlvmContext.ConstArray(Llvm.Ptr, [.. tables]);
        return Module.AddGlobal("csharpc.typeinfo." + suffix, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
    }

    private INamedTypeSymbol InterfaceKey(INamedTypeSymbol itf) => (INamedTypeSymbol)CoreLib.Unmap(TypeSubstitution.EraseTupleNames(itf, Compilation));

    /// <summary>Method tables kept in per-kind caches (classes, structs, enums, interfaces) and the runtime's own.</summary>
    private void RegisterKnownMethodTables()
    {
        foreach (var layout in _layouts.Values)
            if (!layout.MethodTable.IsNull) RegisterTypeMethodTable(layout.MethodTable, layout.Type);
        foreach (var layout in _structs.Values)
            if (!layout.MethodTable.IsNull) RegisterTypeMethodTable(layout.MethodTable, layout.Type);
        foreach (var (type, mt) in _enumMethodTables) RegisterTypeMethodTable(mt, type);
        foreach (var (type, mt) in _interfaceTables) RegisterTypeMethodTable(mt, type);
        RegisterTypeMethodTable(ExternalGlobal("csrt_Object_MT"), Compilation.GetSpecialType(SpecialType.System_Object));
        RegisterTypeMethodTable(ExternalGlobal("csrt_String_MT"), Compilation.GetSpecialType(SpecialType.System_String));
        // Type objects themselves, which System.Linq.Expressions reflects over (Expression.Constant(typeof(int),
        // typeof(Type)) checks typeof(Type).IsAssignableFrom(value.GetType())); only its programs pay for the entry.
        if (UsesLinqExpressions)
            RegisterTypeMethodTable(ExternalGlobal("csrt_Type_MT"), RuntimeTypeMarker.Instance);
        // Boxed primitives the program uses (boxing references these without going through typeof).
        foreach (var special in s_boxedPrimitives)
        {
            var type = Compilation.GetSpecialType(special);
            if (_globals.TryGetValue($"csrt_Boxed_{type.MetadataName}_MT", out var mt)) RegisterTypeMethodTable(mt, type);
        }
        foreach (var (table, name) in s_boxedRuntimeTypes)
            if (_globals.TryGetValue(table, out var mt) && Compilation.GetTypeByMetadataName(name) is { } type) RegisterTypeMethodTable(mt, type);
    }

    /// <summary>System.RuntimeType, the class of csharpc's Type objects (csrt_Type_MT): internal to CoreLib, so no symbol.</summary>
    private sealed class RuntimeTypeMarker
    {
        public static readonly RuntimeTypeMarker Instance = new();
    }

    private TypeInfoRecord RuntimeTypeRecord(LLVMValueRef mt)
    {
        var none = LlvmContext.Null(Llvm.Ptr);
        var typeInfo = Compilation.GetTypeByMetadataName("System.Reflection.TypeInfo")!;
        var assembly = AssemblyOf(Compilation.GetSpecialType(SpecialType.System_Object));
        return new TypeInfoRecord
        {
            Fields = [mt, MetadataMethodTable(typeInfo), none, none, none, none],
            Namespace = CString("System"), Assembly = assembly is null ? none : CString(assembly),
            Flags = TI.Class | TI.Sealed | TI.NotPublic | TI.TypeDefinition | TI.ReferenceType,
            ArgumentCount = 0, Rank = 0, Variance = 0, Interfaces = ClrInterfaceOrder(typeInfo),
        };
    }

    private static readonly SpecialType[] s_boxedPrimitives =
    [
        SpecialType.System_Int32, SpecialType.System_Int64, SpecialType.System_UInt32, SpecialType.System_UInt64,
        SpecialType.System_Int16, SpecialType.System_UInt16, SpecialType.System_Byte, SpecialType.System_SByte,
        SpecialType.System_Double, SpecialType.System_Single, SpecialType.System_Boolean, SpecialType.System_Char,
        SpecialType.System_IntPtr, SpecialType.System_UIntPtr,
    ];

    private static readonly (string Table, string Type)[] s_boxedRuntimeTypes =
    [
        ("csrt_Boxed_Decimal_MT", "System.Decimal"), ("csrt_Boxed_TimeSpan_MT", "System.TimeSpan"),
        ("csrt_Boxed_DateTime_MT", "System.DateTime"), ("csrt_Boxed_BigInteger_MT", "System.Numerics.BigInteger"),
    ];

    /// <summary>The method table reflection hands out for a type (typeof's).</summary>
    private LLVMValueRef MetadataMethodTable(ITypeSymbol type) => TypeMethodTable(type, null!);

    /// <summary>The Type object of a generic parameter: name only ("T"), described by the metadata table.</summary>
    private LLVMValueRef GenericParameterMethodTable(INamedTypeSymbol owner, int position)
    {
        var parameters = ChainArguments(owner);
        var parameter = (ITypeParameterSymbol)parameters[position];
        var key = "typeparam:" + owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + position;
        if (_arrayMethodTables.TryGetValue(key, out var existing)) return existing;
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(parameter.Name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        var global = Module.AddGlobal("csrt.typeparam." + parameter.Name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        RegisterTypeMethodTable(global, new GenericParameter(owner, parameter, position));
        return _arrayMethodTables[key] = global;
    }

    private TypeInfoRecord TypeInfoRecordFor(LLVMValueRef mt, object described)
    {
        if (described is DisplayClassType displayClass) return DisplayClassRecord(mt, displayClass);
        if (described is RuntimeTypeMarker) return RuntimeTypeRecord(mt);
        var none = LlvmContext.Null(Llvm.Ptr);
        LLVMValueRef baseMt = none, element = none, definition = none, declaring = none, arguments = none;
        LLVMValueRef namespaceName = none, assembly = none;
        int argumentCount = 0, rank = 0, variance = 0;
        ulong flags;
        List<INamedTypeSymbol> interfaces = [];
        LLVMValueRef Str(string? s) => s is null ? none : CString(s);

        if (described is GenericParameter gp)
        {
            var p = gp.Parameter;
            var valueType = p.HasValueTypeConstraint || p.HasUnmanagedTypeConstraint;
            flags = TI.GenericParameter | TI.ContainsGenericParameters | TI.Public | TI.Nested | TI.Visible
                | (valueType ? TI.ValueType : TI.Class) | (p.IsReferenceType ? TI.ReferenceType : 0);
            var constraint = valueType ? Compilation.GetSpecialType(SpecialType.System_ValueType)
                : p.ConstraintTypes.FirstOrDefault(c => c.TypeKind == TypeKind.Class) ?? Compilation.GetSpecialType(SpecialType.System_Object);
            if (TypeSubstitution.IsOpen(constraint)) flags |= TI.NoHierarchy;
            else baseMt = MetadataMethodTable(constraint);
            declaring = MetadataMethodTable(gp.Owner);
            rank = gp.Position;
            namespaceName = Str(NamespaceOf(gp.Owner));
            assembly = Str(AssemblyOf(gp.Owner));
        }
        else
        {
            var type = RealType((ITypeSymbol)described);
            flags = TypeFlags(type);
            assembly = Str(AssemblyOf(type));
            switch (type)
            {
                case IArrayTypeSymbol array:
                    baseMt = MetadataMethodTable(Compilation.GetSpecialType(SpecialType.System_Array));
                    element = MetadataMethodTable(array.ElementType);
                    rank = array.Rank;
                    // System.Array's interfaces; IList<T> & co. are answered from the element type
                    // (CSRT_TI_ARRAY_INTERFACE in type.c), covariance included.
                    // GetInterfaces lists them too for a one-dimensional array (T[]: IList`1, ICollection`1,
                    // IEnumerable`1, IReadOnlyList`1, IReadOnlyCollection`1 after Array's own).
                    interfaces = ClrInterfaceOrder(Compilation.GetSpecialType(SpecialType.System_Array));
                    if (array.IsSZArray && array.ElementType is not IPointerTypeSymbol and not IFunctionPointerTypeSymbol)
                        foreach (var generic in new[] { SpecialType.System_Collections_Generic_IList_T, SpecialType.System_Collections_Generic_ICollection_T,
                                     SpecialType.System_Collections_Generic_IEnumerable_T, SpecialType.System_Collections_Generic_IReadOnlyList_T,
                                     SpecialType.System_Collections_Generic_IReadOnlyCollection_T })
                            interfaces.Add(Compilation.GetSpecialType(generic).Construct(array.ElementType));
                    // .NET quirk: an array reports its innermost element type's generic arguments
                    // (typeof(List<int>[]).GetGenericArguments() is [Int32]); GenericTypeArguments stays empty.
                    var inner = array.ElementType;
                    while (inner is IArrayTypeSymbol nestedArray) inner = nestedArray.ElementType;
                    if (inner is INamedTypeSymbol innerNamed && !IsDef(innerNamed) && ChainArguments(innerNamed) is { Count: > 0 } innerArguments)
                    {
                        arguments = MtArray("args", [.. innerArguments.Select(MetadataMethodTable)]);
                        argumentCount = innerArguments.Count;
                    }
                    break;
                case IPointerTypeSymbol pointer:
                    element = MetadataMethodTable(pointer.PointedAtType);
                    break;
                case INamedTypeSymbol named:
                {
                    // An enum's `element` is its underlying type (Type.GetEnumUnderlyingType; GetElementType is null).
                    if (named is { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType }) element = MetadataMethodTable(underlyingType);
                    if (named.TypeKind != TypeKind.Interface && named.BaseType is { } baseType)
                    {
                        if (TypeSubstitution.IsOpen(baseType)) flags |= TI.NoHierarchy;
                        else baseMt = MetadataMethodTable(baseType);
                    }
                    if (named.AllInterfaces.Any(TypeSubstitution.IsOpen)) flags |= TI.NoInterfaces;
                    else interfaces = ClrInterfaceOrder(named);
                    var chain = ChainArguments(named);
                    if (chain.Count > 0)
                    {
                        definition = MetadataMethodTable(named.OriginalDefinition);
                        var owner = named.OriginalDefinition;
                        arguments = MtArray("args", IsDef(named)
                            ? [.. chain.Select((_, i) => GenericParameterMethodTable(owner, i))]
                            : [.. chain.Select(MetadataMethodTable)]);
                        argumentCount = chain.Count;
                        if (!IsDef(named) && named.ContainingType is null)
                            for (var k = 0; k < named.TypeParameters.Length; k++)
                                variance |= named.OriginalDefinition.TypeParameters[k].Variance switch
                                {
                                    VarianceKind.Out => 1, VarianceKind.In => 2, _ => 0,
                                } << (2 * k);
                    }
                    if (named.ContainingType is { } outer) declaring = MetadataMethodTable(outer.OriginalDefinition);
                    break;
                }
            }
        }
        return new TypeInfoRecord
        {
            Fields = [mt, baseMt, element, definition, declaring, arguments],
            Namespace = namespaceName, Assembly = assembly, Flags = flags,
            ArgumentCount = argumentCount, Rank = rank, Variance = variance, Interfaces = interfaces,
        };
    }

    private static readonly string[] s_arrayInterfaces =
    [
        "System.Collections.Generic.IList`1", "System.Collections.Generic.ICollection`1", "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IReadOnlyList`1", "System.Collections.Generic.IReadOnlyCollection`1",
    ];

    /// <summary>csrt_TypeInfo flags (csrt.h, CSRT_TI_*).</summary>
    private static class TI
    {
        public const ulong Class = 1 << 0, ValueType = 1 << 1, Enum = 1 << 2, Array = 1 << 3, Primitive = 1 << 4, Abstract = 1 << 5,
            Sealed = 1 << 6, Interface = 1 << 7, Public = 1 << 8, NotPublic = 1 << 9, Nested = 1 << 10, NestedPublic = 1 << 11,
            NestedPrivate = 1 << 12, NestedAssembly = 1 << 13, NestedFamily = 1 << 14, NestedFamOrAssem = 1 << 15,
            NestedFamAndAssem = 1 << 16, Visible = 1 << 17, GenericType = 1 << 18, GenericDefinition = 1 << 19,
            ConstructedGeneric = 1 << 20, GenericParameter = 1 << 21, ContainsGenericParameters = 1 << 22, Pointer = 1 << 23,
            SZArray = 1 << 24, VariableBoundArray = 1 << 25, TypeDefinition = 1 << 26, ByRefLike = 1 << 27, Nullable = 1 << 28,
            HasElementType = 1 << 29;
        public const ulong ReferenceType = 1UL << 32, NoHierarchy = 1UL << 33,
            ArrayInterface = 1UL << 35, NoInterfaces = 1UL << 36;
        public const int ElementClassShift = 40;
    }

    /// <summary>The Type property a flag answers, by getter name (BclValueTypes.Reflection.cs).</summary>
    internal static readonly IReadOnlyDictionary<string, ulong> TypeFlagProperties = new Dictionary<string, ulong>
    {
        ["IsClass"] = TI.Class, ["IsValueType"] = TI.ValueType, ["IsEnum"] = TI.Enum, ["IsArray"] = TI.Array,
        ["IsPrimitive"] = TI.Primitive, ["IsAbstract"] = TI.Abstract, ["IsSealed"] = TI.Sealed, ["IsInterface"] = TI.Interface,
        ["IsPublic"] = TI.Public, ["IsNotPublic"] = TI.NotPublic, ["IsNested"] = TI.Nested, ["IsNestedPublic"] = TI.NestedPublic,
        ["IsNestedPrivate"] = TI.NestedPrivate, ["IsNestedAssembly"] = TI.NestedAssembly, ["IsNestedFamily"] = TI.NestedFamily,
        ["IsNestedFamORAssem"] = TI.NestedFamOrAssem, ["IsNestedFamANDAssem"] = TI.NestedFamAndAssem, ["IsVisible"] = TI.Visible,
        ["IsGenericType"] = TI.GenericType, ["IsGenericTypeDefinition"] = TI.GenericDefinition,
        ["IsConstructedGenericType"] = TI.ConstructedGeneric, ["IsGenericParameter"] = TI.GenericParameter,
        ["IsGenericTypeParameter"] = TI.GenericParameter, ["ContainsGenericParameters"] = TI.ContainsGenericParameters,
        ["IsPointer"] = TI.Pointer, ["IsSZArray"] = TI.SZArray, ["IsVariableBoundArray"] = TI.VariableBoundArray,
        ["IsTypeDefinition"] = TI.TypeDefinition, ["IsByRefLike"] = TI.ByRefLike, ["HasElementType"] = TI.HasElementType,
    };

    private ulong TypeFlags(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return TI.Class | TI.Public | TI.Sealed | TI.Array | TI.HasElementType | TI.ReferenceType
                    | (array.IsSZArray ? TI.SZArray : TI.VariableBoundArray) | (IsVisible(array.ElementType) ? TI.Visible : 0);
            case IPointerTypeSymbol pointer:
                return TI.Class | TI.Public | TI.Pointer | TI.HasElementType | (IsVisible(pointer.PointedAtType) ? TI.Visible : 0);
            case INamedTypeSymbol named:
            {
                ulong f = 0;
                if (named.TypeKind == TypeKind.Interface) f |= TI.Interface | TI.Abstract;
                else if (named.IsValueType) f |= TI.ValueType;
                else f |= TI.Class;
                if (named.TypeKind == TypeKind.Enum) f |= TI.Enum;
                if (named.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte
                    or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                    or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
                    or SpecialType.System_IntPtr or SpecialType.System_UIntPtr)
                    f |= TI.Primitive;
                if (named.IsAbstract || named.IsStatic) f |= TI.Abstract;
                if (named.TypeKind != TypeKind.Interface && (named.IsSealed || named.IsStatic || named.IsValueType || named.TypeKind == TypeKind.Delegate))
                    f |= TI.Sealed;
                if (named.ContainingType is null)
                    f |= named.DeclaredAccessibility == Accessibility.Public ? TI.Public : TI.NotPublic;
                else
                    f |= TI.Nested | named.DeclaredAccessibility switch
                    {
                        Accessibility.Public => TI.NestedPublic,
                        Accessibility.Private => TI.NestedPrivate,
                        Accessibility.Internal => TI.NestedAssembly,
                        Accessibility.Protected => TI.NestedFamily,
                        Accessibility.ProtectedOrInternal => TI.NestedFamOrAssem,
                        Accessibility.ProtectedAndInternal => TI.NestedFamAndAssem,
                        _ => 0,
                    };
                if (IsVisible(named)) f |= TI.Visible;
                if (ChainArguments(named).Count > 0)
                    f |= TI.GenericType | (IsDef(named) ? TI.GenericDefinition : TI.ConstructedGeneric);
                if (IsDef(named)) f |= TI.TypeDefinition;
                if (TypeSubstitution.IsOpen(named)) f |= TI.ContainsGenericParameters;
                if (named.IsRefLikeType) f |= TI.ByRefLike;
                if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) f |= TI.Nullable;
                if (named.IsReferenceType) f |= TI.ReferenceType;
                if (!IsDef(named) && s_arrayInterfaces.Contains(Mangler.MetadataName(named.OriginalDefinition))) f |= TI.ArrayInterface;
                f |= (ulong)ElementClass(named) << TI.ElementClassShift;
                return f;
            }
            default:
                return 0;
        }
    }

    /// <summary>Primitives that share array storage: int[] and uint[] (and enums over int) are castable to each other.</summary>
    private static int ElementClass(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } ? ElementClass(underlying) : type.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte => 1,
            SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
            SpecialType.System_Int32 or SpecialType.System_UInt32 => 3,
            SpecialType.System_Int64 or SpecialType.System_UInt64 => 4,
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr => 5,
            _ => 0,
        };

    /// <summary>Type.IsVisible: public all the way out, and so are the type arguments.</summary>
    private static bool IsVisible(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsVisible(array.ElementType),
        IPointerTypeSymbol pointer => IsVisible(pointer.PointedAtType),
        ITypeParameterSymbol => true,
        INamedTypeSymbol named => named.DeclaredAccessibility == Accessibility.Public
            && (named.ContainingType is null || IsVisible(named.ContainingType.OriginalDefinition))
            && named.TypeArguments.All(a => a is ITypeParameterSymbol || IsVisible(a)),
        _ => false,
    };

    /// <summary>Type arguments of a type and the types it is nested in, outermost first (the CLR's view).</summary>
    private static List<ITypeSymbol> ChainArguments(INamedTypeSymbol type)
    {
        var arguments = new List<ITypeSymbol>();
        for (var t = type; t is not null; t = t.ContainingType) arguments.InsertRange(0, t.TypeArguments);
        return arguments;
    }

    /// <summary>
    /// A type definition (not constructed). Roslyn's IsDefinition is reference-based, and symbols that
    /// went through nullable annotations (special types from typeof) are wrappers: compare instead.
    /// </summary>
    internal static bool IsDef(INamedTypeSymbol type) => SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition);

    private static bool IsOpenInstance(ITypeSymbol type) =>
        TypeSubstitution.IsOpen(type) && !(type is INamedTypeSymbol definition && IsDef(definition))
        || type is INamedTypeSymbol named && ChainArguments(named).Any(a => a.TypeKind == TypeKind.Error);

    private static bool HasGenericArguments(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => HasGenericArguments(array.ElementType),
        IPointerTypeSymbol pointer => HasGenericArguments(pointer.PointedAtType),
        INamedTypeSymbol named => ChainArguments(named).Count > 0,
        _ => true,
    };

    private static bool IsCoreLibInternal(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsCoreLibInternal(array.ElementType),
        IPointerTypeSymbol pointer => IsCoreLibInternal(pointer.PointedAtType),
        INamedTypeSymbol named => CoreLib.IsCoreLib(named.OriginalDefinition) || named.TypeArguments.Any(IsCoreLibInternal),
        _ => false,
    };

    /// <summary>The BCL type a corelib shadow stands for (List&lt;int&gt;.Enumerator for ListImpl&lt;int&gt;.Enumerator).</summary>
    private ITypeSymbol RealType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                var element = RealType(array.ElementType);
                return ReferenceEquals(element, array.ElementType) ? array : Compilation.CreateArrayTypeSymbol(element, array.Rank);
            case IPointerTypeSymbol pointer:
                var pointed = RealType(pointer.PointedAtType);
                return ReferenceEquals(pointed, pointer.PointedAtType) ? pointer : Compilation.CreatePointerTypeSymbol(pointed);
            case INamedTypeSymbol named:
                // Rebuilding a definition from its own parameters would make it a constructed type.
                var unmapped = IsDef(named)
                    ? ((INamedTypeSymbol)CoreLib.Unmap(named)).OriginalDefinition
                    : (INamedTypeSymbol)CoreLib.Unmap(TypeSubstitution.EraseTupleNames(named, Compilation));
                if (unmapped.ContainingType is { } outer && CoreLib.IsCoreLib(unmapped.OriginalDefinition)
                    && RealType(outer) is INamedTypeSymbol realOuter && !ReferenceEquals(realOuter, outer)
                    && realOuter.GetTypeMembers(unmapped.Name, unmapped.Arity).FirstOrDefault() is { } nested)
                    return unmapped.Arity > 0 && !IsDef(unmapped) ? nested.Construct([.. unmapped.TypeArguments.Select(RealType)]) : nested;
                return unmapped;
            default:
                return type;
        }
    }

    /// <summary>
    /// Type.FullName: like the method table's name, but generic arguments are assembly-qualified
    /// ("List`1[[System.Int32, System.Private.CoreLib, ...]]"); null for generic parameters and types
    /// containing them (except definitions). <paramref name="unknown"/> is set when csharpc can't tell.
    /// </summary>
    private string? FullNameOf(ITypeSymbol type, ref bool unknown)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FullNameOf(array.ElementType, ref unknown) is { } e ? e + ArraySuffix(array) : null;
            case IPointerTypeSymbol pointer:
                return FullNameOf(pointer.PointedAtType, ref unknown) is { } p ? p + "*" : null;
            case INamedTypeSymbol named:
            {
                if (CoreLib.IsCoreLib(named.OriginalDefinition)) unknown = true;  // corelib-internal: no .NET counterpart
                var definition = Mangler.MetadataName(named.OriginalDefinition);
                var arguments = ChainArguments(named);
                if (arguments.Count == 0 || IsDef(named)) return definition;
                if (arguments.Any(TypeSubstitution.IsOpen)) return null;
                var parts = new List<string>();
                foreach (var argument in arguments)
                {
                    var name = FullNameOf(argument, ref unknown);
                    var assembly = AssemblyOf(argument);
                    if (assembly is null) unknown = true;
                    parts.Add("[" + name + ", " + assembly + "]");
                }
                return definition + "[" + string.Join(",", parts) + "]";
            }
            default:
                return null;
        }
    }

    private static string ArraySuffix(IArrayTypeSymbol array) =>
        array.IsSZArray ? "[]" : array.Rank == 1 ? "[*]" : "[" + new string(',', array.Rank - 1) + "]";

    private static string? NamespaceOf(INamedTypeSymbol type)
    {
        while (type.ContainingType is { } outer) type = outer;
        return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null;
    }

    /// <summary>
    /// Type.Assembly.FullName, as .NET reports it at run time; null when csharpc can't tell (then the
    /// program gets a NotSupportedException instead of a wrong answer).
    /// </summary>
    internal string? AssemblyOf(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array: return AssemblyOf(array.ElementType);
            case IPointerTypeSymbol pointer: return AssemblyOf(pointer.PointedAtType);
            case ITypeParameterSymbol parameter:
                return parameter.ContainingType is { } owner ? AssemblyOf(owner) : null;
            case INamedTypeSymbol named:
            {
                var definition = RealType(named.OriginalDefinition) is INamedTypeSymbol real ? real.OriginalDefinition : named.OriginalDefinition;
                if (CoreLib.IsCoreLib(definition)) return null;
                var assembly = definition.ContainingAssembly;
                if (assembly is null) return null;
                if (SymbolEqualityComparer.Default.Equals(assembly, Compilation.Assembly)) return ProgramAssemblyName(definition);
                if (Il.Contains(assembly)) return assembly.Identity.GetDisplayName();
                // A reference assembly: follow the shared framework's type forwards (List<T> is in
                // System.Collections.dll's reference assembly but System.Private.CoreLib at run time).
                var top = definition;
                while (top.ContainingType is { } outer) top = outer;
                var ns = top.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : "";
                return SharedFramework.DefiningAssembly(assembly.Identity.Name, ns, top.MetadataName);
            }
            default:
                return null;
        }
    }

    /// <summary>"name, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" for the program's own types.</summary>
    internal string? ProgramAssemblyName(ISymbol? symbol)
    {
        if (MainIl is not null) return MainIl.Symbol.Identity.GetDisplayName();
        var unit = ProgramAssemblies.Count > 1 && symbol?.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree?.FilePath is { } path
            ? ProgramAssemblies.FirstOrDefault(a => a.Files.Contains(Path.GetFullPath(path)))
            : null;
        unit ??= ProgramAssemblies.FirstOrDefault(a => a.IsRoot);
        var name = unit?.IsRoot != false ? Compilation.AssemblyName ?? unit?.Name : unit.Name;
        var version = unit is null ? "1.0.0.0" : unit.Version;
        if (version == "source")
        {
            // No SDK-generated [AssemblyVersion]: the program's own, or 0.0.0.0 (unless it is in another
            // project of the build, whose attributes this compilation doesn't see).
            if (unit is { IsRoot: false }) return null;
            version = Compilation.Assembly.Identity.Version.ToString();
        }
        return name is null || version is null ? null : $"{name}, Version={version}, Culture=neutral, PublicKeyToken=null";
    }
}
