using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>Generic System.Enum statics over a concrete enum: Parse/TryParse/GetValues/GetNames/IsDefined/GetName.</summary>
public sealed partial class FunctionCodeGen
{
    private LLVMValueRef? TryEnumStatic(IInvocationOperation inv, IMethodSymbol target)
    {
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToList();
        INamedTypeSymbol enumType;
        if (target.IsGenericMethod)
        {
            if (S(target.TypeArguments[0]) is not INamedTypeSymbol { TypeKind: TypeKind.Enum } generic) return null;
            enumType = generic;
        }
        else
        {
            // The Type overloads (Enum.GetValues(typeof(Color))): with a typeof operand they are the
            // generic ones in disguise; the leading Type argument is dropped.
            if (target.Parameters.FirstOrDefault()?.Type is not { Name: "Type", ContainingNamespace.Name: "System" }) return null;
            if (TypeOfOperand(args[0].Value) is not { } typeOperand || S(typeOperand) is not INamedTypeSymbol { TypeKind: TypeKind.Enum } named)
            {
                // Configuration.Binder and similar pass a run-time Type into Enum.Parse/IsDefined:
                // lower to a corelib helper (NotSupportedException until a full run-time enum table exists)
                // so those libraries compile; programs that only bind non-enum properties never call it.
                if (target.Name is "Parse" && args.Count is 2 or 3)
                {
                    var typeArg = EmitValue(args[0].Value);
                    var text = Coerce(EmitValue(args[1].Value), TypeOf(args[1].Value), S(args[1].Parameter!.Type));
                    var ignore = args.Count == 3
                        ? Coerce(EmitValue(args[2].Value), TypeOf(args[2].Value), S(args[2].Parameter!.Type))
                        : L.False;
                    return CallCoreLib("EnumRuntime", "Parse", typeArg, text, ignore);
                }
                if (target.Name is "IsDefined" && args.Count == 2)
                    return CallCoreLib("EnumRuntime", "IsDefined", EmitValue(args[0].Value), EmitValue(args[1].Value));
                if (target.Name is "GetUnderlyingType" && args.Count == 1)
                    return CallCoreLib("TypeExtras", "EnumUnderlyingType", EmitValue(args[0].Value));
                if (target.Name is "GetValues" or "GetNames" or "IsDefined" or "Parse" or "GetUnderlyingType")
                    throw CompileError.Unsupported($"Enum.{target.Name} with a Type that is not typeof(an enum)", inv);
                return null;
            }
            enumType = named;
            args.RemoveAt(0);
            if (TryEnumTypeOverload(inv, target, enumType, args) is { } handled) return handled;
            if (target.Name is not ("GetValues" or "GetNames" or "Parse")) return null;  // GetName(Type, object): Intrinsics
        }
        var underlying = enumType.EnumUnderlyingType!;
        var valueType = T.Value(underlying);
        var bits = (int)valueType.IntWidth;
        var signed = TypeMapper.IsSigned(underlying);
        LLVMValueRef Arg(int i) => Coerce(EmitValue(args[i].Value), TypeOf(args[i].Value), S(args[i].Parameter!.Type));
        LLVMValueRef Narrow(LLVMValueRef v64) => bits == 64 ? v64 : _b.Cast(LLVMOpcode.Trunc, v64, valueType);
        LLVMValueRef[] Table()
        {
            var (values, names, count) = _m.EnumTable(enumType);
            return [values, names, L.ConstInt(L.I32, count), L.ConstInt(L.I32, bits), signed ? L.True : L.False];
        }

        switch (target.Name, args.Count)
        {
            case ("TryParse", 2 or 3) when target.IsGenericMethod:
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
                if (!target.IsGenericMethod)  // Enum.Parse(Type, string): the parsed value, boxed
                    return Box(Narrow(CallRuntime("csrt_enum_parse_or_throw", L.I64, [L.Ptr, L.I1, L.Ptr, L.Ptr, L.I32, L.I32, L.I1],
                        [s, ignoreCase, .. Table()])), enumType, inv);
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
                var sorted = _m.EnumMembers(enumType);
                var members = sorted.GroupBy(m => m.Raw).Select(g => (Raw: g.Key, Name: ModuleCodeGen.EnumNameOf(sorted, g.Key, flags: false))).ToList();
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
    /// <summary>Type overloads without a generic counterpart: GetUnderlyingType, IsDefined(Type, object).</summary>
    private LLVMValueRef? TryEnumTypeOverload(IInvocationOperation inv, IMethodSymbol target, INamedTypeSymbol enumType, List<IArgumentOperation> args)
    {
        var underlying = enumType.EnumUnderlyingType!;
        switch (target.Name)
        {
            case "GetUnderlyingType":
                return TypeObject(underlying, inv);
            case "IsDefined":
            {
                // The value is a boxed enum of this type, a boxed integer of its underlying type, or a name.
                var raw = args[0].Value;
                while (true)
                {
                    if (raw is IConversionOperation { Operand.Type: not null } c && (c.Operand.Type.IsValueType || c.Operand.Type.SpecialType == SpecialType.System_String)) raw = c.Operand;
                    else if (raw is IlValueOperation { BoxedFrom: { } boxed }) raw = boxed; // IL's `box E`
                    else break;
                }
                // IL passes a string to the object parameter as is.
                if (raw is IlValueOperation { EntryType.SpecialType: SpecialType.System_String, Type.SpecialType: SpecialType.System_Object } passed)
                    raw = new IlValueOperation(passed.Syntax, passed.EntryType!, passed.Emit);
                var type = TypeOf(raw);
                if (type?.SpecialType == SpecialType.System_String)
                {
                    var name = EmitValue(raw);
                    NullCheck(name);
                    LLVMValueRef any = L.False;
                    foreach (var (_, n) in _m.EnumMembers(enumType))
                        any = _b.BinOp(LLVMOpcode.Or, any, CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], name, _m.StringLiteral(n)));
                    return any;
                }
                if (!SymbolEqualityComparer.Default.Equals(type, enumType) && !SymbolEqualityComparer.Default.Equals(type, underlying))
                    throw CompileError.Unsupported($"Enum.IsDefined with a value of type '{type}' (only '{enumType}', '{underlying}' or string)", inv);
                var v = EmitValue(raw);
                var valueType = T.Value(underlying);
                LLVMValueRef found = L.False;
                foreach (var (r, _) in _m.EnumMembers(enumType))
                    found = _b.BinOp(LLVMOpcode.Or, found, _b.ICmp(LLVMIntPredicate.EQ, v, L.ConstUInt(valueType, r)));
                return found;
            }
        }
        return null;
    }
}
