using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Private fields of built-in types in CoreLib IL (#393, #405): the generic-math, conversion and formatting code served from the
/// framework's IL reads and writes Int128's halves (_lower, _upper) and decimal's parts (_flags, _hi32, _lo64), which the reference pack
/// doesn't declare. csharpc keeps those types in .NET's own layout (Int128 as a little-endian LLVM i128, decimal as { int, uint, ulong }:
/// TypeMapper), so a field access is a load or store of the field's type at its offset in the value; ldflda stays inside the value.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>ldfld / ldflda / stfld of such a field; false when the token is any other field.</summary>
    private bool TryEmitIlBuiltinPrivateField(System.Reflection.Metadata.ILOpCode op, int token, Stack<IlEntry> stack)
    {
        if (!IlAssembly.IsBuiltinPrivateField(token, IlBody.Context, out var offset, out var special)) return false;
        var fieldType = Special(special);
        var value = op == System.Reflection.Metadata.ILOpCode.Stfld ? stack.Pop() : null;
        var instance = stack.Pop();
        var baseAddress = instance.Kind switch
        {
            IlKind.Value => Spill(instance.Value),
            IlKind.ByRef or IlKind.Pointer or IlKind.NativeInt => IlAddress(instance),
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
}
