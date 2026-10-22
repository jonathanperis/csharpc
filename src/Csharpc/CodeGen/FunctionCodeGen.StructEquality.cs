using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Bodies of the per-struct ValueType.Equals / ValueType.GetHashCode (ModuleCodeGen.StructEquality.cs).
///
/// CoreCLR (System.Private.CoreLib ValueType.cs, vm/comutilnative.cpp):
///  * Equals compares the raw bytes only when CanCompareBitsOrUseFastGetHashCode holds — no GC
///    references, tightly packed, no float/double fields, no field of a struct type overriding
///    Equals/GetHashCode (recursively). Otherwise it walks the instance fields in declaration order,
///    boxes each and returns false at the first `!thisField.Equals(thatField)` (null fields are equal
///    only to null). The byte comparison is only taken where it agrees with the field walk, so the
///    field walk *is* the semantics: string fields compare by value, double/float fields with
///    double.Equals (NaN equals NaN, 0.0 equals -0.0), decimal fields by value, nested structs
///    recursively, fields of types overriding Equals(object) through that override. Comparing fields
///    (never bytes) also keeps padding out of it.
///  * GetHashCode hashes all bytes on the fast path; otherwise it hashes a single field: the first
///    non-null reference field or the first non-reference field, descending into nested structs that
///    neither compare bitwise nor override GetHashCode. .NET seeds it with a random per-process value,
///    so only its consistency with Equals and which user GetHashCode overrides it calls are observable.
///    csharpc mixes every field whose hash runs no user code (better distribution than the CLR's
///    single field), plus CLR's chosen field when hashing it may run user code.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private static IEnumerable<IFieldSymbol> InstanceFields(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst);

    private INamedTypeSymbol? UserStruct(ITypeSymbol t) =>
        t is INamedTypeSymbol { TypeKind: TypeKind.Struct } named && !t.IsTupleType && !TypeMapper.IsNullable(t) && _m.IsUserType(named)
            ? named : null;

    private static bool IsPrimitiveLike(ITypeSymbol t) =>
        IsIntegral(t) || TypeMapper.IsFloatingPoint(t) || Int128Support.Is128(t)
        || t.TypeKind is TypeKind.Enum or TypeKind.Pointer or TypeKind.FunctionPointer;

    /// <summary>
    /// Whether comparing/hashing a value of this type runs no user code (so it may be evaluated eagerly,
    /// out of order, or more often than .NET does). <paramref name="hash"/>: GetHashCode, else Equals.
    /// </summary>
    private bool RunsNoUserCode(ITypeSymbol t, bool hash, int depth = 0)
    {
        t = _m.CoreLib.Map(t);
        if (depth > 16) return false;
        if (IsPrimitiveLike(t) || t.SpecialType is SpecialType.System_String or SpecialType.System_Decimal
            || BclValueTypes.IsTicks(t) || BclValueTypes.IsBigInteger(t)) return true;
        if (t is INamedTypeSymbol { IsTupleType: true } tuple) return tuple.TupleElements.All(e => RunsNoUserCode(e.Type, hash, depth + 1));
        if (TypeMapper.IsNullable(t)) return RunsNoUserCode(((INamedTypeSymbol)t).TypeArguments[0], hash, depth + 1);
        if (UserStruct(t) is { } s)
            return ModuleCodeGen.StructOverride(s, "Equals") is null && ModuleCodeGen.StructOverride(s, "GetHashCode") is null
                && (hash || GenericInterfaceImpl(s, "System.IEquatable`1", "Equals") is null)
                && InstanceFields(s).All(f => RunsNoUserCode(f.Type, hash, depth + 1));
        return false;
    }

    /// <summary>
    /// Calls to library Equals/GetHashCode with a value receiver: `base.Equals(obj)` / `base.GetHashCode()`
    /// in a struct (ValueType's implementations), and GetHashCode()/Equals(T)/Equals(object) on primitives.
    /// </summary>
    private LLVMValueRef? TryValueEqualityCall(Microsoft.CodeAnalysis.Operations.IInvocationOperation inv, IMethodSymbol target)
    {
        if (target.IsStatic || inv.Instance is null || target.Name is not ("Equals" or "GetHashCode")) return null;
        var isHash = target is { Name: "GetHashCode", Parameters.IsEmpty: true };
        var isEquals = target is { Name: "Equals", Parameters.Length: 1 };
        if (!isHash && !isEquals) return null;
        var owner = target.ContainingType;
        if (owner.SpecialType == SpecialType.System_ValueType
            && inv.Instance is Microsoft.CodeAnalysis.Operations.IInstanceReferenceOperation && _method is not null
            && UserStruct(_m.CoreLib.Map(S(_method.ContainingType))) is { } s)
        {
            var self = ThisValue(inv);
            if (isHash)
            {
                var hash = _m.StructDefaultHash(s);
                return _b.Call(hash.Type, hash.Fn, [self]);
            }
            // ValueType.Equals: false for null or another type (GetType() != obj.GetType()), else field by field.
            var other = EmitArgument(inv.Arguments[0]);
            var eq = _m.StructDefaultEquals(s);
            return ShortCircuit(isAnd: true,
            [
                () => _b.ICmp(LLVMIntPredicate.NE, other, LlvmContext.Null(L.Ptr)),
                () => _b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, other), _m.StructMethodTable(s)),
                () => _b.Call(eq.Type, eq.Fn, [self, _b.InBoundsGep(L.I8, other, [L.ConstInt(L.I64, 8)])]),
            ]);
        }
        var receiver = inv.Instance;
        if (owner.SpecialType == SpecialType.System_Enum)
        {
            // Enum.GetHashCode/Equals on an enum value (the receiver is boxed to System.Enum).
            while (receiver is Microsoft.CodeAnalysis.Operations.IConversionOperation conversion) receiver = conversion.Operand;
            if (TypeOf(receiver) is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) return null;
            owner = enumType;
        }
        else if (!(IsIntegral(owner) || TypeMapper.IsFloatingPoint(owner)) || !SymbolEqualityComparer.Default.Equals(TypeOf(receiver), owner))
            return null;
        var value = EmitValue(receiver);
        if (isHash) return HashOf(owner, value, inv);
        var parameterType = target.Parameters[0].Type;
        if (SymbolEqualityComparer.Default.Equals(parameterType, owner)) return EqualsOf(owner, value, EmitArgument(inv.Arguments[0]), inv);
        if (parameterType.SpecialType != SpecialType.System_Object) return null;
        var boxed = Box(value, owner, inv);
        return VirtualCall(ObjectMethod("Equals", 1), boxed, [boxed, EmitArgument(inv.Arguments[0])]);
    }

    // ======================================================================================
    // Equals
    // ======================================================================================

    internal void EmitStructDefaultEquals(StructLayout layout)
    {
        var (a, b) = (ParamOf(0), ParamOf(1));
        var fields = InstanceFields(layout.Type).ToList();
        Func<LLVMValueRef> Compare(IFieldSymbol f) =>
            () => ObjectEquals(f.Type, LoadValue(FieldAddressOn(a, f), f.Type), LoadValue(FieldAddressOn(b, f), f.Type));
        if (fields.All(f => RunsNoUserCode(f.Type, hash: false)))
        {
            // No user code involved: evaluate every field (straight-line code LLVM can merge into wide compares).
            var all = L.True;
            foreach (var f in fields) all = _b.BinOp(LLVMOpcode.And, all, Compare(f)());
            _b.Ret(all);
        }
        else _b.Ret(ShortCircuit(isAnd: true, fields.Select(Compare)));
        Finish();
    }

    /// <summary>
    /// `((object)a).Equals((object)b)` as ValueType.Equals applies it to one pair of fields (a null `a`
    /// equals only a null `b`): Equals(object) semantics, not IEquatable&lt;T&gt;.
    /// </summary>
    private LLVMValueRef ObjectEquals(ITypeSymbol type, LLVMValueRef a, LLVMValueRef b)
    {
        var t = EnumBase(_m.CoreLib.Map(type));
        if (IsIntegral(t) || Int128Support.Is128(t) || t.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
            return _b.ICmp(LLVMIntPredicate.EQ, a, b);
        if (TypeMapper.IsFloatingPoint(t) || t.SpecialType is SpecialType.System_String or SpecialType.System_Decimal
            || BclValueTypes.IsTicks(t) || BclValueTypes.IsBigInteger(t) || t.IsTupleType)
            return EqualsOf(t, a, b, null!);  // same as their Equals(object); tuples use EqualityComparer<Ti> per element
        if (TypeMapper.IsNullable(t))
        {
            // Boxing T? yields null or a boxed T: equal when both are empty, else T's Equals(object).
            var u = ((INamedTypeSymbol)t).TypeArguments[0];
            var (ha, hb) = (NullableHas(a), NullableHas(b));
            if (UserStruct(_m.CoreLib.Map(u)) is { } us && ModuleCodeGen.StructOverride(us, "Equals") is { } overrider)
            {
                // The override sees `Equals(null)` when only b is empty.
                return ShortCircuit(isAnd: false,
                [
                    () => _b.BinOp(LLVMOpcode.And, _b.Not(ha), _b.Not(hb)),
                    () => ShortCircuit(isAnd: true, [() => ha, () => CallMethod(overrider, [Spill(NullablePayload(a, u)), Box(b, t, null!)])]),
                ]);
            }
            return ShortCircuit(isAnd: false,
            [
                () => _b.BinOp(LLVMOpcode.And, _b.Not(ha), _b.Not(hb)),
                () => ShortCircuit(isAnd: true, [() => _b.BinOp(LLVMOpcode.And, ha, hb), () => ObjectEquals(u, NullablePayload(a, u), NullablePayload(b, u))]),
            ]);
        }
        if (UserStruct(t) is { } s)
        {
            if (ModuleCodeGen.StructOverride(s, "Equals") is { } overrider) return CallMethod(overrider, [Spill(a), Box(b, s, null!)]);
            var eq = _m.StructDefaultEquals(s);
            return _b.Call(eq.Type, eq.Fn, [Spill(a), Spill(b)]);
        }
        if (t.IsReferenceType)
            return BranchOnNull(a, L.I1, () => _b.ICmp(LLVMIntPredicate.EQ, b, LlvmContext.Null(L.Ptr)),
                () => VirtualCall(ObjectMethod("Equals", 1), a, [a, b]));
        return EqualsOf(t, a, b, null!);
    }

    // ======================================================================================
    // GetHashCode
    // ======================================================================================

    private const int HashPrime1 = unchecked((int)0x9E3779B1), HashPrime2 = unchecked((int)0x85EBCA77);

    /// <summary>h = rotl(h + x * P2, 13) * P1 (an xxHash32 round).</summary>
    private LLVMValueRef HashMix(LLVMValueRef h, LLVMValueRef x)
    {
        var i32 = L.I32;
        h = _b.BinOp(LLVMOpcode.Add, h, _b.BinOp(LLVMOpcode.Mul, x, L.ConstInt(i32, HashPrime2)));
        h = _b.BinOp(LLVMOpcode.Or, _b.BinOp(LLVMOpcode.Shl, h, L.ConstInt(i32, 13)), _b.BinOp(LLVMOpcode.LShr, h, L.ConstInt(i32, 19)));
        return _b.BinOp(LLVMOpcode.Mul, h, L.ConstInt(i32, HashPrime1));
    }

    internal void EmitStructDefaultHash(StructLayout layout)
    {
        var self = ParamOf(0);
        var h = MixPureFields(layout.Type, self, L.ConstInt(L.I32, 0x165667B1), 0);
        if (!RunsNoUserCode(layout.Type, hash: true))
        {
            var chosen = NewTemp(L.I32, "hash.field");
            _b.Store(L.ConstInt(L.I32, 0), chosen);
            var done = NewBlock("hash.done");
            HashClrChosenField(layout.Type, self, chosen, done, 0);
            _b.PositionAtEnd(done);
            h = HashMix(h, _b.Load(L.I32, chosen));
        }
        // Final avalanche, so Dictionary's modulo sees every input bit.
        var i32 = L.I32;
        h = _b.BinOp(LLVMOpcode.Xor, h, _b.BinOp(LLVMOpcode.LShr, h, L.ConstInt(i32, 15)));
        h = _b.BinOp(LLVMOpcode.Mul, h, L.ConstInt(i32, HashPrime2));
        h = _b.BinOp(LLVMOpcode.Xor, h, _b.BinOp(LLVMOpcode.LShr, h, L.ConstInt(i32, 13)));
        _b.Ret(h);
        Finish();
    }

    /// <summary>Mixes the hash of every field (nested struct fields flattened) whose hashing runs no user code.</summary>
    private LLVMValueRef MixPureFields(INamedTypeSymbol type, LLVMValueRef address, LLVMValueRef h, int depth)
    {
        foreach (var f in InstanceFields(type))
        {
            var ft = _m.CoreLib.Map(f.Type);
            if (!RunsNoUserCode(ft, hash: true)) continue;
            var fieldAddress = FieldAddressOn(address, f);
            if (UserStruct(ft) is { } nested && depth < 16)
                h = MixPureFields(nested, fieldAddress, h, depth + 1);
            else
                h = HashMix(h, HashOf(ft, LoadValue(fieldAddress, f.Type), null!));
        }
        return h;
    }

    /// <summary>
    /// CoreCLR's GetHashCodeStrategy: the first non-null reference field, or the first non-reference
    /// field, recursing into a struct field that neither compares bitwise nor overrides GetHashCode.
    /// Stores the field's hash in <paramref name="slot"/> only when computing it may run user code (the
    /// other fields are already in MixPureFields); every path ends at <paramref name="done"/>.
    /// </summary>
    private void HashClrChosenField(INamedTypeSymbol type, LLVMValueRef address, LLVMValueRef slot, LLVMBasicBlockRef done, int depth)
    {
        foreach (var f in InstanceFields(type))
        {
            var ft = _m.CoreLib.Map(f.Type);
            var fieldAddress = FieldAddressOn(address, f);
            if (ft.IsReferenceType)
            {
                var value = LoadValue(fieldAddress, f.Type);
                var chosen = NewBlock("hash.ref");
                var next = NewBlock("hash.next");
                _b.CondBr(_b.ICmp(LLVMIntPredicate.NE, value, LlvmContext.Null(L.Ptr)), chosen, next);
                _b.PositionAtEnd(chosen);
                if (!RunsNoUserCode(ft, hash: true)) _b.Store(VirtualCall(ObjectMethod("GetHashCode", 0), value, [value]), slot);
                _b.Br(done);
                _b.PositionAtEnd(next);
                continue;
            }
            if (UserStruct(ft) is { } nested && ModuleCodeGen.StructOverride(nested, "GetHashCode") is null
                && !RunsNoUserCode(nested, hash: true) && depth < 16)
            {
                HashClrChosenField(nested, fieldAddress, slot, done, depth + 1);
                return;
            }
            if (!RunsNoUserCode(ft, hash: true)) _b.Store(HashOf(ft, LoadValue(fieldAddress, f.Type), null!), slot);
            _b.Br(done);
            return;
        }
        _b.Br(done); // no fields, or every reference field is null
    }
}
