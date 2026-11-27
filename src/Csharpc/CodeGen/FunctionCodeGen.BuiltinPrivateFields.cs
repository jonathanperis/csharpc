using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Private fields of built-in types in CoreLib IL (#393, #405): the generic-math, conversion and formatting code served from the
/// framework's IL reads and writes Int128's halves (_lower, _upper) and decimal's parts (_flags, _hi32, _lo64), which the reference pack
/// doesn't declare. csharpc keeps those types in .NET's own layout (Int128 as a little-endian LLVM i128, decimal as { int, uint, ulong }:
/// TypeMapper), so a field access is a load or store of the field's type at its offset in the value; ldflda stays inside the value.
/// String's _stringLength and _firstChar (the String members hosted from CoreLib IL: `ref _firstChar`, `this._stringLength`) are read the
/// same way through the object reference, after the method-table pointer: csharpc's string is .NET's layout (csrt_String).
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>ldfld / ldflda / stfld of such a field; false when the token is any other field.</summary>
    private bool TryEmitIlBuiltinPrivateField(System.Reflection.Metadata.ILOpCode op, int token, Stack<IlEntry> stack)
    {
        if (IlAssembly.IsSpanPrivateField(token, IlBody.Context, out var span, out var isReference)) return EmitIlSpanField(op, (INamedTypeSymbol)S(span!), isReference, stack);
        if (!IlAssembly.IsBuiltinPrivateField(token, IlBody.Context, out var fieldOffset, out var special, out var afterHeader)) return false;
        long offset = fieldOffset + (afterHeader ? (long)T.PointerSize : 0);
        var fieldType = Special(special);
        var value = op == System.Reflection.Metadata.ILOpCode.Stfld ? stack.Pop() : null;
        var instance = stack.Pop();
        var baseAddress = instance.Kind switch
        {
            IlKind.Value => Spill(instance.Value),
            IlKind.ByRef or IlKind.Pointer or IlKind.NativeInt => IlAddress(instance),
            // An object reference (a string): ldfld on null is a NullReferenceException, as in .NET; a hosted member's `this` was checked
            // by its caller (EmitHostedInstanceCall), and LLVM folds the repeated check.
            IlKind.Ref when afterHeader => NullChecked(instance),
            _ => throw new IlException($"a built-in type's field accessed through a {instance.Kind} value"),
        };
        var address = offset == 0 ? baseAddress : _b.InBoundsGep(L.I8, baseAddress, [L.ConstInt(L.I64, offset)]);
        switch (op)
        {
            case System.Reflection.Metadata.ILOpCode.Ldflda:
                stack.Push(new IlEntry(IlKind.ByRef, address, fieldType));
                break;
            case System.Reflection.Metadata.ILOpCode.Stfld:
                StoreValue(ToStorage(IlTyped(value!, fieldType), fieldType), address);
                break;
            default:
                stack.Push(IlPush(LoadValue(address, fieldType), fieldType));
                break;
        }
        return true;
    }

    /// <summary>
    /// ldfld / ldflda / stfld of a span's _reference (a `ref T`: offset 0) or _length (an int after the pointer): csharpc's span is
    /// { ref T, int } (TypeMapper), as .NET's is.
    /// </summary>
    private bool EmitIlSpanField(System.Reflection.Metadata.ILOpCode op, INamedTypeSymbol span, bool isReference, Stack<IlEntry> stack)
    {
        var element = span.TypeArguments[0];
        var value = op == System.Reflection.Metadata.ILOpCode.Stfld ? stack.Pop() : null;
        var instance = stack.Pop();
        var baseAddress = instance.Kind switch
        {
            IlKind.Value => Spill(instance.Value),
            IlKind.ByRef or IlKind.Pointer or IlKind.NativeInt => IlAddress(instance),
            _ => throw new IlException($"a span's field accessed through a {instance.Kind} value"),
        };
        var address = isReference ? baseAddress : _b.InBoundsGep(L.I8, baseAddress, [L.ConstInt(L.I64, (long)T.PointerSize)]);
        var lengthType = _m.Compilation.GetSpecialType(SpecialType.System_Int32);
        switch (op)
        {
            case System.Reflection.Metadata.ILOpCode.Ldflda when isReference:
                throw new IlException($"the address of {span}._reference");
            case System.Reflection.Metadata.ILOpCode.Ldflda:
                stack.Push(new IlEntry(IlKind.ByRef, address, lengthType));
                break;
            case System.Reflection.Metadata.ILOpCode.Stfld:
                if (isReference) _b.Store(IlAddress(value!), address);
                else StoreValue(ToStorage(IlTyped(value!, lengthType), lengthType), address);
                break;
            default:
                stack.Push(isReference ? new IlEntry(IlKind.ByRef, _b.Load(L.Ptr, address), element) : IlPush(LoadValue(address, lengthType), lengthType));
                break;
        }
        return true;
    }

    private LLVMValueRef NullChecked(IlEntry reference)
    {
        if (!reference.NonNull) NullCheck(reference.Value);
        return reference.Value;
    }
}
