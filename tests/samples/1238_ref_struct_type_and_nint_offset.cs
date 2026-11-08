#:property AllowUnsafeBlocks=true
// Two latent miscompiles the Roslyn survey's complete run exposed (invalid IR, #169): a ref struct with a span field gets a
// method table (typeof, reflection) although its field-wise Equals/GetHashCode cannot exist, and IntPtr + int (netstandard
// libraries call IntPtr.op_Addition(IntPtr, int)) must widen the offset to the native width.
using System;
using System.Reflection;

var buffer = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
var reader = new LittleEndianReader(buffer);
Console.WriteLine(reader.ReadUInt16() + " " + reader.ReadUInt32() + " " + reader.ReadByte());
Console.WriteLine(typeof(LittleEndianReader).Name + " " + typeof(LittleEndianReader).IsValueType + " " + typeof(LittleEndianReader).IsByRefLike);
Console.WriteLine(typeof(LittleEndianReader).GetMethod("ReadByte") != null);

unsafe
{
    fixed (byte* p = buffer)
    {
        IntPtr start = (IntPtr)p;
        int offset = 5;
        IntPtr moved = (IntPtr)((byte*)start + offset);
        IntPtr again = start + offset;
        IntPtr back = again - 2;
        Console.WriteLine(*(byte*)moved + " " + *(byte*)again + " " + *(byte*)back + " " + (moved == again) + " " + (back - start));
        UIntPtr u = (UIntPtr)p;
        Console.WriteLine(*(byte*)(u + 3) + " " + *(byte*)(u + 6));
    }
}

ref struct LittleEndianReader
{
    private ReadOnlySpan<byte> _span;
    public LittleEndianReader(ReadOnlySpan<byte> span) { _span = span; }
    public byte ReadByte() { byte b = _span[0]; _span = _span.Slice(1); return b; }
    public ushort ReadUInt16() { ushort v = (ushort)(_span[0] | _span[1] << 8); _span = _span.Slice(2); return v; }
    public uint ReadUInt32() { uint v = (uint)(_span[0] | _span[1] << 8 | _span[2] << 16 | _span[3] << 24); _span = _span.Slice(4); return v; }
}
