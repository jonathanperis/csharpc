using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Int128's and UInt128's <c>_lower</c> / <c>_upper</c> in CoreLib IL (#393): their generic-math members, served from the framework's IL
/// (upstream-types/System.IntegerMembers.txt), read and write the two 64-bit halves of a value that csharpc keeps as one LLVM i128. The
/// i128's little-endian memory layout is .NET's struct layout (lower half at byte 0, upper half at byte 8) on every target csharpc
/// supports, so a field access is a 64-bit access at that offset of the value's address; ldflda stays inside the value's 16 bytes.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>ldfld / ldflda / stfld of Int128._lower, UInt128._upper ...; false when the token is any other field.</summary>
    private bool TryEmitIlInt128Half(System.Reflection.Metadata.ILOpCode op, int token, Stack<IlEntry> stack)
    {
        if (!IlAssembly.IsInt128Half(token, IlBody.Context, out var upper)) return false;
        var half = Special(SpecialType.System_UInt64);
        var value = op == System.Reflection.Metadata.ILOpCode.Stfld ? stack.Pop() : null;
        var instance = stack.Pop();
        var baseAddress = instance.Kind switch
        {
            IlKind.Value => Spill(instance.Value),
            IlKind.ByRef or IlKind.Pointer or IlKind.NativeInt => IlAddress(instance),
            _ => throw new IlException($"Int128 field accessed through a {instance.Kind} value"),
        };
        var address = upper ? _b.InBoundsGep(L.I8, baseAddress, [L.ConstInt(L.I64, 8)]) : baseAddress;
        switch (op)
        {
            case System.Reflection.Metadata.ILOpCode.Ldflda:
                stack.Push(new IlEntry(IlKind.ByRef, address, half));
                break;
            case System.Reflection.Metadata.ILOpCode.Stfld:
                StoreValue(ToStorage(IlTyped(value!, half), half), address);
                break;
            default:
                stack.Push(IlPush(LoadValue(address, half), half));
                break;
        }
        return true;
    }
}
