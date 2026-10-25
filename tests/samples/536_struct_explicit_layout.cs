#:property AllowUnsafeBlocks=true
// [StructLayout(LayoutKind.Explicit)] structs: unions of primitives at offset 0 and explicit,
// non-overlapping offsets with gaps (docs-standard-native-interop-marshalling-data-with-platform-invoke).
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

unsafe
{
    var u = new MyUnion { d = 1.5 };
    Console.WriteLine($"MyUnion: size {sizeof(MyUnion)}, d = {u.d}, i = 0x{u.i:X8}");
    u.i = 0;
    Console.WriteLine($"after i = 0: d = {u.d}, bits 0x{BitConverter.DoubleToInt64Bits(u.d):X16}");

    var b = new Bytes { Value = 0x11223344 };
    Console.WriteLine($"Bytes: size {sizeof(Bytes)}, low byte 0x{b.Low:X2}, low short 0x{b.LowShort:X4}");
    b.Low = 0xFF;
    Console.WriteLine($"Bytes after Low = 0xFF: 0x{b.Value:X8}");
    var copy = b;
    copy.Value = 1;
    Console.WriteLine($"copies are values: 0x{b.Value:X8} 0x{copy.Value:X8}");

    var g = new Gapped { A = 7, B = 0x0102030405060708, C = 3 };
    byte* p = (byte*)&g;
    Console.WriteLine($"Gapped: size {sizeof(Gapped)} = {Unsafe.SizeOf<Gapped>()}, A at {(byte*)&g.A - p}, B at {(byte*)&g.B - p}, C at {(byte*)&g.C - p}");
    Console.WriteLine($"Gapped values: {g.A} 0x{g.B:X16} {g.C}");
    Console.WriteLine($"Sized: size {sizeof(Sized)}");

    Gapped[] array = { g, new Gapped { A = 1, B = 2, C = 3 } };
    Console.WriteLine($"array: {array[0].C} {array[1].A} {array[1].B} {array[1].C}");
    var boxed = (object)array[1];
    Console.WriteLine($"boxed: {((Gapped)boxed).B} {boxed.GetType().Name}");
    Console.WriteLine(Describe(u));
}

static string Describe(MyUnion value) => $"by value: {value.i}";

// Declared like the docs' P/Invoke sample; never called.
static class NativeMethods
{
    [DllImport("PinvokeLib.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void TestUnion(MyUnion u, int type);
}

[StructLayout(LayoutKind.Explicit)]
internal struct MyUnion
{
    [FieldOffset(0)] public int i;
    [FieldOffset(0)] public double d;
}

[StructLayout(LayoutKind.Explicit)]
internal struct Bytes
{
    [FieldOffset(0)] public int Value;
    [FieldOffset(0)] public byte Low;
    [FieldOffset(0)] public short LowShort;
}

[StructLayout(LayoutKind.Explicit)]
internal struct Gapped
{
    [FieldOffset(0)] public int A;
    [FieldOffset(8)] public long B;
    [FieldOffset(20)] public short C;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct Sized
{
    [FieldOffset(4)] public int X;
}
