using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Csharpc.Frontend.Il;

/// <summary>One decoded IL instruction. Branch targets are absolute offsets.</summary>
public readonly record struct IlInstruction(int Offset, ILOpCode OpCode, int Size, long Int = 0, double Real = 0, int Token = 0, int[]? Targets = null)
{
    public int Next => Offset + Size;
    public int Target => Targets![0];
    public override string ToString() => $"IL_{Offset:X4}: {Name(OpCode)}{Operand()}";

    private string Operand() => OpCode switch
    {
        _ when Targets is { Length: 1 } t && OpCode != ILOpCode.Switch => $" IL_{t[0]:X4}",
        ILOpCode.Switch => " (" + string.Join(", ", Targets!.Select(t => $"IL_{t:X4}")) + ")",
        _ when Token != 0 => $" 0x{Token:X8}",
        ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8 => $" {Real}",
        _ when IlDecoder.HasIntOperand(OpCode) => $" {Int}",
        _ => "",
    };

    /// <summary>The ILAsm spelling: Ldc_i4_s -> ldc.i4.s.</summary>
    public static string Name(ILOpCode op) => op.ToString().ToLowerInvariant().Replace('_', '.');
}

/// <summary>An IL method body, decoded once: instructions, exception regions, local types.</summary>
public sealed class IlMethodBody
{
    public required IMethodSymbol Method { get; init; }
    public required IlAssembly Assembly { get; init; }
    public required ImmutableArray<IlInstruction> Instructions { get; init; }
    public required ImmutableArray<ExceptionRegion> Regions { get; init; }
    public required ImmutableArray<IlType> Locals { get; init; }
    public required IlGenericContext Context { get; init; }
    public required bool InitLocals { get; init; }

    public static IlMethodBody Decode(IlAssembly assembly, IMethodSymbol method, MethodBodyBlock block)
    {
        var context = IlGenericContext.For(method);
        return new IlMethodBody
        {
            Method = method,
            Assembly = assembly,
            Instructions = IlDecoder.Decode(block.GetILReader()),
            Regions = block.ExceptionRegions,
            Locals = assembly.DecodeLocals(block, context),
            Context = context,
            InitLocals = block.LocalVariablesInitialized,
        };
    }

    /// <summary>A readable listing (--dump-il).</summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Locals.Length; i++) sb.AppendLine($"    .local [{i}] {Locals[i]}");
        foreach (var r in Regions)
            sb.AppendLine($"    .try IL_{r.TryOffset:X4}-IL_{r.TryOffset + r.TryLength:X4} {r.Kind.ToString().ToLowerInvariant()}"
                + (r.Kind == ExceptionRegionKind.Filter ? $" IL_{r.FilterOffset:X4}" : "")
                + $" handler IL_{r.HandlerOffset:X4}-IL_{r.HandlerOffset + r.HandlerLength:X4}");
        foreach (var instruction in Instructions) sb.AppendLine("    " + instruction);
        return sb.ToString();
    }
}

/// <summary>Decodes the IL byte stream (ECMA-335 III) into instructions.</summary>
public static class IlDecoder
{
    private enum Operand { None, I1, I4, I8, R4, R8, U1, U2, Br1, Br4, Token, Switch }

    public static ImmutableArray<IlInstruction> Decode(BlobReader il)
    {
        var result = ImmutableArray.CreateBuilder<IlInstruction>();
        while (il.RemainingBytes > 0)
        {
            var offset = il.Offset;
            int code = il.ReadByte();
            if (code == 0xFE)
            {
                if (il.RemainingBytes == 0) throw new IlException($"IL_{offset:X4}: truncated two-byte opcode");
                code = 0xFE00 | il.ReadByte();
            }
            var op = (ILOpCode)code;
            IlInstruction instruction;
            switch (OperandOf(op, offset))
            {
                case Operand.None: instruction = new(offset, op, il.Offset - offset); break;
                case Operand.I1: { long v = il.ReadSByte(); instruction = new(offset, op, il.Offset - offset, Int: v); break; }
                case Operand.U1: { long v = il.ReadByte(); instruction = new(offset, op, il.Offset - offset, Int: v); break; }
                case Operand.U2: { long v = il.ReadUInt16(); instruction = new(offset, op, il.Offset - offset, Int: v); break; }
                case Operand.I4: { long v = il.ReadInt32(); instruction = new(offset, op, il.Offset - offset, Int: v); break; }
                case Operand.I8: { var v = il.ReadInt64(); instruction = new(offset, op, il.Offset - offset, Int: v); break; }
                case Operand.R4: { double v = il.ReadSingle(); instruction = new(offset, op, il.Offset - offset, Real: v); break; }
                case Operand.R8: { var v = il.ReadDouble(); instruction = new(offset, op, il.Offset - offset, Real: v); break; }
                case Operand.Token: { var v = il.ReadInt32(); instruction = new(offset, op, il.Offset - offset, Token: v); break; }
                case Operand.Br1:
                {
                    int delta = il.ReadSByte();
                    instruction = new(offset, op, il.Offset - offset, Targets: [il.Offset + delta]);
                    break;
                }
                case Operand.Br4:
                {
                    var delta = il.ReadInt32();
                    instruction = new(offset, op, il.Offset - offset, Targets: [il.Offset + delta]);
                    break;
                }
                case Operand.Switch:
                {
                    var count = il.ReadUInt32();
                    if (count > (uint)il.RemainingBytes / 4) throw new IlException($"IL_{offset:X4}: switch table runs past the end of the body");
                    var deltas = new int[count];
                    for (var i = 0; i < count; i++) deltas[i] = il.ReadInt32();
                    var end = il.Offset;
                    instruction = new(offset, op, end - offset, Targets: [.. deltas.Select(d => end + d)]);
                    break;
                }
                default: throw new InvalidOperationException();
            }
            result.Add(instruction);
        }
        return result.ToImmutable();
    }

    internal static bool HasIntOperand(ILOpCode op) => OperandOf(op, 0) is Operand.I1 or Operand.U1 or Operand.U2 or Operand.I4 or Operand.I8;

    private static Operand OperandOf(ILOpCode op, int offset) => op switch
    {
        ILOpCode.Ldc_i4_s => Operand.I1,
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
            or ILOpCode.Unaligned => Operand.U1,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => Operand.U2,
        ILOpCode.Ldc_i4 => Operand.I4,
        ILOpCode.Ldc_i8 => Operand.I8,
        ILOpCode.Ldc_r4 => Operand.R4,
        ILOpCode.Ldc_r8 => Operand.R8,
        ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s
            or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
            or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s => Operand.Br1,
        ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt
            or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
            or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave => Operand.Br4,
        ILOpCode.Switch => Operand.Switch,
        ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Cpobj or ILOpCode.Ldobj
            or ILOpCode.Ldstr or ILOpCode.Newobj or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Unbox
            or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Ldsflda
            or ILOpCode.Stsfld or ILOpCode.Stobj or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Ldelema
            or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Unbox_any or ILOpCode.Refanyval or ILOpCode.Mkrefany
            or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Initobj or ILOpCode.Constrained
            or ILOpCode.Sizeof => Operand.Token,
        _ when Enum.IsDefined(op) => Operand.None,
        _ => throw new IlException($"IL_{offset:X4}: invalid opcode 0x{(int)op:X2}"),
    };

    /// <summary>Instructions after which control never falls through to the next one.</summary>
    public static bool EndsBlock(ILOpCode op) => op is ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s
        or ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Endfinally or ILOpCode.Endfilter or ILOpCode.Jmp;
}
