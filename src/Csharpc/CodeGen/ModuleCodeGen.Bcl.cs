using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<string, LLVMValueRef> _arrayMethodTables = [];
    private readonly Dictionary<string, LLVMValueRef> _comparers = [];

    private LoweredFunction NewComparer(string key, out bool existed)
    {
        var type = Llvm.FunctionType(Llvm.I32, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
        if (_comparers.TryGetValue(key, out var fn)) { existed = true; return new LoweredFunction(fn, type); }
        existed = false;
        fn = Module.AddFunction("csharpc.compare." + key, type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        _comparers[key] = fn;
        return new LoweredFunction(fn, type);
    }

    /// <summary>int cmp(ctx = Comparison&lt;T&gt; delegate, T* a, T* b) for csrt_array_introsort.</summary>
    public LLVMValueRef DelegateComparer(ITypeSymbol element, INamedTypeSymbol comparison)
    {
        var lowered = NewComparer("delegate." + ClrName(element), out var existed);
        if (existed) return lowered.Fn;
        var g = new FunctionCodeGen(this, null, lowered);
        g.EmitSynthetic(() =>
        {
            var a = g.LoadValue(g.ParamOf(1), element);
            var b = g.LoadValue(g.ParamOf(2), element);
            g.Builder.Ret(g.InvokeDelegate(g.ParamOf(0), comparison, a, b));
        });
        return lowered.Fn;
    }

    /// <summary>
    /// Comparer&lt;T&gt;.Default for a user type implementing IComparable&lt;T&gt;: nulls sort first, then
    /// a.CompareTo(b) (devirtualized when possible). Null if the type isn't IComparable&lt;T&gt;.
    /// </summary>
    public LLVMValueRef? ComparableComparer(ITypeSymbol element)
    {
        var comparable = element.AllInterfaces.FirstOrDefault(i =>
            i.OriginalDefinition.ToDisplayString() == "System.IComparable<T>" && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], element));
        if (comparable is null || !IsUserType(element)) return null;
        var member = comparable.GetMembers("CompareTo").OfType<IMethodSymbol>().First();
        if (element.FindImplementationForInterfaceMember(member) is not IMethodSymbol impl) return null;

        var lowered = NewComparer("comparable." + ClrName(element), out var existed);
        if (existed) return lowered.Fn;
        var g = new FunctionCodeGen(this, null, lowered);
        g.EmitSynthetic(() =>
        {
            var b = g.Builder;
            if (element.IsValueType)
            {
                // struct: CompareTo(this = &a, other = b)
                var other = g.LoadValue(g.ParamOf(2), element);
                var f = GetFunction(impl);
                b.Ret(b.Call(f.Type, f.Fn, [g.ParamOf(1), other]));
                return;
            }
            var x = b.Load(Llvm.Ptr, g.ParamOf(1));
            var y = b.Load(Llvm.Ptr, g.ParamOf(2));
            var xNull = b.ICmp(LLVMIntPredicate.EQ, x, LlvmContext.Null(Llvm.Ptr));
            var yNull = b.ICmp(LLVMIntPredicate.EQ, y, LlvmContext.Null(Llvm.Ptr));
            var call = g.NewBlock("cmp.call");
            var nulls = g.NewBlock("cmp.nulls");
            b.CondBr(b.BinOp(LLVMOpcode.Or, xNull, yNull), nulls, call);
            b.PositionAtEnd(nulls);
            // both null: 0; x null: -1; y null: 1
            b.Ret(b.Select(xNull, b.Select(yNull, Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, -1)), Llvm.ConstInt(Llvm.I32, 1)));
            b.PositionAtEnd(call);
            var dispatch = (impl.IsVirtual || impl.IsOverride || impl.IsAbstract) && !impl.IsSealed && !element.IsSealed;
            if (dispatch) b.Ret(g.VirtualCall(impl, x, [x, y]));
            else
            {
                var f = GetFunction(impl);
                b.Ret(b.Call(f.Type, f.Fn, [x, y]));
            }
        });
        return lowered.Fn;
    }
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _enumMethodTables = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Method table for boxed enums: { csrt_Object_MT, "Ns.Color", 8 + size, 0, IFormattable + IComparable map, [ToString, Equals, GetHashCode] }.
    /// ToString formats the payload by name (EnumFormatter); equality is bytewise, the hash is the underlying integer's.
    /// </summary>
    public LLVMValueRef EnumMethodTable(INamedTypeSymbol enumType)
    {
        enumType = enumType.OriginalDefinition;
        if (_enumMethodTables.TryGetValue(enumType, out var existing)) return existing;
        var underlying = enumType.EnumUnderlyingType!;
        var valueType = Types.Value(underlying);
        var mtType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32,
            LlvmContext.Array(Llvm.Ptr, ClassLayout.ObjectSlotCount));
        var global = Module.AddGlobal(Mangler.TypeSymbol(enumType, "BoxMT"), mtType, constant: true, linkage: LLVMLinkage.Internal);
        _enumMethodTables[enumType] = global;

        // ToString thunk: (box) => EnumFormatter(*(T*)(box + 8))
        var thunkType = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr);
        var thunk = Module.AddFunction(Mangler.TypeSymbol(enumType, "BoxToString"), thunkType);
        Native.LLVMSetLinkage(thunk, LLVMLinkage.Internal);
        using (var b = new IrBuilder(Llvm))
        {
            b.PositionAtEnd(b.AppendBlock(thunk, "entry"));
            var payload = b.Load(valueType, b.InBoundsGep(Llvm.I8, thunk.Param(0), [Llvm.ConstInt(Llvm.I64, 8)]));
            var formatter = EnumFormatter(enumType);
            b.Ret(b.Call(formatter.Type, formatter.Fn, [payload]));
        }
        // IFormattable.ToString(format, provider), so composite formatting ("{0:D}") and FormatObject
        // honour enum format strings on boxed enums.
        var formatThunk = Module.AddFunction(Mangler.TypeSymbol(enumType, "BoxToStringFormat"), Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr));
        Native.LLVMSetLinkage(formatThunk, LLVMLinkage.Internal);
        using (var b = new IrBuilder(Llvm))
        {
            b.PositionAtEnd(b.AppendBlock(formatThunk, "entry"));
            var payload = b.Load(valueType, b.InBoundsGep(Llvm.I8, formatThunk.Param(0), [Llvm.ConstInt(Llvm.I64, 8)]));
            var formatter = EnumFormatWith(enumType);
            b.Ret(b.Call(formatter.Type, formatter.Fn, [payload, formatThunk.Param(1)]));
        }
        var hashThunk = Module.AddFunction(Mangler.TypeSymbol(enumType, "BoxGetHashCode"), Llvm.FunctionType(Llvm.I32, Llvm.Ptr));
        Native.LLVMSetLinkage(hashThunk, LLVMLinkage.Internal);
        using (var b = new IrBuilder(Llvm))
        {
            b.PositionAtEnd(b.AppendBlock(hashThunk, "entry"));
            var payload = b.Load(valueType, b.InBoundsGep(Llvm.I8, hashThunk.Param(0), [Llvm.ConstInt(Llvm.I64, 8)]));
            b.Ret(EnumHash(b, payload, enumType));
        }
        // IComparable.CompareTo(object), as System.Enum's: null sorts first, another type throws.
        var compareType = Llvm.FunctionType(Llvm.I32, Llvm.Ptr, Llvm.Ptr);
        var compareThunk = Module.AddFunction(Mangler.TypeSymbol(enumType, "BoxCompareTo"), compareType);
        Native.LLVMSetLinkage(compareThunk, LLVMLinkage.Internal);
        new FunctionCodeGen(this, null, new LoweredFunction(compareThunk, compareType)).EmitEnumCompareObjectThunk(enumType, global);

        var formattable = Compilation.GetTypeByMetadataName("System.IFormattable")!;
        var comparable = Compilation.GetTypeByMetadataName("System.IComparable")!;
        var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
        LLVMValueRef Entry(INamedTypeSymbol itf, LLVMValueRef slot, string suffix)
        {
            var slotsInit = LlvmContext.ConstArray(Llvm.Ptr, slot);
            var slotTable = Module.AddGlobal(Mangler.TypeSymbol(enumType, suffix), Native.LLVMTypeOf(slotsInit), slotsInit, constant: true, linkage: LLVMLinkage.Internal);
            return LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(itf), slotTable);
        }
        var mapInit = LlvmContext.ConstArray(entryType, Entry(formattable, formatThunk, "BoxITBL"), Entry(comparable, compareThunk, "BoxICMP"));
        var map = Module.AddGlobal(Mangler.TypeSymbol(enumType, "BoxIMAP"), Native.LLVMTypeOf(mapInit), mapInit, constant: true, linkage: LLVMLinkage.Internal);
        Native.LLVMSetInitializer(global, Llvm.ConstStruct(
            ExternalGlobal("csrt_Object_MT"),
            CString(Mangler.MetadataName(enumType)),
            Llvm.ConstInt(Llvm.I32, 8 + (long)Types.Target.SizeOf(valueType)),
            Llvm.ConstInt(Llvm.I32, 64 | 128 /* CSRT_MT_VALUE_TYPE | CSRT_MT_ENUM */),
            map, Llvm.ConstInt(Llvm.I32, 2), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, thunk, Runtime("csrt_valuetype_equals", Llvm.I1, Llvm.Ptr, Llvm.Ptr).Fn,
                hashThunk)));
        return global;
    }

    /// <summary>CLR full name as Type.FullName reports it: "System.Int32", "Program+Nested", "System.String[]".</summary>
    public static string ClrName(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol { Rank: 1 } a => ClrName(a.ElementType) + "[]",
        IArrayTypeSymbol a => ClrName(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]",
        IPointerTypeSymbol p => ClrName(p.PointedAtType) + "*",
        INamedTypeSymbol named => Mangler.MetadataName(TypeSubstitution.IsOpen(named) ? named.OriginalDefinition : named),
        _ => type.Name,
    };

    /// <summary>
    /// Per-element-type array method table: { csrt_Array_MT, "System.Int32[]", 0, 0, [Object virtuals] }.
    /// Arrays get it right after allocation so GetType() and ToString() report the element type.
    /// </summary>
    /// <summary>Element type of every element-typed array MT, for interface registration at startup.</summary>

    public LLVMValueRef ArrayMethodTable(ITypeSymbol elementType)
    {
        var name = ClrName(elementType) + "[]";
        // Keyed by the exact element type: List<int>[] and List<Edge>[] share a display name, but
        // not their interfaces (and a corelib shadow is the same type as what it shadows).
        var key = "array:" + CoreLib.Map(TypeSubstitution.EraseTupleNames(elementType, Compilation)).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (_arrayMethodTables.TryGetValue(key, out var existing)) return existing;
        var tagged = new TaggedArray(name, elementType, default) { Element = LlvmContext.Null(Llvm.Ptr) };
        var init = ArrayMethodTableInit(tagged, LlvmContext.Null(Llvm.Ptr), 0);
        var global = Module.AddGlobal("csrt.arraymt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        tagged = tagged with { Global = global };
        _taggedArrays.Add(tagged);
        RegisterTypeMethodTable(global, Compilation.CreateArrayTypeSymbol(elementType));
        _arrayMethodTables[key] = global;
        // After registering: the element's table may lead back here (arrays of arrays, a class with T[] fields).
        tagged.Element = ArrayElementMethodTable(elementType);
        if (!tagged.Element.IsNull) Native.LLVMSetInitializer(global, ArrayMethodTableInit(tagged, LlvmContext.Null(Llvm.Ptr), 0));
        return global;
    }

    /// <summary>
    /// Header layout = TypeMapper.MethodTableHeader, then the vtable and the element type's method table
    /// (csrt_ArrayMethodTable). The interface map is filled in once the program's array interfaces are known
    /// (ModuleCodeGen.CoreLib.cs, RefreshArrayInterfaceMaps).
    /// </summary>
    private LLVMValueRef ArrayMethodTableInit(TaggedArray array, LLVMValueRef interfaceMap, int interfaceCount)
    {
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var slots = defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn).ToArray();
        var elementType = array.ElementType;
        var flags = (Optimizations.HoldsNoReferences(elementType) ? 8 /* CSRT_MT_NO_REFERENCES */ : 0) | ArrayElementFlags(elementType);
        return Llvm.ConstStruct(ExternalGlobal("csrt_Array_MT"), CString(array.Name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, flags),
            // reserved = the element size (Array.CreateInstanceFromArrayType allocates from the Type: csrt.c).
            interfaceMap, Llvm.ConstInt(Llvm.I32, interfaceCount), Llvm.ConstInt(Llvm.I32, (long)Types.Target.SizeOf(Types.Storage(elementType))),
            LlvmContext.ConstArray(Llvm.Ptr, slots), array.Element);
    }

    /// <summary>
    /// csrt_MethodTable flags of an array type for casts between array types (csrt.c, array_assignable):
    /// CSRT_MT_REFERENCE_ELEMENTS, or the reduced type of integer/enum elements (int[] is a uint[] in the CLR).
    /// </summary>
    private static int ArrayElementFlags(ITypeSymbol element)
    {
        if (element.IsReferenceType) return 512; // CSRT_MT_REFERENCE_ELEMENTS
        var reduced = ReducedElementType(element) switch { "int8" => 1, "int16" => 2, "int32" => 3, "int64" => 4, "native int" => 5, _ => 0 };
        return reduced << 12; // CSRT_MT_ELEMENT_CLASS_SHIFT
    }

    /// <summary>
    /// The method table instances of an array's element type carry, for the CLR's array covariance checks
    /// (csrt_array_store_check): only where one table identifies the type (the one `is`/castclass test
    /// against, FunctionCodeGen.MethodTableFor), else null — no check, so a store can never throw wrongly.
    /// </summary>
    private LLVMValueRef ArrayElementMethodTable(ITypeSymbol element)
    {
        var none = LlvmContext.Null(Llvm.Ptr);
        if (!element.IsReferenceType || TypeSubstitution.IsOpen(element)) return none;
        var mapped = CoreLib.Map(element);
        try
        {
            return mapped switch
            {
                { SpecialType: SpecialType.System_Object } => ExternalGlobal("csrt_Object_MT"),
                { SpecialType: SpecialType.System_String } => ExternalGlobal("csrt_String_MT"),
                { SpecialType: SpecialType.System_Array } => ExternalGlobal("csrt_Array_MT"),
                IArrayTypeSymbol { IsSZArray: true } array => ArrayMethodTable(array.ElementType),
                IArrayTypeSymbol array => MdArrayMethodTable(array),
                INamedTypeSymbol { TypeKind: TypeKind.Interface } itf => InterfaceMethodTable(itf),
                // Variant delegates convert between instantiations (each has its own table): no single table.
                INamedTypeSymbol { TypeKind: TypeKind.Delegate } d when !FunctionCodeGen.MayBeVariant(d) => DelegateMethodTable(d),
                // Runtime objects whose instances share one table whatever their static type (tasks, Type, CTS).
                INamedTypeSymbol { Name: "Task" or "TaskCompletionSource" or "Type" or "CancellationTokenSource" or "ValueType" or "Enum" or "Delegate" or "MulticastDelegate" } => none,
                INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: false } c when IsUserType(c) || IsLibraryException(c) => GetLayout(c).MethodTable,
                _ => none,
            };
        }
        catch (CompileError) { return none; }
    }

    /// <summary>
    /// Rectangular arrays: one table per element type and rank ("System.Int32[,]"), the one instances are
    /// allocated with, so GetType()/ToString() and typeof agree. Derives from csrt_Array_MT (`is Array`);
    /// CSRT_MT_MDARRAY makes interface calls take the untyped-array path, as when they shared that table.
    /// </summary>
    public LLVMValueRef MdArrayMethodTable(IArrayTypeSymbol array)
    {
        var key = "mdarray:" + CoreLib.Map(TypeSubstitution.EraseTupleNames(array, Compilation)).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (_arrayMethodTables.TryGetValue(key, out var existing)) return existing;
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var slots = defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn).ToArray();
        var flags = 32 /* CSRT_MT_MDARRAY */ | (Optimizations.HoldsNoReferences(array.ElementType) ? 8 /* CSRT_MT_NO_REFERENCES */ : 0)
            | (array.ElementType.IsReferenceType ? 512 /* CSRT_MT_REFERENCE_ELEMENTS */ : 0);
        var name = ClrName(array);
        LLVMValueRef Init(LLVMValueRef element) => Llvm.ConstStruct(ExternalGlobal("csrt_Array_MT"), CString(name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, flags),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, array.Rank) /* reserved = rank */,
            LlvmContext.ConstArray(Llvm.Ptr, slots), element);
        var init = Init(LlvmContext.Null(Llvm.Ptr));
        var global = Module.AddGlobal("csrt.mdarraymt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        RegisterTypeMethodTable(global, array);
        _arrayMethodTables[key] = global;
        // The element type, as for one-dimensional arrays: stores into covariant rectangular arrays are checked too.
        var element = ArrayElementMethodTable(array.ElementType);
        if (!element.IsNull) Native.LLVMSetInitializer(global, Init(element));
        return global;
    }

    /// <summary>A name-only method table identifying a library type for Type objects (never instantiated).</summary>
    private LLVMValueRef TypeOnlyMethodTable(INamedTypeSymbol type)
    {
        // The CLR name with type arguments ("System.Collections.Generic.List`1[System.String]"), as
        // Type.ToString prints it; definitions list their parameters ("System.Collections.Generic.List`1[T]").
        // One table per constructed type: List<int> and List<string> are different types for typeof and
        // for the type arguments generic variance compares at run time.
        var name = Mangler.MetadataName(type);
        if (IsDef(type) && ChainArguments(type) is { Count: > 0 } parameters)
            name += "[" + string.Join(",", parameters.Select(p => p.Name)) + "]";
        var key = "type:" + CoreLib.Map(TypeSubstitution.EraseTupleNames(type, Compilation)).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return NameOnlyMethodTable(key, name, type.TypeKind == TypeKind.Interface ? 2 /* CSRT_MT_INTERFACE */ : 0, type);
    }

    private LLVMValueRef NameOnlyMethodTable(string key, string name, int flags, ITypeSymbol type)
    {
        if (_arrayMethodTables.TryGetValue(key, out var existing)) return existing;
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, flags),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        var global = Module.AddGlobal("csrt.typemt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        RegisterTypeMethodTable(global, type);
        return _arrayMethodTables[key] = global;
    }

    /// <summary>The method table that identifies a type at runtime (for typeof / Type objects).</summary>
    public LLVMValueRef TypeMethodTable(ITypeSymbol type, IOperation at)
    {
        LLVMValueRef Builtin(string name)
        {
            var mt = ExternalGlobal(name);
            RegisterTypeMethodTable(mt, type);
            return mt;
        }
        // typeof(List<>): Roslyn's unbound type has error-typed arguments, which substitution and the
        // corelib redirect keep; the generic type definition is what reflection describes.
        if (type is INamedTypeSymbol unbound && (unbound.IsUnboundGenericType || ChainArguments(unbound).Any(a => a.TypeKind == TypeKind.Error)))
            type = unbound.OriginalDefinition;
        if (BclValueTypes.IsDecimal(type)) return Builtin("csrt_Boxed_Decimal_MT");
        if (BclValueTypes.IsTimeSpan(type)) return Builtin("csrt_Boxed_TimeSpan_MT");
        if (BclValueTypes.IsDateTime(type)) return Builtin("csrt_Boxed_DateTime_MT");
        if (BclValueTypes.IsBigInteger(type)) return Builtin("csrt_Boxed_BigInteger_MT");
        if (type is INamedTypeSymbol { Name: "CancellationTokenSource", ContainingNamespace: { Name: "Threading", ContainingNamespace.Name: "System" } })
            return Builtin("csrt_CancellationTokenSource_MT");
        switch (type.SpecialType)
        {
            case SpecialType.System_String: return Builtin("csrt_String_MT");
            case SpecialType.System_Object: return Builtin("csrt_Object_MT");
            case SpecialType.System_ValueType: return Builtin("csrt_ValueType_MT");
            case SpecialType.System_Enum: return Builtin("csrt_Enum_MT");
            case SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_UInt32 or SpecialType.System_UInt64
                or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Boolean or SpecialType.System_Char
                or SpecialType.System_IntPtr or SpecialType.System_UIntPtr:
                return Builtin($"csrt_Boxed_{type.MetadataName}_MT");
        }
        return type switch
        {
            IArrayTypeSymbol { IsSZArray: true } array => ArrayMethodTable(array.ElementType),
            IArrayTypeSymbol array => MdArrayMethodTable(array),
            IPointerTypeSymbol pointer when !TypeSubstitution.IsOpen(pointer.PointedAtType) =>
                NameOnlyMethodTable("ptr:" + pointer.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), ClrName(pointer), 0, pointer),
            INamedTypeSymbol { TypeKind: TypeKind.Enum } e => EnumMethodTable(e),
            // Interfaces: the identity table interface maps refer to, so IsAssignableFrom and `is` agree.
            INamedTypeSymbol { TypeKind: TypeKind.Interface } i when !i.IsUnboundGenericType && !TypeSubstitution.IsOpen(i) => InterfaceMethodTable(i),
            // Delegates: the table delegate instances carry (obj.GetType() == typeof(Func<int>)).
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } d when !d.IsUnboundGenericType && !TypeSubstitution.IsOpen(d) => DelegateMethodTable(d),
            // Closed generic instantiations (and corelib shadows such as List<int>) use the same method
            // table their instances carry, so typeof(G<int>) == obj.GetType() holds.
            INamedTypeSymbol { TypeKind: TypeKind.Class } c when !c.IsStatic && IsUserType(CoreLib.TryMap(c) ?? c) && !c.IsUnboundGenericType && !TypeSubstitution.IsOpen(c) => GetLayout(c).MethodTable,
            INamedTypeSymbol { TypeKind: TypeKind.Struct } s when IsUserType(CoreLib.TryMap(s) ?? s) && !s.IsUnboundGenericType && !TypeSubstitution.IsOpen(s) => StructMethodTable(s),
            INamedTypeSymbol ex when IsLibraryException(ex) => GetLayout(ex).MethodTable,
            INamedTypeSymbol named => TypeOnlyMethodTable(named.IsUnboundGenericType || TypeSubstitution.IsOpen(named) ? named.OriginalDefinition : named),
            _ => throw CompileError.Unsupported($"typeof({type})", at),
        };
    }
}
