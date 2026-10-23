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
    /// Method table for boxed enums: { csrt_Object_MT, "Ns.Color", 8 + size, 0, IFormattable map, [ToString, Equals, GetHashCode] }.
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
        var formattable = Compilation.GetTypeByMetadataName("System.IFormattable")!;
        var slotsInit = LlvmContext.ConstArray(Llvm.Ptr, formatThunk);
        var slotTable = Module.AddGlobal(Mangler.TypeSymbol(enumType, "BoxITBL"), Native.LLVMTypeOf(slotsInit), slotsInit, constant: true, linkage: LLVMLinkage.Internal);
        var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
        var mapInit = LlvmContext.ConstArray(entryType, LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(formattable), slotTable));
        var map = Module.AddGlobal(Mangler.TypeSymbol(enumType, "BoxIMAP"), Native.LLVMTypeOf(mapInit), mapInit, constant: true, linkage: LLVMLinkage.Internal);
        Native.LLVMSetInitializer(global, Llvm.ConstStruct(
            ExternalGlobal("csrt_Object_MT"),
            CString(Mangler.MetadataName(enumType)),
            Llvm.ConstInt(Llvm.I32, 8 + (long)Types.Target.SizeOf(valueType)),
            Llvm.ConstInt(Llvm.I32, 0),
            map, Llvm.ConstInt(Llvm.I32, 1), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, thunk, Runtime("csrt_valuetype_equals", Llvm.I1, Llvm.Ptr, Llvm.Ptr).Fn,
                hashThunk)));
        return global;
    }

    /// <summary>CLR full name as Type.FullName reports it: "System.Int32", "Program+Nested", "System.String[]".</summary>
    public static string ClrName(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol { Rank: 1 } a => ClrName(a.ElementType) + "[]",
        IArrayTypeSymbol a => ClrName(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]",
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
        var init = ArrayMethodTableInit(name, elementType, LlvmContext.Null(Llvm.Ptr), 0);
        var global = Module.AddGlobal("csrt.arraymt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        _taggedArrays.Add(new TaggedArray(name, elementType, global));
        return _arrayMethodTables[key] = global;
    }

    /// <summary>
    /// Header layout = TypeMapper.MethodTableHeader. The interface map is filled in once the program's
    /// array interfaces are known (ModuleCodeGen.CoreLib.cs, RefreshArrayInterfaceMaps).
    /// </summary>
    private LLVMValueRef ArrayMethodTableInit(string name, ITypeSymbol elementType, LLVMValueRef interfaceMap, int interfaceCount)
    {
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var slots = defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn).ToArray();
        var flags = Optimizations.HoldsNoReferences(elementType) ? 8 /* CSRT_MT_NO_REFERENCES */ : 0;
        return Llvm.ConstStruct(ExternalGlobal("csrt_Array_MT"), CString(name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, flags),
            interfaceMap, Llvm.ConstInt(Llvm.I32, interfaceCount), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, slots));
    }

    /// <summary>A name-only method table identifying a library type for Type objects (never instantiated).</summary>
    private LLVMValueRef TypeOnlyMethodTable(INamedTypeSymbol type)
    {
        // The CLR name with type arguments ("System.Collections.Generic.List`1[System.String]"), as
        // Type.ToString prints it. One table per constructed type: List<int> and List<string> are
        // different types for typeof and for the type arguments generic variance compares at run time.
        var name = Mangler.MetadataName(type);
        var key = "type:" + CoreLib.Map(TypeSubstitution.EraseTupleNames(type, Compilation)).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (_arrayMethodTables.TryGetValue(key, out var existing)) return existing;
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        var global = Module.AddGlobal("csrt.typemt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        return _arrayMethodTables[key] = global;
    }

    /// <summary>The method table that identifies a type at runtime (for typeof / Type objects).</summary>
    public LLVMValueRef TypeMethodTable(ITypeSymbol type, IOperation at)
    {
        if (BclValueTypes.IsDecimal(type)) return ExternalGlobal("csrt_Boxed_Decimal_MT");
        if (BclValueTypes.IsTimeSpan(type)) return ExternalGlobal("csrt_Boxed_TimeSpan_MT");
        if (BclValueTypes.IsDateTime(type)) return ExternalGlobal("csrt_Boxed_DateTime_MT");
        if (BclValueTypes.IsBigInteger(type)) return ExternalGlobal("csrt_Boxed_BigInteger_MT");
        switch (type.SpecialType)
        {
            case SpecialType.System_String: return ExternalGlobal("csrt_String_MT");
            case SpecialType.System_Object: return ExternalGlobal("csrt_Object_MT");
            case SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_UInt32 or SpecialType.System_UInt64
                or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Boolean or SpecialType.System_Char:
                return ExternalGlobal($"csrt_Boxed_{type.MetadataName}_MT");
        }
        return type switch
        {
            IArrayTypeSymbol array => ArrayMethodTable(array.ElementType),
            INamedTypeSymbol { TypeKind: TypeKind.Enum } e => EnumMethodTable(e),
            // Closed generic instantiations (and corelib shadows such as List<int>) use the same method
            // table their instances carry, so typeof(G<int>) == obj.GetType() holds.
            INamedTypeSymbol { TypeKind: TypeKind.Class } c when !c.IsStatic && IsUserType(CoreLib.TryMap(c) ?? c) && !c.IsUnboundGenericType && !TypeSubstitution.IsOpen(c) => GetLayout(c).MethodTable,
            INamedTypeSymbol { TypeKind: TypeKind.Struct } s when IsUserType(CoreLib.TryMap(s) ?? s) && !s.IsUnboundGenericType && !TypeSubstitution.IsOpen(s) => StructMethodTable(s),
            INamedTypeSymbol ex when IsLibraryException(ex) => GetLayout(ex).MethodTable,
            INamedTypeSymbol named => TypeOnlyMethodTable(TypeSubstitution.IsOpen(named) ? named.OriginalDefinition : named),
            _ => throw CompileError.Unsupported($"typeof({type})", at),
        };
    }
}
