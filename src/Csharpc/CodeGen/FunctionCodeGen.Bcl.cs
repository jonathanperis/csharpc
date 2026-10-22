using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Lowering support for BCL value types and reflection-lite (see BclValueTypes.cs):
/// System.Decimal ({ i32 flags, i32 hi, i64 lo }, arithmetic in src/runtime/decimal.c),
/// TimeSpan/DateTime (an i64 tick count), System.Type objects (one per method table).
/// </summary>
public sealed partial class FunctionCodeGen
{
    internal LlvmContext Llvm => L;

    /// <summary>Parameter i of the function being generated (for synthesized helpers).</summary>
    internal LLVMValueRef ParamOf(uint index) => _fn.Fn.Param(index);

    /// <summary>Emits the body of a compiler-synthesized function (no Roslyn method behind it).</summary>
    internal void EmitSynthetic(Action body)
    {
        body();
        Finish();
    }
    internal TypeMapper Types => T;

    // ======================================================================================
    // Calls into library members implemented by BclValueTypes
    // ======================================================================================

    /// <summary>Evaluates receiver + arguments (by parameter ordinal; by-ref as addresses) and runs the handler.</summary>
    internal LLVMValueRef CallBcl(BclHandler handler, IMethodSymbol method, IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments, IOperation at)
    {
        var self = !method.IsStatic && instance is not null ? EmitValue(instance) : default;
        var args = new LLVMValueRef[method.Parameters.Length];
        foreach (var argument in arguments) args[argument.Parameter!.Ordinal] = EmitArgument(argument);
        return handler(this, method, self, args, at);
    }

    /// <summary>A library operator / conversion method (op_Addition etc.) applied to operand expressions.</summary>
    internal LLVMValueRef LibraryOperator(IMethodSymbol method, IOperation[] operands, IOperation at)
    {
        // Generic math (`a + b` with T = decimal) binds to INumber<T>'s static abstract operator: use the
        // concrete type's own operator after substitution.
        if (method.IsStatic && method.ContainingType.TypeKind == TypeKind.Interface && operands.Length > 0
            && TypeOf(operands[0]) is INamedTypeSymbol concrete && concrete.TypeKind != TypeKind.Interface)
        {
            var parameterTypes = method.Parameters.Select(p => S(p.Type)).ToArray();
            method = concrete.GetMembers(method.Name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic
                && m.Parameters.Select(p => p.Type).SequenceEqual(parameterTypes, SymbolEqualityComparer.Default)) ?? method;
        }
        if (!BclValueTypes.TryGet(method, out var handler))
            throw CompileError.Unsupported($"operator '{method}'", at);
        var args = operands.Select((o, i) => Coerce(EmitValue(o), TypeOf(o), S(method.Parameters[i].Type))).ToArray();
        return handler(this, method, default, args, at);
    }

    internal LLVMValueRef Spill(LLVMValueRef value, string name = "spill")
    {
        var temp = NewTemp(value.Type, name);
        _b.Store(value, temp);
        return temp;
    }

    // ======================================================================================
    // System.Decimal
    // ======================================================================================

    internal LLVMTypeRef DecimalType => L.Struct(L.I32, L.I32, L.I64);

    internal LLVMValueRef DecimalConstant(decimal value)
    {
        var bits = decimal.GetBits(value); // lo, mid, hi, flags
        var lo64 = (ulong)(uint)bits[0] | ((ulong)(uint)bits[1] << 32);
        return L.ConstStruct(L.ConstInt(L.I32, bits[3]), L.ConstInt(L.I32, bits[2]), L.ConstUInt(L.I64, lo64));
    }

    /// <summary>result = fn(&amp;result, &amp;a, &amp;b) style runtime call returning a decimal value.</summary>
    internal LLVMValueRef DecimalCall(string fn, LLVMTypeRef[] extraTypes, params LLVMValueRef[] operands)
    {
        var result = NewTemp(DecimalType, "dec");
        var args = new List<LLVMValueRef> { result };
        var types = new List<LLVMTypeRef> { L.Ptr };
        var extra = 0;
        foreach (var operand in operands)
        {
            if (operand.Type.Handle == DecimalType.Handle) { args.Add(Spill(operand, "dec.arg")); types.Add(L.Ptr); }
            else { args.Add(operand); types.Add(extraTypes[extra++]); }
        }
        CallRuntime(fn, L.Void, [.. types], [.. args]);
        return _b.Load(DecimalType, result);
    }

    internal LLVMValueRef DecimalCompare(LLVMValueRef a, LLVMValueRef b) =>
        CallRuntime("csrt_decimal_compare", L.I32, [L.Ptr, L.Ptr], Spill(a, "dec.a"), Spill(b, "dec.b"));

    internal LLVMValueRef DecimalArithmetic(BinaryOperatorKind kind, LLVMValueRef l, LLVMValueRef r, IOperation at)
    {
        string? fn = kind switch
        {
            BinaryOperatorKind.Add => "csrt_decimal_add",
            BinaryOperatorKind.Subtract => "csrt_decimal_sub",
            BinaryOperatorKind.Multiply => "csrt_decimal_mul",
            BinaryOperatorKind.Divide => "csrt_decimal_div",
            BinaryOperatorKind.Remainder => "csrt_decimal_rem",
            _ => null,
        };
        if (fn is not null) return DecimalCall(fn, [], l, r);
        var c = DecimalCompare(l, r);
        var zero = L.ConstInt(L.I32, 0);
        return kind switch
        {
            BinaryOperatorKind.Equals => _b.ICmp(LLVMIntPredicate.EQ, c, zero),
            BinaryOperatorKind.NotEquals => _b.ICmp(LLVMIntPredicate.NE, c, zero),
            BinaryOperatorKind.LessThan => _b.ICmp(LLVMIntPredicate.SLT, c, zero),
            BinaryOperatorKind.LessThanOrEqual => _b.ICmp(LLVMIntPredicate.SLE, c, zero),
            BinaryOperatorKind.GreaterThan => _b.ICmp(LLVMIntPredicate.SGT, c, zero),
            BinaryOperatorKind.GreaterThanOrEqual => _b.ICmp(LLVMIntPredicate.SGE, c, zero),
            _ => throw CompileError.Unsupported($"decimal operator {kind}", at),
        };
    }

    internal LLVMValueRef DecimalNegate(LLVMValueRef v) => DecimalCall("csrt_decimal_negate", [], v);

    internal LLVMValueRef DecimalOne => DecimalConstant(1m);

    /// <summary>Conversions to/from decimal (C#: int-&gt;decimal implicit; decimal-&gt;int always checked).</summary>
    internal LLVMValueRef DecimalConvert(LLVMValueRef value, ITypeSymbol from, ITypeSymbol to, IOperation? at)
    {
        if (BclValueTypes.IsDecimal(from) && BclValueTypes.IsDecimal(to)) return value;
        if (BclValueTypes.IsDecimal(to))
        {
            if (value.Type.IsFloatingPoint)
                return value.Type.Kind == LLVMTypeKind.Double
                    ? DecimalCall("csrt_decimal_from_f64", [L.F64], value)
                    : DecimalCall("csrt_decimal_from_f32", [L.F32], value);
            var wide = value.Type.IntWidth == 64 ? value
                : _b.Cast(TypeMapper.IsSigned(from) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, L.I64);
            return TypeMapper.IsSigned(from)
                ? DecimalCall("csrt_decimal_from_i64", [L.I64], wide)
                : DecimalCall("csrt_decimal_from_u64", [L.I64], wide);
        }
        var p = Spill(value, "dec");
        var dst = T.Value(to);
        if (dst.Kind == LLVMTypeKind.Double) return CallRuntime("csrt_decimal_to_f64", L.F64, [L.Ptr], p);
        if (dst.Kind == LLVMTypeKind.Float) return CallRuntime("csrt_decimal_to_f32", L.F32, [L.Ptr], p);
        var kind = to.SpecialType switch
        {
            SpecialType.System_SByte => 1, SpecialType.System_Byte => 2, SpecialType.System_Int16 => 3,
            SpecialType.System_UInt16 => 4, SpecialType.System_Int32 => 5, SpecialType.System_UInt32 => 6,
            SpecialType.System_Int64 => 7, SpecialType.System_UInt64 => 8, SpecialType.System_Char => 9,
            _ when to.TypeKind == TypeKind.Enum => (int?)null,
            _ => throw CompileError.Unsupported($"conversion from '{from}' to '{to}'", at),
        };
        if (kind is null) // decimal -> enum: via the underlying type
            return DecimalConvert(value, from, ((INamedTypeSymbol)to).EnumUnderlyingType!, at);
        var raw = CallRuntime("csrt_decimal_to_integer", L.I64, [L.Ptr, L.I32], p, L.ConstInt(L.I32, kind.Value));
        return dst.IntWidth == 64 ? raw : _b.Cast(LLVMOpcode.Trunc, raw, dst);
    }

    // ======================================================================================
    // System.Type
    // ======================================================================================

    /// <summary>The canonical Type object for a statically known type.</summary>
    internal LLVMValueRef TypeObject(ITypeSymbol type, IOperation at) =>
        CallRuntime("csrt_type_from_mt", L.Ptr, [L.Ptr], _m.TypeMethodTable(type, at));

    /// <summary>obj.GetType(): static for value types and sealed-by-construction cases, else from the object's MT.</summary>
    internal LLVMValueRef EmitGetType(IInvocationOperation inv)
    {
        var instance = inv.Instance!;
        while (instance is IConversionOperation { Conversion.IsImplicit: true } boxing && TypeOf(boxing.Operand) is { IsValueType: true })
            instance = boxing.Operand;
        if (TypeOf(instance) is { IsValueType: true } valueType)
        {
            if (TypeMapper.IsNullable(valueType))
                throw CompileError.Unsupported("GetType() on a nullable value type", inv);
            EmitValue(instance); // keep side effects
            return TypeObject(valueType, inv);
        }
        return CallRuntime("csrt_type_of_object", L.Ptr, [L.Ptr], EmitValue(inv.Instance!));
    }

    /// <summary>Gives a freshly allocated array its element-specific method table ("System.Int32[]").</summary>
    internal void TagArray(LLVMValueRef array, ITypeSymbol elementType) =>
        _b.Store(_m.ArrayMethodTable(elementType), array);

    // ======================================================================================
    // Small IR building blocks for library helpers
    // ======================================================================================

    /// <summary>for (i = 0; i &lt; count; i++) body(i) — count is i64.</summary>
    internal void EmitCountedLoop(LLVMValueRef count, Action<LLVMValueRef> body)
    {
        var index = NewTemp(L.I64, "i");
        _b.Store(L.ConstInt(L.I64, 0), index);
        var cond = NewBlock("loop.cond");
        var loop = NewBlock("loop.body");
        var done = NewBlock("loop.done");
        _b.Br(cond);
        _b.PositionAtEnd(cond);
        var i = _b.Load(L.I64, index);
        _b.CondBr(_b.ICmp(LLVMIntPredicate.SLT, i, count), loop, done);
        _b.PositionAtEnd(loop);
        body(i);
        _b.Store(_b.BinOp(LLVMOpcode.Add, _b.Load(L.I64, index), L.ConstInt(L.I64, 1)), index);
        _b.Br(cond);
        _b.PositionAtEnd(done);
    }

    internal LLVMValueRef ArrayElementAddress(LLVMValueRef array, ITypeSymbol elementType, LLVMValueRef index) =>
        _b.InBoundsGep(T.ArrayHeader(T.Storage(elementType)), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), index]);

    internal LLVMValueRef NewTypedArray(ITypeSymbol elementType, LLVMValueRef length)
    {
        var array = NewArray(elementType, length);
        return array;
    }

    /// <summary>Calls a delegate value: fn(target, args...).</summary>
    internal LLVMValueRef InvokeDelegate(LLVMValueRef d, INamedTypeSymbol delegateType, params LLVMValueRef[] args)
    {
        var invoke = delegateType.DelegateInvokeMethod!;
        NullCheck(d);
        var target = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 1), "target");
        var fn = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 2), "fn");
        List<LLVMTypeRef> parameters = [L.Ptr];
        foreach (var p in invoke.Parameters) parameters.Add(p.RefKind == RefKind.None ? T.Value(p.Type) : L.Ptr);
        var fnType = L.FunctionType(T.Value(invoke.ReturnType), [.. parameters]);
        return _b.Call(fnType, fn, [target, .. args]);
    }
}
