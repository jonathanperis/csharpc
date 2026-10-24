using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Interface slot tables for boxed primitives (IComparable, IComparable&lt;T&gt;, IEquatable&lt;T&gt;,
/// IFormattable), registered with csrt at startup so `IComparable c = 5; c.CompareTo(3)` dispatches.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly List<(ITypeSymbol Primitive, INamedTypeSymbol Itf, string MT, LLVMValueRef Global)> _deferredPrimitiveSlots = [];

    /// <summary>
    /// The slot table main registers for a boxed primitive: filled in only once the program boxes that
    /// primitive (<see cref="EmitDeferredPrimitiveSlots"/>), so the thunks of never-boxed primitives (an
    /// IFormattable.ToString thunk links that type's formatter) aren't compiled. Boxing can still show up
    /// after main is emitted (runtime exports, type metadata), hence the placeholder.
    /// </summary>
    internal LLVMValueRef DeferredPrimitiveInterfaceSlots(ITypeSymbol primitive, INamedTypeSymbol itf, string mtName)
    {
        var members = InterfaceSlots(itf);
        if (!members.Any(m => PrimitiveSlotSupported(primitive, itf, m))) return LlvmContext.Null(Llvm.Ptr);
        var type = LlvmContext.Array(Llvm.Ptr, (ulong)members.Count);
        var global = Module.AddGlobal(".primslots", type, LlvmContext.Null(type), constant: true, linkage: LLVMLinkage.Private);
        _deferredPrimitiveSlots.Add((primitive, itf, mtName, global));
        return global;
    }

    /// <summary>Fills the slot tables of primitives the program boxes; true if it compiled anything.</summary>
    private bool EmitDeferredPrimitiveSlots()
    {
        var any = false;
        for (var i = _deferredPrimitiveSlots.Count - 1; i >= 0; i--)
        {
            var (primitive, itf, mt, global) = _deferredPrimitiveSlots[i];
            if (!_globals.ContainsKey(mt) || _registrationOnlyGlobals.Contains(mt)) continue;
            _deferredPrimitiveSlots.RemoveAt(i);
            var slots = InterfaceSlots(itf).Select(m => PrimitiveSlot(primitive, itf, m)).ToArray();
            Native.LLVMSetInitializer(global, LlvmContext.ConstArray(Llvm.Ptr, [.. slots.Select(s => s.IsNull ? LlvmContext.Null(Llvm.Ptr) : s)]));
            any = true;
        }
        return any;
    }

    /// <summary>Does <see cref="PrimitiveSlot"/> implement this member for boxed values of the primitive?</summary>
    internal static bool PrimitiveSlotSupported(ITypeSymbol primitive, INamedTypeSymbol itf, IMethodSymbol member) =>
        IsBoxedBuiltinValue(primitive)
        && (itf.Name, member.Name) switch
        {
            ("IComparable", "CompareTo") when !itf.IsGenericType => true,
            ("IFormattable", "ToString") => true,
            ("IComparable", "CompareTo") or ("IEquatable", "Equals") => itf.IsGenericType && SymbolEqualityComparer.Default.Equals(itf.TypeArguments[0], primitive),
            _ => false,
        };

    /// <summary>
    /// Value types whose boxes have a runtime method table (csrt_Boxed_X_MT) that main registers for the
    /// program's interfaces: the primitives, and decimal, DateTime and TimeSpan, which .NET boxes as
    /// IFormattable/IComparable too (without the registration `o is IFormattable` was silently false).
    /// </summary>
    internal static bool IsBoxedBuiltinValue(ITypeSymbol type) =>
        type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Double || BclValueTypes.IsTicks(type);

    private LLVMValueRef PrimitiveSlot(ITypeSymbol primitive, INamedTypeSymbol itf, IMethodSymbol member)
    {
        switch (itf.Name, member.Name)
        {
            case ("IComparable", "CompareTo") when !itf.IsGenericType:
            {
                var fnType = Llvm.FunctionType(Llvm.I32, Llvm.Ptr, Llvm.Ptr);
                var fn = Module.AddFunction($".prim.{primitive.MetadataName}.IComparable.object", fnType);
                Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
                new FunctionCodeGen(this, null, new LoweredFunction(fn, fnType)).EmitPrimitiveCompareObjectThunk(primitive);
                return fn;
            }
            case ("IFormattable", "ToString"):
            {
                var fnType = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
                var fn = Module.AddFunction($".prim.{primitive.MetadataName}.IFormattable", fnType);
                Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
                new FunctionCodeGen(this, null, new LoweredFunction(fn, fnType)).EmitPrimitiveFormatThunk(primitive);
                return fn;
            }
            case ("IComparable", "CompareTo") or ("IEquatable", "Equals") when itf.IsGenericType
                && SymbolEqualityComparer.Default.Equals(itf.TypeArguments[0], primitive):
            {
                var isCompare = member.Name == "CompareTo";
                var valueType = Types.Value(primitive);
                var fnType = Llvm.FunctionType(isCompare ? Llvm.I32 : Llvm.I1, Llvm.Ptr, valueType);
                var fn = Module.AddFunction($".prim.{primitive.MetadataName}.{itf.Name}", fnType);
                Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
                new FunctionCodeGen(this, null, new LoweredFunction(fn, fnType)).EmitPrimitiveInterfaceThunk(primitive, isCompare);
                return fn;
            }
        }
        return default;
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>thunk(box, T other): unbox the receiver, then CompareTo / Equals.</summary>
    internal void EmitPrimitiveInterfaceThunk(ITypeSymbol primitive, bool isCompare)
    {
        var self = LoadValue(_b.InBoundsGep(L.I8, _fn.Fn.Param(0), [L.ConstInt(L.I64, 8)]), primitive);
        var other = _fn.Fn.Param(1);
        _b.Ret(isCompare ? CompareValues(self, other, primitive, null!) : FieldEquals(self, other, primitive));
        Finish();
    }

    /// <summary>
    /// IComparable.CompareTo(object) on a boxed primitive: null sorts first; a different boxed type throws
    /// ArgumentException("Object must be of type Int32."), as .NET.
    /// </summary>
    internal void EmitPrimitiveCompareObjectThunk(ITypeSymbol primitive)
    {
        var self = _fn.Fn.Param(0);
        var other = _fn.Fn.Param(1);
        var isNull = NewBlock("other.null");
        var notNull = NewBlock("other.nonnull");
        var mismatch = NewBlock("other.mismatch");
        var compare = NewBlock("other.compare");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, other, LlvmContext.Null(L.Ptr)), isNull, notNull);
        _b.PositionAtEnd(isNull);
        _b.Ret(L.ConstInt(L.I32, 1));
        _b.PositionAtEnd(notNull);
        var sameType = _b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, self), _b.Load(L.Ptr, other));
        _b.CondBr(sameType, compare, mismatch);
        _b.PositionAtEnd(mismatch);
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.ArgumentException")!,
            $"Object must be of type {primitive.MetadataName}.");
        _b.PositionAtEnd(compare);
        LLVMValueRef Payload(LLVMValueRef box) => LoadValue(_b.InBoundsGep(L.I8, box, [L.ConstInt(L.I64, 8)]), primitive);
        _b.Ret(CompareValues(Payload(self), Payload(other), primitive, null!));
        Finish();
    }

    /// <summary>
    /// IComparable.CompareTo(object) on a boxed enum (System.Enum.CompareTo): null sorts first, a box of
    /// another type throws .NET's ArgumentException naming both types, else the underlying values compare.
    /// </summary>
    internal void EmitEnumCompareObjectThunk(INamedTypeSymbol enumType, LLVMValueRef methodTable)
    {
        var underlying = enumType.EnumUnderlyingType!;
        var (self, other) = (_fn.Fn.Param(0), _fn.Fn.Param(1));
        var isNull = NewBlock("other.null");
        var notNull = NewBlock("other.nonnull");
        var mismatch = NewBlock("other.mismatch");
        var compare = NewBlock("other.compare");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, other, LlvmContext.Null(L.Ptr)), isNull, notNull);
        _b.PositionAtEnd(isNull);
        _b.Ret(L.ConstInt(L.I32, 1));
        _b.PositionAtEnd(notNull);
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, other), methodTable), compare, mismatch);
        _b.PositionAtEnd(mismatch);
        CallCoreLib("ThrowHelper", "EnumCompareMismatch", other, _m.StringLiteral(Mangler.MetadataName(enumType)));
        _b.Ret(L.ConstInt(L.I32, 0)); // reached only on wasm32, where the exception is pending
        _b.PositionAtEnd(compare);
        LLVMValueRef Payload(LLVMValueRef box) => LoadValue(_b.InBoundsGep(L.I8, box, [L.ConstInt(L.I64, 8)]), underlying);
        _b.Ret(CompareValues(Payload(self), Payload(other), underlying, null!));
        Finish();
    }

    /// <summary>
    /// IFormattable.ToString(format, provider) on a boxed primitive: the primitive's own formatter, with the
    /// provider (char ignores both, as in .NET). Not the general boxed-value formatter, which would link the
    /// double and decimal formatters into every program that formats a boxed int through IFormattable.
    /// </summary>
    internal void EmitPrimitiveFormatThunk(ITypeSymbol primitive)
    {
        var value = LoadValue(_b.InBoundsGep(L.I8, _fn.Fn.Param(0), [L.ConstInt(L.I64, 8)]), primitive);
        var (format, provider) = (_fn.Fn.Param(1), _fn.Fn.Param(2));
        var bits = L.ConstInt(L.I32, value.Type.IsInteger ? value.Type.IntWidth : 0);
        _b.Ret(primitive.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 =>
                CallCoreLib("Number", "FormatBoxedSigned", value.Type.IntWidth == 64 ? value : _b.Cast(LLVMOpcode.SExt, value, L.I64), format, bits, provider),
            SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 =>
                CallCoreLib("Number", "FormatBoxedUnsigned", value.Type.IntWidth == 64 ? value : _b.Cast(LLVMOpcode.ZExt, value, L.I64), format, bits, provider),
            SpecialType.System_Double => CallCoreLib("Number", "FormatBoxedFloat", value, format, L.False, provider),
            SpecialType.System_Single => CallCoreLib("Number", "FormatBoxedFloat", _b.Cast(LLVMOpcode.FPExt, value, L.F64), format, L.True, provider),
            SpecialType.System_Decimal => CallCoreLib("Number", "FormatBoxedDecimal", value, format, provider),
            SpecialType.System_DateTime => CallCoreLib("DateTimeOps", "BoxedDateTimeFormatWith", value, format, provider),
            _ when BclValueTypes.IsTimeSpan(primitive) => CallCoreLib("DateTimeOps", "BoxedTimeSpanFormatWith", value, format, provider),
            _ => FormatPrimitive(value, primitive) ?? CallRuntime("csrt_format_object", L.Ptr, [L.Ptr, L.Ptr], _fn.Fn.Param(0), format),
        });
        Finish();
    }
}
