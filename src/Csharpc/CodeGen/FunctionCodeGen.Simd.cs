using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// <c>Vector128&lt;T&gt;</c> (and <c>System.Numerics.Vector&lt;T&gt;</c> at the same 16 bytes) on LLVM vectors (issue #252). A Vector128 is a <c>&lt;16 x i8&gt;</c> whatever its element type; an operation views it as
/// <c>&lt;N x T&gt;</c> by bitcast and returns it as <c>&lt;16 x i8&gt;</c> again, so <c>As&lt;T, U&gt;</c> and the <c>AsByte</c> family are the identity.
/// The framework's span helpers (<c>SpanHelpers.IndexOfValueType</c>, <c>IndexOfAny</c>, <c>SequenceEqual</c>...) test
/// <c>Vector128.IsHardwareAccelerated</c> and then run their vector path, which this serves for both front-ends: <see cref="TrySimdCall"/>
/// (the <c>Vector128</c> statics, the operators and instance members of <c>Vector128&lt;T&gt;</c>) and <see cref="TrySimdProperty"/>
/// (<c>Zero</c>, <c>AllBitsSet</c>, <c>Count</c>). Vector64/256/512 stay opaque bytes and their <c>IsHardwareAccelerated</c> false (the baseline
/// of x86-64 and arm64 is 128 bits; NativeAOT's default x64 target is the same), as do the ISA classes. A member without a lowering is an
/// explicit diagnostic.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>Whether the target has a 128-bit vector unit csharpc uses: x86-64 (SSE2) and arm64 (NEON); wasm32 keeps the scalar paths.</summary>
    internal static bool Vector128Accelerated { get; set; }

    internal static bool TargetHasVector128(string triple) =>
        triple.StartsWith("x86_64", StringComparison.Ordinal) || triple.StartsWith("aarch64", StringComparison.Ordinal) || triple.StartsWith("arm64", StringComparison.Ordinal);

    private readonly record struct SimdElement(ITypeSymbol Type, LLVMTypeRef Llvm, int Bits, bool IsFloat, bool IsSigned)
    {
        public int Count => 128 / Bits;
    }

    private static bool InIntrinsicsNamespace(INamedTypeSymbol type) =>
        type.ContainingNamespace is { Name: "Intrinsics", ContainingNamespace: { Name: "Runtime", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };

    /// <summary>System.Numerics.Vector / Vector&lt;T&gt;: 16 bytes here (NativeAOT's default on x64 and arm64), so the same lowering as Vector128.</summary>
    internal static bool InNumericsNamespace(INamedTypeSymbol type) =>
        type.ContainingNamespace is { Name: "Numerics", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    private static bool IsVector128Struct(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Vector128", Arity: 1 } n && InIntrinsicsNamespace(n)
        || type is INamedTypeSymbol { Name: "Vector", Arity: 1, ContainingType: null } v && InNumericsNamespace(v);

    private static bool IsVector128Class(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Vector128", Arity: 0 } n && InIntrinsicsNamespace(n)
        || type is INamedTypeSymbol { Name: "Vector", Arity: 0, ContainingType: null } v && InNumericsNamespace(v);

    private SimdElement ElementOf(ITypeSymbol type, IOperation? at = null)
    {
        type = S(type);
        var pointer = (int)T.PointerSize * 8;
        return type.SpecialType switch
        {
            SpecialType.System_Byte => new(type, L.I8, 8, false, false),
            SpecialType.System_SByte => new(type, L.I8, 8, false, true),
            SpecialType.System_UInt16 => new(type, L.I16, 16, false, false),
            SpecialType.System_Int16 => new(type, L.I16, 16, false, true),
            SpecialType.System_UInt32 => new(type, L.I32, 32, false, false),
            SpecialType.System_Int32 => new(type, L.I32, 32, false, true),
            SpecialType.System_UInt64 => new(type, L.I64, 64, false, false),
            SpecialType.System_Int64 => new(type, L.I64, 64, false, true),
            SpecialType.System_Single => new(type, L.F32, 32, true, true),
            SpecialType.System_Double => new(type, L.F64, 64, true, true),
            SpecialType.System_UIntPtr => new(type, pointer == 64 ? L.I64 : L.I32, pointer, false, false),
            SpecialType.System_IntPtr => new(type, pointer == 64 ? L.I64 : L.I32, pointer, false, true),
            _ => throw CompileError.Unsupported($"Vector128<{type}>: the element type is not supported (as in .NET)", at),
        };
    }

    private LLVMTypeRef VectorOf(SimdElement e) => Native.LLVMVectorType(e.Llvm, (uint)e.Count);
    private LLVMTypeRef IntVectorOf(SimdElement e) => Native.LLVMVectorType(e.Bits switch { 8 => L.I8, 16 => L.I16, 32 => L.I32, _ => L.I64 }, (uint)e.Count);
    private LLVMTypeRef RawVector => Native.LLVMVectorType(L.I8, 16);

    private LLVMValueRef View(LLVMValueRef raw, SimdElement e) => _b.Cast(LLVMOpcode.BitCast, raw, VectorOf(e));
    private LLVMValueRef IntView(LLVMValueRef raw, SimdElement e) => _b.Cast(LLVMOpcode.BitCast, raw, IntVectorOf(e));
    private LLVMValueRef Raw(LLVMValueRef typed) => _b.Cast(LLVMOpcode.BitCast, typed, RawVector);

    private LLVMValueRef Splat(LLVMValueRef scalar, SimdElement e)
    {
        var vector = VectorOf(e);
        var one = _b.InsertElement(Native.LLVMGetPoison(vector), scalar, L.ConstInt(L.I32, 0));
        return _b.ShuffleVector(one, Native.LLVMGetPoison(vector), LlvmContext.Null(Native.LLVMVectorType(L.I32, (uint)e.Count)));
    }

    /// <summary>The per-lane comparison as a mask (all ones where true), raw.</summary>
    private LLVMValueRef Compare(SimdElement e, string kind, LLVMValueRef rawA, LLVMValueRef rawB)
    {
        var (a, b) = (View(rawA, e), View(rawB, e));
        LLVMValueRef lanes;
        if (e.IsFloat)
        {
            var predicate = kind switch
            {
                "eq" => LLVMRealPredicate.OEQ, "gt" => LLVMRealPredicate.OGT, "ge" => LLVMRealPredicate.OGE,
                "lt" => LLVMRealPredicate.OLT, _ => LLVMRealPredicate.OLE,
            };
            lanes = _b.FCmp(predicate, a, b);
        }
        else
        {
            var predicate = kind switch
            {
                "eq" => LLVMIntPredicate.EQ,
                "gt" => e.IsSigned ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT,
                "ge" => e.IsSigned ? LLVMIntPredicate.SGE : LLVMIntPredicate.UGE,
                "lt" => e.IsSigned ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT,
                _ => e.IsSigned ? LLVMIntPredicate.SLE : LLVMIntPredicate.ULE,
            };
            lanes = _b.ICmp(predicate, a, b);
        }
        return Raw(_b.Cast(LLVMOpcode.SExt, lanes, IntVectorOf(e)));
    }

    /// <summary>Whether every lane (or any lane) of a mask is set, as a bool.</summary>
    private LLVMValueRef MaskBits(LLVMValueRef rawMask, SimdElement e)
    {
        var negative = _b.ICmp(LLVMIntPredicate.SLT, IntView(rawMask, e), LlvmContext.Null(IntVectorOf(e)));
        return _b.Cast(LLVMOpcode.BitCast, negative, L.IntType(e.Count));
    }

    private LLVMValueRef AllSet(LLVMValueRef rawMask, SimdElement e) =>
        _b.ICmp(LLVMIntPredicate.EQ, MaskBits(rawMask, e), L.ConstInt(L.IntType(e.Count), -1));

    private LLVMValueRef AnySet(LLVMValueRef rawMask, SimdElement e) =>
        _b.ICmp(LLVMIntPredicate.NE, MaskBits(rawMask, e), L.ConstInt(L.IntType(e.Count), 0));

    private LLVMValueRef Load128(LLVMValueRef address)
    {
        var load = _b.Load(RawVector, address);
        Native.LLVMSetAlignment(load, 1);
        return load;
    }

    private LLVMValueRef Offset(LLVMValueRef address, LLVMValueRef offset, SimdElement e) =>
        _b.Gep(e.Llvm, address, [offset.Type.IntWidth < T.NativeInt.IntWidth ? _b.Cast(LLVMOpcode.ZExt, offset, T.NativeInt) : offset]);

    private LLVMValueRef? TrySimdProperty(IPropertyReferenceOperation p, IPropertySymbol property)
    {
        if (property.IsIndexer && IsVector128Struct(property.ContainingType) && p.Instance is not null)
            return EmitSimd(property.GetMethod!, property.ContainingType.TypeArguments[0], EmitValue(p.Instance), [EmitValue(p.Arguments[0].Value)], p);
        if (!property.IsStatic) return null;
        if (IsVector128Class(property.ContainingType) && property.Name == "IsHardwareAccelerated") return Vector128Accelerated ? L.True : L.False;
        var vector = SimdVectorOf(property.ContainingType, p.ConstrainedToType);
        if (vector is null) return null;
        if (property.Name == "IsHardwareAccelerated") return Vector128Accelerated ? L.True : L.False;
        var element = ElementOf(vector.TypeArguments[0], p);
        return property.Name switch
        {
            "Zero" => LlvmContext.Null(RawVector),
            "One" => Raw(Splat(element.IsFloat ? LlvmContext.ConstReal(element.Llvm, 1) : L.ConstInt(element.Llvm, 1), element)),
            "AllBitsSet" => ConstAllOnes(),
            "Count" or "ElementCount" => L.ConstInt(L.I32, element.Count),
            "Indices" => Raw(Native.LLVMConstVector([.. Enumerable.Range(0, element.Count).Select(i => element.IsFloat ? LlvmContext.ConstReal(element.Llvm, i) : L.ConstInt(element.Llvm, i))], (uint)element.Count)),
            "IsSupported" => L.True,
            _ => null,
        };
    }

    /// <summary>Vector128&lt;T&gt; itself, or the one a constrained call on the internal ISimdVector&lt;TSelf, T&gt; (the framework's generic span code) names.</summary>
    private INamedTypeSymbol? SimdVectorOf(INamedTypeSymbol owner, ITypeSymbol? constrainedTo)
    {
        if (IsVector128Struct(owner)) return owner;
        if (owner is { TypeKind: TypeKind.Interface, Name: "ISimdVector" } && constrainedTo is not null && S(constrainedTo) is INamedTypeSymbol concrete && IsVector128Struct(concrete)) return concrete;
        return null;
    }

    private LLVMValueRef ConstAllOnes() => Native.LLVMConstAllOnes(RawVector);

    /// <summary>
    /// <c>new Vector&lt;T&gt;(T value)</c> (every lane the value) and <c>new Vector&lt;T&gt;(ReadOnlySpan&lt;T&gt; / Span&lt;T&gt; values)</c> (the first 16 bytes; a span
    /// that is too short throws ArgumentOutOfRangeException, as .NET's). Other forms (arrays, bytes) are an explicit diagnostic.
    /// </summary>
    private LLVMValueRef? TrySimdCreation(IObjectCreationOperation creation, INamedTypeSymbol type)
    {
        if (!IsVector128Struct(type) || creation.Constructor is not { Parameters.Length: 1 } constructor) return null;
        var parameter = S(constructor.Parameters[0].Type);
        var element = ElementOf(type.TypeArguments[0], creation);
        if (SymbolEqualityComparer.Default.Equals(parameter, element.Type))
            return Raw(Splat(EmitArgumentValue(creation.Arguments[0]), element));
        if (parameter is INamedTypeSymbol { Name: "ReadOnlySpan" or "Span", TypeArguments: [var spanElement] } && TypeMapper.IsSpan(parameter))
        {
            var span = EmitArgumentValue(creation.Arguments[0]);
            var perVector = 16 / (int)T.Target.SizeOf(T.Storage(S(spanElement)));
            var tooShort = NewBlock("simd.span.short");
            var ok = NewBlock("simd.span.ok");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.SLT, SpanLength(span), L.ConstInt(L.I32, perVector)), tooShort, ok);
            _b.PositionAtEnd(tooShort);
            ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.ArgumentOutOfRangeException")!, "Specified argument was out of the range of valid values. (Parameter 'values')");
            _b.PositionAtEnd(ok);
            return Load128(SpanPointer(span));
        }
        return null;
    }

    private LLVMValueRef? TrySimdCall(IInvocationOperation inv, IMethodSymbol target)
    {
        var owner = target.ContainingType;
        var constrained = SimdVectorOf(owner, inv.ConstrainedToType);
        if (!IsVector128Class(owner) && constrained is null) return null;
        var arguments = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(a => EmitArgument(a, target)).ToArray();
        // The element type: the vector the operation produces or takes first, else its type argument.
        ITypeSymbol? elementType;
        if (constrained is not null) elementType = constrained.TypeArguments[0];
        else if (IsVector128Struct(target.ReturnType)) elementType = ((INamedTypeSymbol)target.ReturnType).TypeArguments[0];
        else
            elementType = target.Parameters.Select(p => p.Type).OfType<INamedTypeSymbol>().FirstOrDefault(IsVector128Struct)?.TypeArguments[0]
                ?? (target.IsGenericMethod ? target.TypeArguments[0] : null);
        var instance = inv.Instance is null ? default : EmitValue(inv.Instance);
        return EmitSimd(target, elementType, instance, arguments, inv);
    }

    private LLVMValueRef EmitSimd(IMethodSymbol target, ITypeSymbol? elementType, LLVMValueRef instance, LLVMValueRef[] a, IOperation at)
    {
        var name = target.Name;
        // Members that name no element type: conversions between element views are the identity on the raw bytes.
        if (name is "As" or "AsByte" or "AsSByte" or "AsUInt16" or "AsInt16" or "AsUInt32" or "AsInt32" or "AsUInt64" or "AsInt64" or "AsSingle" or "AsDouble" or "AsNInt" or "AsNUInt"
            || name.StartsWith("AsVector", StringComparison.Ordinal)) // Vector.AsVectorInt32 ...
            return a[0];
        if (elementType is null) throw CompileError.Unsupported($"'{target}'", at);
        var e = ElementOf(elementType, at);
        LLVMValueRef Zero() => LlvmContext.Null(RawVector);
        LLVMValueRef Ints(Func<LLVMValueRef, LLVMValueRef, LLVMValueRef> op, int x, int y) => Raw(op(View(a[x], e), View(a[y], e)));
        LLVMValueRef Arith(LLVMOpcode integer, LLVMOpcode floating) => Ints((x, y) => _b.BinOp(e.IsFloat ? floating : integer, x, y), 0, 1);
        LLVMValueRef Shift(LLVMOpcode opcode, LLVMValueRef count)
        {
            // The count is taken modulo the element width, as the hardware shifts and .NET's do.
            var masked = _b.BinOp(LLVMOpcode.And, count.Type.IntWidth == 32 ? count : _b.Cast(LLVMOpcode.Trunc, count, L.I32), L.ConstInt(L.I32, e.Bits - 1));
            var lane = e.Bits == 64 ? _b.Cast(LLVMOpcode.ZExt, masked, L.I64) : e.Bits == 32 ? masked : _b.Cast(LLVMOpcode.Trunc, masked, e.Llvm);
            return Raw(_b.BinOp(opcode, View(a[0], e), Splat(lane, e)));
        }
        LLVMOpcode RightShift() => e.IsSigned && !e.IsFloat ? LLVMOpcode.AShr : LLVMOpcode.LShr;

        switch (name)
        {
            case "get_Zero": return Zero();
            case "get_AllBitsSet": return ConstAllOnes();
            case "get_Count" or "get_ElementCount": return L.ConstInt(L.I32, e.Count);
            case "Create" when a.Length == 1 && a[0].Type.Kind == LLVMTypeKind.Struct: // Create(ReadOnlySpan<T>): the first vector's worth
            {
                var tooShort = NewBlock("simd.span.short");
                var ok = NewBlock("simd.span.ok");
                _b.CondBr(_b.ICmp(LLVMIntPredicate.SLT, SpanLength(a[0]), L.ConstInt(L.I32, e.Count)), tooShort, ok);
                _b.PositionAtEnd(tooShort);
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.ArgumentOutOfRangeException")!, "Specified argument was out of the range of valid values. (Parameter 'values')");
                _b.PositionAtEnd(ok);
                return Load128(SpanPointer(a[0]));
            }
            case "Widen" when a.Length == 3: // Widen(source, out low, out high): the lower and upper half, each lane twice as wide
            {
                var wide = ElementOf(((INamedTypeSymbol)target.Parameters[1].Type).TypeArguments[0], at);
                var source = View(a[0], e);
                var half = e.Count / 2;
                LLVMValueRef Half(int start)
                {
                    var mask = Native.LLVMConstVector([.. Enumerable.Range(start, half).Select(i => L.ConstInt(L.I32, i))], (uint)half);
                    var lanes = _b.ShuffleVector(source, Native.LLVMGetPoison(VectorOf(e)), mask);
                    var to = Native.LLVMVectorType(wide.Llvm, (uint)half);
                    return Raw(_b.Cast(e.IsFloat ? LLVMOpcode.FPExt : e.IsSigned ? LLVMOpcode.SExt : LLVMOpcode.ZExt, lanes, to));
                }
                foreach (var (address, value) in new[] { (a[1], Half(0)), (a[2], Half(half)) })
                    Native.LLVMSetAlignment(_b.Store(value, address), 1);
                return default;
            }
            case "Create" or "CopyTo" when target.Parameters.Any(p => p.Type is IArrayTypeSymbol):
            {
                // Create(T[] values[, int index]) and CopyTo(T[] destination[, int startIndex]), with .NET's checks and messages.
                var create = name == "Create";
                var arrayAt = target.Parameters.ToList().FindIndex(p => p.Type is IArrayTypeSymbol);
                var array = a[arrayAt];
                var vector = create ? default : target.IsStatic ? a[0] : instance;
                NullCheck(array);
                var length = LoadArrayLength(array);
                var index = a.Length > arrayAt + 1 ? _b.Cast(LLVMOpcode.SExt, a[arrayAt + 1], L.I64) : L.ConstInt(L.I64, 0);
                var hasIndex = a.Length > arrayAt + 1;
                var count = L.ConstInt(L.I64, e.Count);
                void Throw(LLVMValueRef bad, string type, string message)
                {
                    var fail = NewBlock("simd.array.bad");
                    var ok = NewBlock("simd.array.ok");
                    _b.CondBr(bad, fail, ok);
                    _b.PositionAtEnd(fail);
                    ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName(type)!, message);
                    _b.PositionAtEnd(ok);
                }
                var room = _b.BinOp(LLVMOpcode.Sub, length, index);
                if (create)
                    Throw(hasIndex ? _b.BinOp(LLVMOpcode.Or, _b.ICmp(LLVMIntPredicate.SLT, index, L.ConstInt(L.I64, 0)), _b.ICmp(LLVMIntPredicate.SLT, room, count))
                        : _b.ICmp(LLVMIntPredicate.SLT, length, count),
                        "System.ArgumentOutOfRangeException", "Index was out of range. Must be non-negative and less than or equal to the size of the collection. (Parameter 'index')");
                else
                {
                    if (hasIndex)
                        Throw(_b.ICmp(LLVMIntPredicate.UGE, index, length), "System.ArgumentOutOfRangeException",
                            "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'startIndex')");
                    Throw(_b.ICmp(LLVMIntPredicate.SLT, room, count), "System.ArgumentException", "Destination is too short. (Parameter 'destination')");
                }
                var start = Offset(_b.StructGep(T.ArrayHeader(L.I8), array, 2), index, e);
                if (create) return Load128(start);
                Native.LLVMSetAlignment(_b.Store(vector, start), 1);
                return default;
            }
            case "Sum" when a.Length == 1:
            {
                // Lanes added pairwise ((a0 + a1) + (a2 + a3)), as the hardware's horizontal add does: the order is only visible for floats.
                var lanes = Enumerable.Range(0, e.Count).Select(i => _b.ExtractElement(View(a[0], e), L.ConstInt(L.I32, i))).ToList();
                while (lanes.Count > 1)
                    lanes = [.. Enumerable.Range(0, lanes.Count / 2).Select(i => _b.BinOp(e.IsFloat ? LLVMOpcode.FAdd : LLVMOpcode.Add, lanes[2 * i], lanes[2 * i + 1]))];
                return lanes[0];
            }
            case "CopyTo" when a.Length == (target.IsStatic ? 2 : 1) && a[^1].Type.Kind == LLVMTypeKind.Struct: // CopyTo(Span<T> destination), as an instance (Vector<T>) or an extension (Vector128)
            {
                var (vectorValue, destination) = target.IsStatic ? (a[0], a[1]) : (instance, a[0]);
                var tooShort = NewBlock("simd.copy.short");
                var ok = NewBlock("simd.copy.ok");
                _b.CondBr(_b.ICmp(LLVMIntPredicate.SLT, SpanLength(destination), L.ConstInt(L.I32, e.Count)), tooShort, ok);
                _b.PositionAtEnd(tooShort);
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.ArgumentException")!, "Destination is too short. (Parameter 'destination')");
                _b.PositionAtEnd(ok);
                Native.LLVMSetAlignment(_b.Store(vectorValue, SpanPointer(destination)), 1);
                return default;
            }
            case "Create" or "CreateScalarUnsafe" or "CreateScalar" when a.Length == 1 && a[0].Type == e.Llvm:
                if (name == "Create") return Raw(Splat(a[0], e));
                return Raw(_b.InsertElement(name == "CreateScalar" ? LlvmContext.Null(VectorOf(e)) : Native.LLVMGetPoison(VectorOf(e)), a[0], L.ConstInt(L.I32, 0)));
            case "Create" when a.Length == e.Count && a.All(x => x.Type == e.Llvm):
            {
                var v = LlvmContext.Null(VectorOf(e));
                for (var i = 0; i < a.Length; i++) v = _b.InsertElement(v, a[i], L.ConstInt(L.I32, i));
                return Raw(v);
            }
            case "LoadUnsafe" or "Load" or "LoadAligned" or "LoadAlignedNonTemporal":
                return Load128(a.Length == 2 ? Offset(a[0], a[1], e) : a[0]);
            case "StoreUnsafe" or "Store" or "StoreAligned" or "StoreAlignedNonTemporal":
            {
                var store = _b.Store(a[0], a.Length == 3 ? Offset(a[1], a[2], e) : a[1]);
                Native.LLVMSetAlignment(store, 1);
                return default;
            }
            case "Equals" when a.Length == 2: return Compare(e, "eq", a[0], a[1]);
            case "op_Equality": return AllSet(Compare(e, "eq", a[0], a[1]), e);
            case "op_Inequality": return _b.Not(AllSet(Compare(e, "eq", a[0], a[1]), e));
            case "EqualsAll": return AllSet(Compare(e, "eq", a[0], a[1]), e);
            case "EqualsAny": return AnySet(Compare(e, "eq", a[0], a[1]), e);
            case "GreaterThan": return Compare(e, "gt", a[0], a[1]);
            case "GreaterThanOrEqual": return Compare(e, "ge", a[0], a[1]);
            case "LessThan": return Compare(e, "lt", a[0], a[1]);
            case "LessThanOrEqual": return Compare(e, "le", a[0], a[1]);
            case "GreaterThanAll": return AllSet(Compare(e, "gt", a[0], a[1]), e);
            case "GreaterThanAny": return AnySet(Compare(e, "gt", a[0], a[1]), e);
            case "LessThanAll": return AllSet(Compare(e, "lt", a[0], a[1]), e);
            case "LessThanAny": return AnySet(Compare(e, "lt", a[0], a[1]), e);
            case "BitwiseAnd" or "op_BitwiseAnd": return _b.BinOp(LLVMOpcode.And, a[0], a[1]);
            case "BitwiseOr" or "op_BitwiseOr": return _b.BinOp(LLVMOpcode.Or, a[0], a[1]);
            case "Xor" or "op_ExclusiveOr": return _b.BinOp(LLVMOpcode.Xor, a[0], a[1]);
            case "AndNot": return _b.BinOp(LLVMOpcode.And, a[0], _b.Not(a[1]));
            case "OnesComplement" or "op_OnesComplement": return _b.Not(a[0]);
            case "Add" or "op_Addition": return Arith(LLVMOpcode.Add, LLVMOpcode.FAdd);
            case "Subtract" or "op_Subtraction": return Arith(LLVMOpcode.Sub, LLVMOpcode.FSub);
            case "Multiply" or "op_Multiply" when a[0].Type == a[1].Type: return Arith(LLVMOpcode.Mul, LLVMOpcode.FMul);
            case "Multiply" or "op_Multiply":
                return a[0].Type == RawVector ? Raw(_b.BinOp(e.IsFloat ? LLVMOpcode.FMul : LLVMOpcode.Mul, View(a[0], e), Splat(a[1], e)))
                    : Raw(_b.BinOp(e.IsFloat ? LLVMOpcode.FMul : LLVMOpcode.Mul, Splat(a[0], e), View(a[1], e)));
            case "Divide" or "op_Division" when e.IsFloat && a[0].Type == a[1].Type: return Arith(LLVMOpcode.SDiv, LLVMOpcode.FDiv);
            case "Negate" or "op_UnaryNegation":
                return Raw(e.IsFloat ? _b.FNeg(View(a[0], e)) : _b.Neg(View(a[0], e)));
            case "op_UnaryPlus": return a[0];
            case "Min" or "Max" when !e.IsFloat:
            {
                var min = name == "Min";
                var pick = Compare(e, min ? "lt" : "gt", a[0], a[1]);
                return Select(pick, a[0], a[1]);
            }
            case "ConditionalSelect": return _b.BinOp(LLVMOpcode.Or, _b.BinOp(LLVMOpcode.And, a[1], a[0]), _b.BinOp(LLVMOpcode.And, a[2], _b.Not(a[0])));
            case "ExtractMostSignificantBits":
                return _b.Cast(LLVMOpcode.ZExt, MaskBits(a[0], e), L.I32);
            // ISimdVector's helpers over a comparison mask (every lane all ones or zero), which the generic span code uses:
            case "AnyWhereAllBitsSet": return AnySet(a[0], e);
            case "AllWhereAllBitsSet": return AllSet(a[0], e);
            case "NoneWhereAllBitsSet": return _b.Not(AnySet(a[0], e));
            case "CountWhereAllBitsSet": return BitCount("ctpop", MaskBits(a[0], e), false);
            case "IndexOfWhereAllBitsSet":
            {
                var bits = MaskBits(a[0], e);
                var none = _b.ICmp(LLVMIntPredicate.EQ, bits, LlvmContext.Null(bits.Type));
                return _b.Select(none, L.ConstInt(L.I32, -1), BitCount("cttz", bits, true));
            }
            case "LastIndexOfWhereAllBitsSet":
            {
                var bits = MaskBits(a[0], e);
                var none = _b.ICmp(LLVMIntPredicate.EQ, bits, LlvmContext.Null(bits.Type));
                return _b.Select(none, L.ConstInt(L.I32, -1), _b.BinOp(LLVMOpcode.Sub, L.ConstInt(L.I32, e.Count - 1), BitCount("ctlz", bits, true)));
            }
            case "ShiftLeft" or "op_LeftShift": return Shift(LLVMOpcode.Shl, a[1]);
            case "ShiftRightLogical" or "op_UnsignedRightShift": return Shift(LLVMOpcode.LShr, a[1]);
            case "ShiftRightArithmetic": return Shift(LLVMOpcode.AShr, a[1]);
            case "op_RightShift": return Shift(RightShift(), a[1]);
            case "GetElement" or "get_Item":
            {
                var (vector, index) = name == "get_Item" ? (instance, a[0]) : (a[0], a[1]);
                CheckLane(index, e);
                return _b.ExtractElement(View(vector, e), index);
            }
            case "WithElement":
                CheckLane(a[1], e);
                return Raw(_b.InsertElement(View(a[0], e), a[2], a[1]));
            case "Shuffle" or "ShuffleNative":
            {
                // lane i = vector[indices[i]], or zero when the index is outside 0..Count-1 (negative included), for constant and run-time indices:
                // LLVM's shufflevector needs a constant mask, so each lane is an extractelement by the index; with constant indices that folds to a shuffle.
                var source = View(a[0], e);
                var indices = IntView(a[1], e);
                var lane = e.IsFloat ? LlvmContext.Null(e.Llvm) : L.ConstInt(e.Llvm, 0);
                var result = LlvmContext.Null(VectorOf(e));
                for (var i = 0; i < e.Count; i++)
                {
                    var index = _b.ExtractElement(indices, L.ConstInt(L.I32, i));
                    var inRange = _b.ICmp(LLVMIntPredicate.ULT, index, L.ConstInt(index.Type, e.Count));
                    var safe = _b.Select(inRange, index, L.ConstInt(index.Type, 0));
                    var picked = _b.ExtractElement(source, safe);
                    result = _b.InsertElement(result, _b.Select(inRange, picked, lane), L.ConstInt(L.I32, i));
                }
                return Raw(result);
            }
            case "ToScalar": return _b.ExtractElement(View(a[0], e), L.ConstInt(L.I32, 0));
            case "GetElementUnsafe": return _b.ExtractElement(View(_b.Load(RawVector, a[0]), e), a[1]);
            default:
                throw CompileError.Unsupported($"'{target}' (Vector128 is lowered for the operations the framework's span helpers use)", at);
        }
    }

    /// <summary>llvm.ctpop / cttz / ctlz of a lane bitmask, as an int.</summary>
    private LLVMValueRef BitCount(string name, LLVMValueRef bits, bool zeroIsPoison)
    {
        var f = zeroIsPoison ? _m.Intrinsic($"llvm.{name}.i{bits.Type.IntWidth}", bits.Type, bits.Type, L.I1) : _m.Intrinsic($"llvm.{name}.i{bits.Type.IntWidth}", bits.Type, bits.Type);
        var r = zeroIsPoison ? _b.Call(f.Type, f.Fn, [bits, L.True]) : _b.Call(f.Type, f.Fn, [bits]);
        return _b.Cast(bits.Type.IntWidth < 32 ? LLVMOpcode.ZExt : LLVMOpcode.Trunc, r, L.I32);
    }

    private LLVMValueRef Select(LLVMValueRef rawMask, LLVMValueRef rawA, LLVMValueRef rawB) =>
        _b.BinOp(LLVMOpcode.Or, _b.BinOp(LLVMOpcode.And, rawA, rawMask), _b.BinOp(LLVMOpcode.And, rawB, _b.Not(rawMask)));

    /// <summary>GetElement / WithElement throw ArgumentOutOfRangeException for an index outside the vector, as .NET's.</summary>
    private void CheckLane(LLVMValueRef index, SimdElement e)
    {
        var bad = NewBlock("simd.lane.bad");
        var ok = NewBlock("simd.lane.ok");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.ULT, index, L.ConstInt(index.Type, e.Count)), ok, bad);
        _b.PositionAtEnd(bad);
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.ArgumentOutOfRangeException")!, "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')");
        _b.PositionAtEnd(ok);
    }
}
