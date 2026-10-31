#:property AllowUnsafeBlocks=true
// Struct layouts LLVM's struct types can't express, placed where the CLR places them: explicit fields
// that overlap partially (Roslyn's DiaSymReader VariantStructure: a decimal at 0 over a long at 8) and
// sequential layouts with Pack below a field's alignment (Pack = 1, Pack = 2).
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

unsafe
{
    var v = new Variant { Type = 7, Long = 0x0102030405060708 };
    Console.WriteLine($"Variant: size {sizeof(Variant)} = {Unsafe.SizeOf<Variant>()}, type {v.Type}, long 0x{v.Long:X16}, int 0x{v.Int:X8}");
    var flag = new Variant { Long = 1 };
    Console.WriteLine($"bool over a long of 1: {flag.Bool}, then {(flag.Bool = false) || flag.Long == 0}");
    byte* vp = (byte*)&v;
    Console.WriteLine($"Variant offsets: Type {(byte*)&v.Type - vp}, Long {(byte*)&v.Long - vp}, Padding {(byte*)&v.Padding - vp}, Wide {(byte*)&v.Wide - vp}");
    v.Double = 2.5;
    Console.WriteLine($"after Double = 2.5: long 0x{v.Long:X16}, type {v.Type}, padding {v.Padding.A:X} {v.Padding.B:X}");
    v.Wide = new Pair { A = -1, B = 0x11 };
    Console.WriteLine($"after Wide: type 0x{(ushort)v.Type:X4}, long 0x{v.Long:X16}, pointer 0x{(long)v.Ptrs.P:X}");

    var p = new Packed1 { A = 0xAB, B = 0x1122334455667788, C = 0x99 };
    byte* pp = (byte*)&p;
    Console.WriteLine($"Packed1: size {sizeof(Packed1)}, A at {(byte*)&p.A - pp}, B at {(byte*)&p.B - pp}, C at {(byte*)&p.C - pp}, values 0x{p.A:X2} 0x{p.B:X16} 0x{p.C:X2}");
    Console.WriteLine($"Packed1 bytes: {Convert.ToHexString(new ReadOnlySpan<byte>(pp, sizeof(Packed1)))}");

    var q = new Packed2 { A = 1, B = 2, C = 3 };
    byte* qp = (byte*)&q;
    Console.WriteLine($"Packed2: size {sizeof(Packed2)}, A at {(byte*)&q.A - qp}, B at {(byte*)&q.B - qp}, C at {(byte*)&q.C - qp}, sum {q.A + q.B + q.C}");

    var outer = new Outer { Before = 1, Inner = p, After = 2 };
    byte* op = (byte*)&outer;
    Console.WriteLine($"Outer: size {sizeof(Outer)}, Inner at {(byte*)&outer.Inner - op}, After at {(byte*)&outer.After - op}, inner B 0x{outer.Inner.B:X16}");

    Packed1[] array = { p, new Packed1 { A = 1, B = 2, C = 3 } };
    Console.WriteLine($"array: {array[1].A} {array[1].B} {array[1].C} {array[0].C:X2}");
    object boxed = array[1];
    var back = (Packed1)boxed;
    Console.WriteLine($"boxed: {back.B} {boxed.GetType().Name} {back.Equals(array[1])} {back.Equals(array[0])}");
    var copy = v;
    copy.Long = 5;
    Console.WriteLine($"copies are values: 0x{v.Long:X16} {copy.Long}");
}

[StructLayout(LayoutKind.Explicit)]
struct Variant
{
    [FieldOffset(0)] public short Type;
    [FieldOffset(8)] public long Long;
    [FieldOffset(8)] public Pair Padding;
    [FieldOffset(0)] public Pair Wide;
    [FieldOffset(8)] public bool Bool;
    [FieldOffset(8)] public int Int;
    [FieldOffset(8)] public double Double;
    [FieldOffset(8)] public Pointers Ptrs; // pointers in an overlay hold no references (DiaSymReader VariantPadding)
}

struct Pair { public long A; public long B; }

unsafe struct Pointers { public byte* P; public byte* Q; }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Packed1 { public byte A; public long B; public byte C; }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
struct Packed2 { public byte A; public int B; public short C; }

struct Outer { public byte Before; public Packed1 Inner; public byte After; }
