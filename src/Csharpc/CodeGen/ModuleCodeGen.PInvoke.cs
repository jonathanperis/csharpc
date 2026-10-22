using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// [DllImport] (and the extern stubs the [LibraryImport] source generator emits): every P/Invoke
/// method becomes an internal function with the method's managed signature that marshals its
/// arguments, calls the native symbol with the platform C ABI (System V x86-64 or AAPCS64), captures errno for
/// SetLastError, and converts the result back.
///
/// Libraries: libc/libm (and "__Internal"/"csrt") are resolved by the static linker — a direct call,
/// as fast as C. Any other library is bound lazily with dlopen/dlsym on first call (cached), with
/// .NET's probing rules and DllNotFoundException / EntryPointNotFoundException on failure.
/// Marshalling here covers what DllImport does implicitly; [LibraryImport] marshalling is C#.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<IMethodSymbol, LoweredFunction> _pinvokeStubs = new(SymbolEqualityComparer.Default);
    private int _pinvokeCounter;

    private static readonly HashSet<string> s_directLinkLibraries =
        ["", "libc", "c", "libc.so", "libc.so.6", "libm", "m", "libm.so", "libm.so.6", "libdl", "libdl.so.2",
         "libpthread", "libpthread.so.0", "__Internal", "csrt"];

    public static bool IsPInvoke(IMethodSymbol method) => method.OriginalDefinition.GetDllImportData() is not null;

    private enum ParamKind { Direct, Bool, Char, String, Array, ByRef, Struct, StructByVal }

    private sealed record NativeParam(ParamKind Kind, ITypeSymbol Type, LLVMTypeRef[] NativeTypes, bool Unicode = false,
        LLVMTypeRef StructType = default, int BoolSize = 4);

    /// <summary>The managed-ABI stub for a P/Invoke method (params as csharpc passes them; no this/env).</summary>
    public LoweredFunction PInvokeStub(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        if (_pinvokeStubs.TryGetValue(method, out var existing)) return existing;
        var data = method.GetDllImportData()!;
        if (method.IsGenericMethod) throw CompileError.Unsupported("generic P/Invoke methods", method);

        var unicodeDefault = data.CharacterSet == System.Runtime.InteropServices.CharSet.Unicode;
        var managedParams = method.Parameters.Select(p => p.RefKind != RefKind.None ? Llvm.Ptr : Types.Value(p.Type)).ToArray();
        var managedRet = method.ReturnsVoid ? Llvm.Void : method.RefKind != RefKind.None ? Llvm.Ptr : Types.Value(method.ReturnType);
        var managedType = Llvm.FunctionType(managedRet, managedParams);
        var stub = Module.AddFunction(Mangler.Method(method) + "$pinvoke" + _pinvokeCounter++, managedType);
        Native.LLVMSetLinkage(stub, LLVMLinkage.Internal);
        var lowered = _pinvokeStubs[method] = new LoweredFunction(stub, managedType);

        // ---- native signature ----
        var parameters = method.Parameters.Select(p => ClassifyParameter(p, unicodeDefault)).ToList();
        var (nativeRet, retKind, retStruct, sret) = ClassifyReturn(method, unicodeDefault);
        List<LLVMTypeRef> nativeParamTypes = [];
        if (sret) nativeParamTypes.Add(Llvm.Ptr);
        foreach (var p in parameters) nativeParamTypes.AddRange(p.NativeTypes);
        var nativeType = Llvm.FunctionType(sret ? Llvm.Void : nativeRet, [.. nativeParamTypes]);

        var library = data.ModuleName ?? "";
        var entry = data.EntryPointName ?? method.Name;
        var direct = s_directLinkLibraries.Contains(library);
        var nativeAttributes = NativeAttributes(parameters, retKind, method, sret, retStruct);
        LLVMValueRef native = default;
        if (direct)
        {
            native = Module.GetFunction(entry);
            if (native.IsNull)
            {
                native = Module.AddFunction(entry, nativeType);
                // Weak: a symbol missing from libc becomes null (EntryPointNotFoundException at the call,
                // like .NET) instead of a link error.
                Native.LLVMSetLinkage(native, (LLVMLinkage)12 /* LLVMExternalWeakLinkage */);
                foreach (var (index, attribute) in nativeAttributes) Native.LLVMAddAttributeAtIndex(native, index, attribute);
            }
        }

        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(stub, "entry"));

        // ---- marshal arguments ----
        List<LLVMValueRef> args = [];
        List<LLVMValueRef> temps = [];
        LLVMValueRef sretSlot = default;
        if (sret)
        {
            sretSlot = b.Alloca(retStruct, "sret");
            args.Add(sretSlot);
        }
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var value = stub.Param((uint)i);
            switch (p.Kind)
            {
                case ParamKind.Direct or ParamKind.ByRef:
                    args.Add(value);
                    break;
                case ParamKind.Bool:
                    args.Add(b.Cast(LLVMOpcode.ZExt, value, p.NativeTypes[0]));
                    break;
                case ParamKind.Char:
                    args.Add(p.Unicode ? value : b.Cast(LLVMOpcode.Trunc, value, Llvm.I8));
                    break;
                case ParamKind.String:
                {
                    var toNative = Runtime("csrt_pinvoke_string_to_native", Llvm.Ptr, Llvm.Ptr, Llvm.I1);
                    var nativeString = b.Call(toNative.Type, toNative.Fn, [value, p.Unicode ? Llvm.True : Llvm.False]);
                    temps.Add(nativeString);
                    args.Add(nativeString);
                    break;
                }
                case ParamKind.Array:
                {
                    // Blittable element arrays are passed by reference to element 0 (the GC never moves objects).
                    var element = ((IArrayTypeSymbol)p.Type).ElementType;
                    var data0 = b.InBoundsGep(Types.ArrayHeader(Types.Storage(element)), value,
                        [Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 2), Llvm.ConstInt(Llvm.I64, 0)]);
                    var isNull = b.ICmp(LLVMIntPredicate.EQ, value, LlvmContext.Null(Llvm.Ptr));
                    args.Add(b.Select(isNull, LlvmContext.Null(Llvm.Ptr), data0));
                    break;
                }
                case ParamKind.Struct:
                {
                    // Register-passed struct: split into the scalars the C ABI passes (System V eightbytes,
                    // or the single i64 / [2 x i64] / HFA array clang uses for AAPCS64).
                    var slot = b.Alloca(LargerOf(p.StructType, CoercedStruct(p.NativeTypes)), "abi");
                    b.Store(value, slot);
                    var coerced = CoercedStruct(p.NativeTypes);
                    for (var k = 0; k < p.NativeTypes.Length; k++)
                        args.Add(b.Load(p.NativeTypes[k], b.StructGep(coerced, slot, (uint)k)));
                    break;
                }
                case ParamKind.StructByVal:
                {
                    var slot = b.Alloca(p.StructType, "byval");
                    b.Store(value, slot);
                    args.Add(slot);
                    break;
                }
            }
        }

        // ---- call ----
        LLVMValueRef callee = native;
        if (direct && Native.LLVMGetLinkage(native) == (LLVMLinkage)12)
        {
            var missing = b.AppendBlock(stub, "missing");
            var present = b.AppendBlock(stub, "present");
            b.CondBr(b.ICmp(LLVMIntPredicate.EQ, native, LlvmContext.Null(Llvm.Ptr)), missing, present);
            b.PositionAtEnd(missing);
            var thrower = Runtime("csrt_pinvoke_missing_entry", Llvm.Void, Llvm.Ptr, Llvm.Ptr);
            b.Call(thrower.Type, thrower.Fn, [CString(library), CString(entry)]);
            b.Unreachable();
            b.PositionAtEnd(present);
        }
        if (!direct)
        {
            var cache = Module.AddGlobal($"__pinvoke_cache{_pinvokeCounter}", Llvm.Ptr, LlvmContext.Null(Llvm.Ptr), linkage: LLVMLinkage.Internal);
            var cached = b.Load(Llvm.Ptr, cache, "fnptr");
            var resolve = b.AppendBlock(stub, "resolve");
            var call = b.AppendBlock(stub, "call");
            b.CondBr(b.ICmp(LLVMIntPredicate.EQ, cached, LlvmContext.Null(Llvm.Ptr)), resolve, call);
            b.PositionAtEnd(resolve);
            var resolver = Runtime("csrt_pinvoke_resolve", Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
            var resolved = b.Call(resolver.Type, resolver.Fn, [CString(library), CString(entry)]);
            b.Store(resolved, cache);
            b.Br(call);
            b.PositionAtEnd(call);
            callee = b.Load(Llvm.Ptr, cache, "fn");
        }
        if (data.SetLastError)
        {
            var clear = Runtime("csrt_pinvoke_clear_errno", Llvm.Void);
            b.Call(clear.Type, clear.Fn, []);
        }
        var result = b.Call(nativeType, callee, [.. args]);
        foreach (var (index, attribute) in nativeAttributes) Native.LLVMAddCallSiteAttribute(result, index, attribute);
        if (data.SetLastError)
        {
            var save = Runtime("csrt_pinvoke_save_errno", Llvm.Void);
            b.Call(save.Type, save.Fn, []);
        }
        foreach (var temp in temps)
        {
            var free = Runtime("csrt_pinvoke_free", Llvm.Void, Llvm.Ptr);
            b.Call(free.Type, free.Fn, [temp]);
        }

        // ---- return ----
        switch (retKind)
        {
            case ParamKind.Direct when method.ReturnsVoid:
                b.RetVoid();
                break;
            case ParamKind.Direct or ParamKind.ByRef:
                b.Ret(result);
                break;
            case ParamKind.Bool:
                b.Ret(b.ICmp(LLVMIntPredicate.NE, result, Llvm.ConstInt(nativeRet, 0)));
                break;
            case ParamKind.Char:
                b.Ret(nativeRet.IntWidth == 16 ? result : b.Cast(LLVMOpcode.ZExt, result, Llvm.I16));
                break;
            case ParamKind.String:
            {
                var fromNative = Runtime("csrt_pinvoke_string_from_native", Llvm.Ptr, Llvm.Ptr, Llvm.I1, Llvm.I1);
                var unicode = ReturnIsUnicode(method, unicodeDefault);
                b.Ret(b.Call(fromNative.Type, fromNative.Fn, [result, unicode ? Llvm.True : Llvm.False, Llvm.True]));
                break;
            }
            case ParamKind.Struct:
            {
                var slot = b.Alloca(LargerOf(retStruct, nativeRet), "ret");
                b.Store(result, slot);
                b.Ret(b.Load(retStruct, slot));
                break;
            }
            case ParamKind.StructByVal:
                b.Ret(b.Load(retStruct, sretSlot));
                break;
            default:
                throw CompileError.Unsupported($"P/Invoke return type '{method.ReturnType}'", method);
        }
        return lowered;
    }

    /// <summary>ABI attributes of the native call (sret, byval, extension of small integers). They go on
    /// the call instruction too: LLVM lowers a call from its call-site attributes and only falls back to
    /// the callee's when it calls a known function, which a dlsym'd pointer is not.</summary>
    private List<(uint Index, LLVMAttributeRef Attribute)> NativeAttributes(List<NativeParam> parameters, ParamKind retKind,
        IMethodSymbol method, bool sret, LLVMTypeRef retStruct)
    {
        List<(uint, LLVMAttributeRef)> attributes = [];
        LLVMAttributeRef Enum(string name, ulong value = 0) =>
            Native.LLVMCreateEnumAttribute(Llvm.Ref, Native.LLVMGetEnumAttributeKindForName(name, (nuint)name.Length), value);
        LLVMAttributeRef Typed(string name, LLVMTypeRef type) =>
            Native.LLVMCreateTypeAttribute(Llvm.Ref, Native.LLVMGetEnumAttributeKindForName(name, (nuint)name.Length), type);
        uint index = 1;
        if (sret) attributes.Add((index++, Typed("sret", retStruct)));
        foreach (var p in parameters)
        {
            // AAPCS64 passes large structs as a plain pointer to a caller-owned copy (the slot the stub
            // made); byval would copy them onto the stack instead, which is the System V convention.
            if (p.Kind == ParamKind.StructByVal && Types.Target.IsX86_64)
                attributes.Add((index, Typed("byval", p.StructType)));
            // AAPCS64 composites that spill to the stack take 8-byte-aligned slots (clang: alignstack(8)).
            else if (p.Kind == ParamKind.Struct && Types.Target.IsAArch64)
                attributes.Add((index, Enum("alignstack", 8)));
            else if (p.NativeTypes.Length == 1 && p.NativeTypes[0].IsInteger && p.NativeTypes[0].IntWidth < 32)
                attributes.Add((index, Enum(p.Kind == ParamKind.Direct && TypeMapper.IsSigned(p.Type) ? "signext" : "zeroext")));
            index += (uint)p.NativeTypes.Length;
        }
        if (!method.ReturnsVoid && retKind is ParamKind.Direct or ParamKind.Bool or ParamKind.Char
            && Types.Value(method.ReturnType) is { IsInteger: true } rt && rt.IntWidth < 32 && retKind == ParamKind.Direct)
            attributes.Add((0, Enum(TypeMapper.IsSigned(method.ReturnType) ? "signext" : "zeroext")));
        return attributes;
    }

    private NativeParam ClassifyParameter(IParameterSymbol p, bool unicodeDefault)
    {
        var type = p.Type;
        var marshalAs = MarshalAsOf(p.GetAttributes());
        if (p.RefKind != RefKind.None)
        {
            if (!IsBlittable(type)) throw CompileError.Unsupported($"P/Invoke by-ref parameter of non-blittable type '{type}'", p);
            return new NativeParam(ParamKind.ByRef, type, [Llvm.Ptr]);
        }
        switch (type)
        {
            case { SpecialType: SpecialType.System_Boolean }:
            {
                // UnmanagedType: Bool = 2 (4-byte BOOL, the DllImport default), I1 = 3, U1 = 4, VariantBool = 0x25.
                var size = marshalAs switch { 3 or 4 => 1, 0x25 => 2, _ => 4 };
                return new NativeParam(ParamKind.Bool, type, [Llvm.IntType(size * 8)], BoolSize: size);
            }
            case { SpecialType: SpecialType.System_Char }:
            {
                var unicode = marshalAs switch { 6 => true, 3 or 4 => false, _ => unicodeDefault }; // U2 / I1, U1
                return new NativeParam(ParamKind.Char, type, [unicode ? Llvm.I16 : Llvm.I8], unicode);
            }
            case { SpecialType: SpecialType.System_String }:
            {
                var unicode = marshalAs switch { 0x15 => true, 0x14 => false, 0x30 => false, _ => unicodeDefault }; // LPWStr, LPStr, LPUTF8Str
                return new NativeParam(ParamKind.String, type, [Llvm.Ptr], unicode);
            }
            case IArrayTypeSymbol { Rank: 1, ElementType: var element } when IsBlittable(element):
                return new NativeParam(ParamKind.Array, type, [Llvm.Ptr]);
            case INamedTypeSymbol { TypeKind: TypeKind.Struct } s when !IsPrimitiveLike(s):
            {
                if (!IsBlittable(s)) throw CompileError.Unsupported($"P/Invoke parameter of non-blittable struct '{s}'", p);
                var llvmType = Types.Value(s);
                return ClassifyStruct(llvmType, p) is { } eightbytes
                    ? new NativeParam(ParamKind.Struct, type, eightbytes, StructType: llvmType)
                    : new NativeParam(ParamKind.StructByVal, type, [Llvm.Ptr], StructType: llvmType);
            }
            case INamedTypeSymbol { TypeKind: TypeKind.Delegate }:
                throw CompileError.Unsupported("delegate P/Invoke parameters (use [UnmanagedCallersOnly] + function pointers)", p);
            default:
                if (type.IsReferenceType && type.SpecialType != SpecialType.None)
                    throw CompileError.Unsupported($"P/Invoke parameter of type '{type}'", p);
                if (type.IsReferenceType) throw CompileError.Unsupported($"P/Invoke parameter of reference type '{type}'", p);
                return new NativeParam(ParamKind.Direct, type, [Types.Value(type)]);
        }
    }

    private (LLVMTypeRef Native, ParamKind Kind, LLVMTypeRef Struct, bool Sret) ClassifyReturn(IMethodSymbol method, bool unicodeDefault)
    {
        if (method.ReturnsVoid) return (Llvm.Void, ParamKind.Direct, default, false);
        var type = method.ReturnType;
        var marshalAs = MarshalAsOf(method.GetReturnTypeAttributes());
        switch (type)
        {
            case { SpecialType: SpecialType.System_Boolean }:
                return (Llvm.IntType(marshalAs switch { 3 or 4 => 8, 0x25 => 16, _ => 32 }), ParamKind.Bool, default, false);
            case { SpecialType: SpecialType.System_Char }:
                return (unicodeDefault ? Llvm.I16 : Llvm.I8, ParamKind.Char, default, false);
            case { SpecialType: SpecialType.System_String }:
                return (Llvm.Ptr, ParamKind.String, default, false);
            case INamedTypeSymbol { TypeKind: TypeKind.Struct } s when !IsPrimitiveLike(s):
            {
                if (!IsBlittable(s)) throw CompileError.Unsupported($"P/Invoke return of non-blittable struct '{s}'", method);
                var llvmType = Types.Value(s);
                return ClassifyStruct(llvmType, method) is { } eightbytes
                    ? (CoercedStruct(eightbytes), ParamKind.Struct, llvmType, false)
                    : (Llvm.Void, ParamKind.StructByVal, llvmType, true);
            }
            default:
                if (type.IsReferenceType) throw CompileError.Unsupported($"P/Invoke return of reference type '{type}'", method);
                return (Types.Value(type), ParamKind.Direct, default, false);
        }
    }

    private bool ReturnIsUnicode(IMethodSymbol method, bool unicodeDefault) =>
        MarshalAsOf(method.GetReturnTypeAttributes()) switch { 0x15 => true, 0x14 or 0x30 => false, _ => unicodeDefault };

    /// <summary>The UnmanagedType value of a [MarshalAs] attribute, or null.</summary>
    private static int? MarshalAsOf(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        var attr = attributes.FirstOrDefault(a => a.AttributeClass?.Name == "MarshalAsAttribute");
        if (attr is null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value switch { short s => s, int i => i, var v => v is null ? null : Convert.ToInt32(v) };
    }

    private static bool IsPrimitiveLike(INamedTypeSymbol type) =>
        type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum;

    /// <summary>Blittable: primitives (not bool/char), enums, pointers, and structs made only of those.</summary>
    private bool IsBlittable(ITypeSymbol type) => type switch
    {
        IPointerTypeSymbol or IFunctionPointerTypeSymbol => true,
        { SpecialType: SpecialType.System_Boolean or SpecialType.System_Char } => false,
        { SpecialType: SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_Single or SpecialType.System_Double } => true,
        INamedTypeSymbol { TypeKind: TypeKind.Enum } => true,
        INamedTypeSymbol { TypeKind: TypeKind.Struct } s when IsUserType(s) =>
            s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst).All(f => IsBlittable(f.Type)),
        _ => false,
    };

    /// <summary>Register types of a by-value struct under the target's C ABI, or null if it goes through
    /// memory (System V: byval / sret; AAPCS64: pointer to a copy / sret in x8).</summary>
    private LLVMTypeRef[]? ClassifyStruct(LLVMTypeRef type, ISymbol at) =>
        Types.Target.IsX86_64 ? ClassifySysV(type)
        : Types.Target.IsAArch64 ? ClassifyAapcs64(type)
        : throw CompileError.Unsupported($"P/Invoke struct passing on target '{Types.Target.Triple}'", at);

    // ---- AAPCS64 (Linux) classification, mirroring clang's AArch64ABIInfo ------------------------

    private LLVMTypeRef[]? ClassifyAapcs64(LLVMTypeRef type)
    {
        var size = Types.Target.SizeOf(type);
        if (size == 0) return null;
        List<(ulong Offset, LLVMTypeRef Type)> scalars = [];
        Flatten(type, 0, scalars);
        // Homogeneous floating-point aggregate: 1-4 members of one FP type and no padding, passed in
        // consecutive SIMD registers (s0-s3 / d0-d3) whatever the total size.
        var first = scalars.Count > 0 ? scalars[0].Type : default;
        if (scalars.Count is >= 1 and <= 4 && first.IsFloatingPoint
            && scalars.All(s => s.Type.Kind == first.Kind)
            && size == (ulong)scalars.Count * Types.Target.SizeOf(first))
            return [LlvmContext.Array(first, (ulong)scalars.Count)];
        // Other composites ≤ 16 bytes travel in one or two GPRs; larger ones by reference.
        if (size > 16) return null;
        return [size <= 8 ? Llvm.I64 : LlvmContext.Array(Llvm.I64, 2)];
    }

    // ---- System V x86-64 classification of small structs ---------------------------------------

    /// <summary>Eightbyte types for a struct ≤ 16 bytes, or null if it's passed in memory.</summary>
    private LLVMTypeRef[]? ClassifySysV(LLVMTypeRef type)
    {
        var size = Types.Target.SizeOf(type);
        if (size == 0 || size > 16) return null;
        List<(ulong Offset, LLVMTypeRef Type)> scalars = [];
        Flatten(type, 0, scalars);
        var count = (int)((size + 7) / 8);
        var result = new LLVMTypeRef[count];
        for (var i = 0; i < count; i++)
        {
            var lo = (ulong)i * 8;
            var inside = scalars.Where(s => s.Offset >= lo && s.Offset < lo + 8).ToList();
            var sse = inside.Count > 0 && inside.All(s => s.Type.IsFloatingPoint);
            if (sse)
            {
                var doubles = inside.Any(s => s.Type.Kind == LLVMTypeKind.Double);
                result[i] = doubles ? Llvm.F64 : inside.Count >= 2 ? Native.LLVMVectorType(Llvm.F32, 2) : Llvm.F32;
            }
            else
            {
                var bytes = Math.Min(8UL, size - lo);
                result[i] = Llvm.IntType((int)bytes * 8);
            }
        }
        return result;
    }

    private void Flatten(LLVMTypeRef type, ulong offset, List<(ulong, LLVMTypeRef)> scalars)
    {
        switch (type.Kind)
        {
            case LLVMTypeKind.Struct:
                for (uint i = 0; i < Native.LLVMCountStructElementTypes(type); i++)
                    Flatten(Native.LLVMStructGetTypeAtIndex(type, i), offset + Native.LLVMOffsetOfElement(Types.Target.DataLayout, type, i), scalars);
                break;
            case LLVMTypeKind.Array:
            {
                var element = Native.LLVMGetElementType(type);
                var elementSize = Types.Target.SizeOf(element);
                for (ulong i = 0; i < Native.LLVMGetArrayLength2(type); i++) Flatten(element, offset + i * elementSize, scalars);
                break;
            }
            default:
                scalars.Add((offset, type));
                break;
        }
    }

    private LLVMTypeRef CoercedStruct(LLVMTypeRef[] eightbytes) =>
        eightbytes.Length == 1 ? Llvm.Struct(eightbytes[0]) : Llvm.Struct(eightbytes);

    private LLVMTypeRef LargerOf(LLVMTypeRef a, LLVMTypeRef b) =>
        Types.Target.SizeOf(a) >= Types.Target.SizeOf(b) ? a : b;
}
