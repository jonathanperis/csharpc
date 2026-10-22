using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>Generic System.Enum statics over a concrete enum: Parse/TryParse/GetValues/GetNames/IsDefined/GetName.</summary>
public sealed partial class FunctionCodeGen
{
    private LLVMValueRef? TryEnumStatic(IInvocationOperation inv, IMethodSymbol target)
    {
        if (!target.IsGenericMethod || target.TypeArguments[0] is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) return null;
        var underlying = enumType.EnumUnderlyingType!;
        var valueType = T.Value(underlying);
        var bits = (int)valueType.IntWidth;
        var signed = TypeMapper.IsSigned(underlying);
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToList();
        LLVMValueRef Arg(int i) => Coerce(EmitValue(args[i].Value), TypeOf(args[i].Value), S(args[i].Parameter!.Type));
        LLVMValueRef Narrow(LLVMValueRef v64) => bits == 64 ? v64 : _b.Cast(LLVMOpcode.Trunc, v64, valueType);
        LLVMValueRef[] Table()
        {
            var (values, names, count) = _m.EnumTable(enumType);
            return [values, names, L.ConstInt(L.I32, count), L.ConstInt(L.I32, bits), signed ? L.True : L.False];
        }

        switch (target.Name, args.Count)
        {
            case ("TryParse", 2 or 3):
            {
                var s = Arg(0);
                var ignoreCase = args.Count == 3 ? Arg(1) : L.False;
                var temp = NewTemp(L.I64, "enum.parsed");
                var status = CallRuntime("csrt_enum_parse", L.I32, [L.Ptr, L.I1, L.Ptr, L.Ptr, L.I32, L.I32, L.I1, L.Ptr],
                    [s, ignoreCase, .. Table(), temp]);
                var ok = _b.ICmp(LLVMIntPredicate.EQ, status, L.ConstInt(L.I32, 1));
                _b.Store(Narrow(_b.Load(L.I64, temp)), AddressOf(args[^1].Value));
                return ok;
            }
            case ("Parse", 1 or 2):
            {
                var s = Arg(0);
                var ignoreCase = args.Count == 2 ? Arg(1) : L.False;
                return Narrow(CallRuntime("csrt_enum_parse_or_throw", L.I64, [L.Ptr, L.I1, L.Ptr, L.Ptr, L.I32, L.I32, L.I1],
                    [s, ignoreCase, .. Table()]));
            }
            case ("GetValues", 0):
            {
                var members = _m.EnumMembers(enumType);
                var array = NewArray(enumType, L.ConstInt(L.I64, members.Count));
                for (var i = 0; i < members.Count; i++)
                    StoreValue(L.ConstUInt(valueType, members[i].Raw),
                        _b.InBoundsGep(T.ArrayHeader(valueType), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, i)]));
                return array;
            }
            case ("GetNames", 0):
            {
                var members = _m.EnumMembers(enumType);
                var array = NewArray(_m.Compilation.GetSpecialType(SpecialType.System_String), L.ConstInt(L.I64, members.Count));
                for (var i = 0; i < members.Count; i++)
                    _b.Store(_m.StringLiteral(members[i].Name),
                        _b.InBoundsGep(T.ArrayHeader(L.Ptr), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, i)]));
                return array;
            }
            case ("IsDefined", 1) or ("GetName", 1):
            {
                var v = Arg(0);
                var members = _m.EnumMembers(enumType).GroupBy(m => m.Raw).Select(g => g.First()).ToList();
                if (target.Name == "IsDefined")
                {
                    LLVMValueRef any = L.False;
                    foreach (var (raw, _) in members) any = _b.BinOp(LLVMOpcode.Or, any, _b.ICmp(LLVMIntPredicate.EQ, v, L.ConstUInt(valueType, raw)));
                    return any;
                }
                LLVMValueRef name = LlvmContext.Null(L.Ptr);
                foreach (var (raw, n) in Enumerable.Reverse(members))
                    name = _b.Select(_b.ICmp(LLVMIntPredicate.EQ, v, L.ConstUInt(valueType, raw)), _m.StringLiteral(n), name);
                return name;
            }
        }
        return null;
    }
}
