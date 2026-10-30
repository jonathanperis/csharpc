using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// The CLR's array covariance checks. A string[] is an object[], so a store through an object[] must check
/// the value against the array's real element type (stelem.ref: ArrayTypeMismatchException), and a writable
/// reference to an element (ldelema without `readonly.`, new Span&lt;T&gt;(T[])) needs the element type to be
/// exactly the static one. As in CoreCLR/NativeAOT, only arrays whose static element type has subtypes can
/// be covariant, and the common case is one inline test: the array's method table is its static type's.
/// Anything else (a covariant array, or one the runtime allocated untagged) goes to csrt.c's slow paths.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>
    /// Can a T[] (T the static element type) hold an array whose element type is another type, so that
    /// stores and writable element references need checking? Value types, sealed classes (string) and
    /// arrays of those have no other array types they could be.
    /// </summary>
    internal static bool NeedsCovarianceCheck(ITypeSymbol element) => element switch
    {
        { IsReferenceType: false } => false,
        IArrayTypeSymbol array => NeedsCovarianceCheck(array.ElementType),
        // Delegates are sealed, but variance makes Func<string>[] a Func<object>[].
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } d => MayBeVariant(d),
        INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true } => false,
        _ => true,
    };

    private static bool IsNullConstant(LLVMValueRef value) => Native.LLVMIsNull(value) != 0;

    /// <summary>The method table an array of static type <paramref name="arrayType"/> carries when its runtime type is that.</summary>
    private LLVMValueRef StaticArrayMethodTable(IArrayTypeSymbol arrayType) =>
        arrayType.IsSZArray ? _m.ArrayMethodTable(arrayType.ElementType) : _m.MdArrayMethodTable(arrayType);

    /// <summary>
    /// `if (array->mt != static T[]'s) slow(...)`: the slow call sits in its own block after the test.
    /// </summary>
    private void IfNotStaticArrayType(LLVMValueRef array, IArrayTypeSymbol arrayType, string helper, LLVMTypeRef[] parameters, params LLVMValueRef[] args)
    {
        var expected = StaticArrayMethodTable(arrayType);
        var mt = _b.Load(L.Ptr, array, "array.mt");
        _m.Facts.MethodTableLoad(mt);
        var slow = NewBlock("array.covariant");
        var done = NewBlock("array.checked");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, mt, expected), done, slow);
        _b.PositionAtEnd(slow);
        CallRuntime(helper, L.Void, parameters, args);
        _b.Br(done);
        _b.PositionAtEnd(done);
    }

    /// <summary>stelem.ref's check before storing <paramref name="value"/> into a (non-null) array of static type <paramref name="arrayType"/>.</summary>
    internal void ArrayStoreCheck(LLVMValueRef array, IArrayTypeSymbol arrayType, LLVMValueRef value)
    {
        if (!NeedsCovarianceCheck(arrayType.ElementType) || IsNullConstant(value)) return;
        IfNotStaticArrayType(array, arrayType, "csrt_array_store_check", [L.Ptr, L.Ptr], array, value);
    }

    /// <summary>ldelema's (and new Span&lt;T&gt;(T[])'s) check: the (non-null) array's element type is exactly the static one.</summary>
    internal void ArrayExactTypeCheck(LLVMValueRef array, IArrayTypeSymbol arrayType)
    {
        if (!NeedsCovarianceCheck(arrayType.ElementType)) return;
        IfNotStaticArrayType(array, arrayType, "csrt_array_exact_check", [L.Ptr, L.Ptr], array, StaticArrayMethodTable(arrayType));
    }

    /// <summary>A store that needs stelem.ref's check: the array and its static type.</summary>
    private sealed record CovariantStore(LLVMValueRef Array, IArrayTypeSymbol Type);

    /// <summary>
    /// The address an assignment stores to. An array element whose array may be covariant also yields the
    /// array, to check the value once it is computed (C# evaluates the target's array and index first).
    /// </summary>
    private LLVMValueRef StoreAddress(IOperation target, out CovariantStore? covariant)
    {
        covariant = null;
        if (target is IArrayElementReferenceOperation element && TypeOf(element.ArrayReference) is IArrayTypeSymbol arrayType
            && NeedsCovarianceCheck(arrayType.ElementType))
        {
            var address = ElementAddress(element, out var array);
            covariant = new CovariantStore(array, arrayType);
            return address;
        }
        if (target is IFlowCaptureReferenceOperation capture && _cfg!.CapturedArrays.TryGetValue(capture.Id, out var captured))
            covariant = new CovariantStore(_b.Load(L.Ptr, captured.Slot), captured.Type);
        return AddressOf(target);
    }

    /// <summary>ldelema: a writable reference to an element (ref locals, ref/out arguments, ref returns).</summary>
    private LLVMValueRef WritableElementAddress(IArrayElementReferenceOperation element)
    {
        var address = ElementAddress(element, out var array);
        if (TypeOf(element.ArrayReference) is IArrayTypeSymbol arrayType) ArrayExactTypeCheck(array, arrayType);
        return address;
    }

    /// <summary>`readonly. ldelema` (ref readonly locals and returns, `in` arguments): no exact-type check.</summary>
    private LLVMValueRef ReadOnlyAddressOf(IOperation target) =>
        target is IArrayElementReferenceOperation element ? ElementAddress(element) : AddressOf(target);

    /// <summary>
    /// The address an lvalue capture holds (`a[i] = c ? x : y`, `a[i] ??= v`: Roslyn's CFG captures the target
    /// before the value's branches). A covariance-checked element remembers its array for the store.
    /// </summary>
    private LLVMValueRef CaptureLvalueAddress(IFlowCaptureOperation lvalue)
    {
        if (lvalue.Value is IArrayElementReferenceOperation element && TypeOf(element.ArrayReference) is IArrayTypeSymbol arrayType
            && NeedsCovarianceCheck(arrayType.ElementType))
        {
            var address = ElementAddress(element, out var array);
            var slot = _allocas.Alloca(L.Ptr, "capture.array");
            _b.Store(array, slot);
            _cfg!.CapturedArrays[lvalue.Id] = (slot, arrayType);
            return address;
        }
        return AddressOrSpill(lvalue.Value);
    }
}
