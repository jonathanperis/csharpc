using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// System.Int128 / System.UInt128 as LLVM i128. Their operators and conversions are library methods
/// (op_Addition, op_Explicit...); this maps them onto the same integer lowering as the primitives
/// (wrapping arithmetic, checked division, masked shifts) and formats via runtime/parse.c.
/// </summary>
internal static class Int128Support
{
    public static bool Is128(ITypeSymbol? t) =>
        t is INamedTypeSymbol { Name: "Int128" or "UInt128", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    public static bool TryGet(IMethodSymbol method, out BclHandler handler)
    {
        handler = null!;
        if (!Is128(method.ContainingType)) return false;
        var self = method.ContainingType;
        BinaryOperatorKind? binary = method.Name switch
        {
            "op_Addition" or "op_CheckedAddition" => BinaryOperatorKind.Add,
            "op_Subtraction" or "op_CheckedSubtraction" => BinaryOperatorKind.Subtract,
            "op_Multiply" or "op_CheckedMultiply" => BinaryOperatorKind.Multiply,
            "op_Division" or "op_CheckedDivision" => BinaryOperatorKind.Divide,
            "op_Modulus" => BinaryOperatorKind.Remainder,
            "op_BitwiseAnd" => BinaryOperatorKind.And,
            "op_BitwiseOr" => BinaryOperatorKind.Or,
            "op_ExclusiveOr" => BinaryOperatorKind.ExclusiveOr,
            "op_LeftShift" => BinaryOperatorKind.LeftShift,
            "op_RightShift" => BinaryOperatorKind.RightShift,
            "op_UnsignedRightShift" => BinaryOperatorKind.UnsignedRightShift,
            "op_Equality" => BinaryOperatorKind.Equals,
            "op_Inequality" => BinaryOperatorKind.NotEquals,
            "op_LessThan" => BinaryOperatorKind.LessThan,
            "op_LessThanOrEqual" => BinaryOperatorKind.LessThanOrEqual,
            "op_GreaterThan" => BinaryOperatorKind.GreaterThan,
            "op_GreaterThanOrEqual" => BinaryOperatorKind.GreaterThanOrEqual,
            _ => null,
        };
        if (binary is { } kind && method.Parameters.Length == 2)
        {
            handler = (g, m, _, a, at) => g.Arithmetic(kind, a[0], a[1], m.Parameters[0].Type, m.Parameters[1].Type, at);
            return true;
        }
        switch (method.Name)
        {
            case "op_UnaryNegation":
                handler = (g, _, _, a, _) => g.Builder.Neg(a[0]);
                return true;
            case "op_UnaryPlus":
                handler = (_, _, _, a, _) => a[0];
                return true;
            case "op_OnesComplement":
                handler = (g, _, _, a, _) => g.Builder.Not(a[0]);
                return true;
            case "op_Increment" or "op_Decrement":
                handler = (g, m, _, a, _) => g.Builder.BinOp(m.Name == "op_Increment" ? LLVMOpcode.Add : LLVMOpcode.Sub, a[0], g.Module.Llvm.ConstInt(a[0].Type, 1));
                return true;
            case "op_Implicit" or "op_Explicit" or "op_CheckedExplicit" when method.Parameters.Length == 1:
                // Integral <-> 128-bit: sign/zero extension or truncation by the source's signedness.
                handler = (g, m, _, a, at) => g.ConvertNumeric(a[0], m.Parameters[0].Type, m.ReturnType, at, isChecked: m.Name == "op_CheckedExplicit");
                return true;
            case "ToString" when method.Parameters.IsEmpty:
                handler = (g, m, s, _, _) => Format(g, s, TypeMapper.IsSigned(m.ContainingType));
                return true;
            case "Equals" when method.Parameters.Length == 1 && Is128(method.Parameters[0].Type):
                handler = (g, _, s, a, _) => g.Builder.ICmp(LLVMIntPredicate.EQ, Load(g, s), a[0]);
                return true;
            case "CompareTo" when method.Parameters.Length == 1 && Is128(method.Parameters[0].Type):
                handler = (g, m, s, a, _) =>
                {
                    var b = g.Builder;
                    var signed = TypeMapper.IsSigned(m.ContainingType);
                    var v = Load(g, s);
                    var i32 = g.Module.Llvm.I32;
                    return b.BinOp(LLVMOpcode.Sub,
                        b.Cast(LLVMOpcode.ZExt, b.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, v, a[0]), i32),
                        b.Cast(LLVMOpcode.ZExt, b.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, v, a[0]), i32));
                };
                return true;
        }
        return false;
    }

    // Instance members receive `self` as an address (structs) — or the value itself; accept both.
    private static LLVMValueRef Load(FunctionCodeGen g, LLVMValueRef self) =>
        self.Type.IsPointer ? g.Builder.Load(g.Module.Llvm.IntTypeOf(128), self) : self;

    internal static LLVMValueRef Format(FunctionCodeGen g, LLVMValueRef self, bool signed)
    {
        var l = g.Module.Llvm;
        var v = Load(g, self);
        var b = g.Builder;
        var lo = b.Cast(LLVMOpcode.Trunc, v, l.I64);
        var hi = b.Cast(LLVMOpcode.Trunc, b.BinOp(LLVMOpcode.LShr, v, l.ConstInt(l.IntTypeOf(128), 64)), l.I64);
        return g.CallRuntime("csrt_format_i128", l.Ptr, [l.I64, l.I64, l.I1], lo, hi, signed ? l.True : l.False);
    }
}
