using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>ValueTuple members (Equals/CompareTo/GetHashCode/ToString) and boxed tuples.</summary>
public sealed partial class FunctionCodeGen
{
    private LLVMValueRef? TryEmitTupleMethod(IInvocationOperation inv, IMethodSymbol target)
    {
        if (target.ContainingType is not INamedTypeSymbol { IsTupleType: true } tuple || target.IsStatic) return null;
        tuple = (INamedTypeSymbol)S(tuple);
        var self = EmitValue(inv.Instance!);
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        switch (target.Name, args.Length)
        {
            case ("Equals", 1) when args[0].Parameter!.Type.IsTupleType():
                return TupleEquals(self, Coerce(EmitValue(args[0].Value), TypeOf(args[0].Value), tuple), tuple);
            case ("Equals", 1):
            {
                var other = EmitValue(args[0].Value);
                var mt = _m.TupleMethodTable(tuple, this);
                return ShortCircuit(isAnd: true,
                [
                    () => _b.ICmp(LLVMIntPredicate.NE, CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], other, mt), LlvmContext.Null(L.Ptr)),
                    () => TupleEquals(self, LoadValue(_b.InBoundsGep(L.I8, other, [L.ConstInt(L.I64, 8)]), tuple), tuple),
                ]);
            }
            case ("CompareTo", 1) when args[0].Parameter!.Type.IsTupleType():
                return TupleCompare(self, Coerce(EmitValue(args[0].Value), TypeOf(args[0].Value), tuple), tuple, inv);
            case ("GetHashCode", 0):
                return TupleHash(self, tuple);
            case ("ToString", 0):
                return FormatValue(self, tuple, inv);
        }
        return null;
    }

    internal LLVMValueRef TupleEquals(LLVMValueRef a, LLVMValueRef b, INamedTypeSymbol tuple)
    {
        // A boxed Memory<T> / ReadOnlyMemory<T> ({ ptr, length }): .NET compares the owner, start index and
        // length, which for csharpc's interior pointers is the pointer and the length.
        if (TypeMapper.IsSpan(tuple))
            return _b.BinOp(LLVMOpcode.And, _b.ICmp(LLVMIntPredicate.EQ, _b.ExtractValue(a, 0), _b.ExtractValue(b, 0)),
                _b.ICmp(LLVMIntPredicate.EQ, _b.ExtractValue(a, 1), _b.ExtractValue(b, 1)));
        List<Func<LLVMValueRef>> conditions = [];
        var elements = tuple.TupleElements;
        for (var i = 0; i < elements.Length; i++)
        {
            var index = i;
            var type = S(elements[i].Type);
            conditions.Add(() => FieldEquals(TupleElement(a, tuple, index), TupleElement(b, tuple, index), type));
        }
        return conditions.Count == 0 ? L.True : ShortCircuit(isAnd: true, conditions);
    }

    internal LLVMValueRef TupleHash(LLVMValueRef value, INamedTypeSymbol tuple)
    {
        LLVMValueRef hash = L.ConstInt(L.I32, 17);
        if (TypeMapper.IsSpan(tuple))
        {
            // .NET hashes the owner's identity (an address-based value): not reproducible either way.
            var address = _b.Cast(LLVMOpcode.PtrToInt, _b.ExtractValue(value, 0), L.I64);
            var folded = _b.Cast(LLVMOpcode.Trunc, _b.BinOp(LLVMOpcode.Xor, address, _b.BinOp(LLVMOpcode.LShr, address, L.ConstInt(L.I64, 32))), L.I32);
            return _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Mul, folded, L.ConstInt(L.I32, 31)), _b.ExtractValue(value, 1));
        }
        var elements = tuple.TupleElements;
        for (var i = 0; i < elements.Length; i++)
            hash = _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Mul, hash, L.ConstInt(L.I32, 31)), FieldHash(TupleElement(value, tuple, i), S(elements[i].Type)));
        return hash;
    }

    /// <summary>Lexicographic CompareTo with Comparer&lt;T&gt;.Default per element (numbers, chars, bools, strings).</summary>
    private LLVMValueRef TupleCompare(LLVMValueRef a, LLVMValueRef b, INamedTypeSymbol tuple, IOperation at)
    {
        var result = NewTemp(L.I32, "tuple.cmp");
        _b.Store(L.ConstInt(L.I32, 0), result);
        var done = NewBlock("tuple.cmp.done");
        var elements = tuple.TupleElements;
        for (var i = 0; i < elements.Length; i++)
        {
            var type = S(elements[i].Type);
            var c = CompareValues(TupleElement(a, tuple, i), TupleElement(b, tuple, i), type, at);
            _b.Store(c, result);
            var next = NewBlock("tuple.cmp.next");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.NE, c, L.ConstInt(L.I32, 0)), done, next);
            _b.PositionAtEnd(next);
        }
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(L.I32, result);
    }

    private LLVMValueRef CompareValues(LLVMValueRef x, LLVMValueRef y, ITypeSymbol type, IOperation at)
    {
        if (type.SpecialType == SpecialType.System_String)
            return CallRuntime("csrt_string_compare_culture", L.I32, [L.Ptr, L.Ptr, L.I1], x, y, L.False);
        if (x.Type.IsInteger || x.Type.IsFloatingPoint)
        {
            LLVMValueRef lt, gt;
            if (x.Type.IsFloatingPoint)
            {
                // .NET ordering: NaN sorts before everything (and equals NaN).
                var xNaN = _b.FCmp(LLVMRealPredicate.UNO, x, x);
                var yNaN = _b.FCmp(LLVMRealPredicate.UNO, y, y);
                lt = _b.BinOp(LLVMOpcode.Or, _b.FCmp(LLVMRealPredicate.OLT, x, y), _b.BinOp(LLVMOpcode.And, xNaN, _b.Not(yNaN)));
                gt = _b.BinOp(LLVMOpcode.Or, _b.FCmp(LLVMRealPredicate.OGT, x, y), _b.BinOp(LLVMOpcode.And, yNaN, _b.Not(xNaN)));
            }
            else
            {
                var signed = TypeMapper.IsSigned(type);
                lt = _b.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, x, y);
                gt = _b.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, x, y);
            }
            return _b.BinOp(LLVMOpcode.Sub, _b.Cast(LLVMOpcode.ZExt, gt, L.I32), _b.Cast(LLVMOpcode.ZExt, lt, L.I32));
        }
        throw CompileError.Unsupported($"tuple CompareTo over '{type}'", at);
    }

    // ---- thunks for boxed tuples (vtable slots of the tuple's method table) ----

    internal void EmitTupleBoxToString(INamedTypeSymbol tuple)
    {
        var value = LoadValue(_b.InBoundsGep(L.I8, _fn.Fn.Param(0), [L.ConstInt(L.I64, 8)]), tuple);
        _b.Ret(FormatValue(value, tuple, null!));
        Finish();
    }

    internal void EmitTupleBoxEquals(INamedTypeSymbol tuple, LLVMValueRef mt)
    {
        var (self, other) = (_fn.Fn.Param(0), _fn.Fn.Param(1));
        var value = LoadValue(_b.InBoundsGep(L.I8, self, [L.ConstInt(L.I64, 8)]), tuple);
        _b.Ret(ShortCircuit(isAnd: true,
        [
            () => _b.ICmp(LLVMIntPredicate.NE, other, LlvmContext.Null(L.Ptr)),
            () => _b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, other), mt),
            () => TupleEquals(value, LoadValue(_b.InBoundsGep(L.I8, other, [L.ConstInt(L.I64, 8)]), tuple), tuple),
        ]));
        Finish();
    }

    internal void EmitTupleBoxHash(INamedTypeSymbol tuple)
    {
        var value = LoadValue(_b.InBoundsGep(L.I8, _fn.Fn.Param(0), [L.ConstInt(L.I64, 8)]), tuple);
        _b.Ret(TupleHash(value, tuple));
        Finish();
    }
}

public sealed partial class FunctionCodeGen
{
    private static bool IsStringBuilderHandler(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "AppendInterpolatedStringHandler", ContainingType.Name: "StringBuilder" };

    /// <summary>
    /// sb.Append($"..."): Roslyn's CFG expands the AppendInterpolatedStringHandler: create(…, sb),
    /// handler.AppendLiteral/AppendFormatted(…) per part, then sb.Append(ref handler). As in .NET the
    /// handler appends straight into the builder, so it is represented by the builder pointer itself.
    /// </summary>
    private LLVMValueRef? TryEmitStringBuilderInterpolation(IInvocationOperation inv, IMethodSymbol target)
    {
        // DefaultInterpolatedStringHandler (string.Create(provider, ...), FormattableString, interpolation in
        // IL) is the corelib shadow InterpolatedStringHandler.cs, which carries the format provider.
        if (IsStringBuilderHandler(target.ContainingType) && !target.IsStatic)
        {
            var sb = EmitValue(inv.Instance!);
            var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
            LLVMValueRef piece;
            if (target.Name == "AppendLiteral") piece = EmitValue(args[0].Value);
            else if (target.Name == "AppendFormatted")
            {
                var format = args.FirstOrDefault(a => a.Parameter!.Name == "format" && a.ArgumentKind != ArgumentKind.DefaultValue);
                var alignment = args.FirstOrDefault(a => a.Parameter!.Name == "alignment" && a.ArgumentKind != ArgumentKind.DefaultValue);
                piece = format is not null ? FormatWith(args[0].Value, EmitValue(format.Value)) : ToStringValue(args[0].Value);
                if (alignment is not null)
                    piece = CallRuntime("csrt_string_align", L.Ptr, [L.Ptr, L.I32], piece,
                        Coerce(EmitValue(alignment.Value), TypeOf(alignment.Value), _m.Compilation.GetSpecialType(SpecialType.System_Int32)));
            }
            else throw CompileError.Unsupported($"AppendInterpolatedStringHandler.{target.Name}", inv);
            CallRuntime("csrt_sb_append_string", L.Ptr, [L.Ptr, L.Ptr], sb, piece);
            return default(LLVMValueRef); // handled (void); a bare `default` would be null = "not handled"
        }
        if (target.ContainingType is { Name: "StringBuilder", ContainingNamespace.Name: "Text" } && target.Name is "Append" or "AppendLine"
            && target.Parameters.LastOrDefault()?.Type is { } last && IsStringBuilderHandler(last))
        {
            foreach (var a in inv.Arguments) EmitValue(a.Value);   // the handler already appended everything
            var self = EmitValue(inv.Instance!);
            return target.Name == "AppendLine"
                ? CallRuntime("csrt_sb_append_string", L.Ptr, [L.Ptr, L.Ptr], self, _m.StringLiteral(_m.NewLine))
                : self;
        }
        return null;
    }
}

internal static class TupleTypeExtensions
{
    public static bool IsTupleType(this ITypeSymbol type) => type is INamedTypeSymbol { IsTupleType: true };
}
