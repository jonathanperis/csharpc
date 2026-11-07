#:property AllowUnsafeBlocks=true
// Marshal.StructureToPtr / PtrToStructure / DestroyStructure between structs, classes and native memory:
// sequential, packed and explicit layouts, bool as BOOL / U1, nested structs, enums, null arguments.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
struct Point { public int X; public int Y; }

[StructLayout(LayoutKind.Sequential)]
struct Mixed { public byte Tag; public int Count; public short Extra; public long Big; }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Packed { public byte Tag; public int Count; public short Extra; }

[StructLayout(LayoutKind.Explicit)]
struct Overlay { [FieldOffset(0)] public int Whole; [FieldOffset(0)] public short Low; [FieldOffset(2)] public short High; [FieldOffset(8)] public long Tail; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct WideText { public int Id; public double Value; public bool Flag; public char Letter; }

[StructLayout(LayoutKind.Sequential)]
struct Flags
{
    public bool Default;
    [MarshalAs(UnmanagedType.U1)] public bool Byte;
    public Color Shade;
    public Point Where;
}

[StructLayout(LayoutKind.Sequential)]
struct Padded { public byte A; public int B; public bool F; }

[StructLayout(LayoutKind.Sequential)]
struct Blit { public byte A; public int B; }

[StructLayout(LayoutKind.Sequential)]
sealed class PaddedClass { public byte A; public int B; public bool F; }

enum Color : short { Red, Green }

struct Generic<T> { public T Value; public int Other; }

[StructLayout(LayoutKind.Sequential)]
sealed class ClassPoint { public int X; public int Y; }

unsafe class P
{
    static void Try(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Structs()
    {
        IntPtr mem = Marshal.AllocHGlobal(64);
        for (int i = 0; i < 64; i++) Marshal.WriteByte(mem, i, 0);
        var m = new Mixed { Tag = 9, Count = 123456, Extra = -5, Big = 1L << 40 };
        Marshal.StructureToPtr(m, mem, false);
        Console.WriteLine($"{Marshal.ReadByte(mem, 0)} {Marshal.ReadInt32(mem, 4)} {Marshal.ReadInt16(mem, 8)} {Marshal.ReadInt64(mem, 16)}");
        Mixed back = Marshal.PtrToStructure<Mixed>(mem);
        Console.WriteLine($"{back.Tag} {back.Count} {back.Extra} {back.Big}");
        Mixed back2 = (Mixed)Marshal.PtrToStructure(mem, typeof(Mixed));
        Console.WriteLine($"{back2.Tag} {back2.Count} {back2.Extra} {back2.Big}");
        Marshal.StructureToPtr((object)m, mem + 32, false);
        Console.WriteLine($"{Marshal.ReadByte(mem, 32)} {Marshal.ReadInt32(mem, 36)}");
        var cp = new ClassPoint { X = 1, Y = 2 };
        Marshal.StructureToPtr(cp, mem, false);
        Console.WriteLine($"{Marshal.ReadInt32(mem, 0)} {Marshal.ReadInt32(mem, 4)}");
        Marshal.WriteInt32(mem, 0, 77);
        Marshal.WriteInt32(mem, 4, 88);
        var cp2 = Marshal.PtrToStructure<ClassPoint>(mem);
        Console.WriteLine($"{cp2.X} {cp2.Y}");
        var cp3 = new ClassPoint();
        Marshal.PtrToStructure(mem, cp3);
        Console.WriteLine($"{cp3.X} {cp3.Y}");
        var cp4 = (ClassPoint)Marshal.PtrToStructure(mem, typeof(ClassPoint));
        Console.WriteLine($"{cp4.X} {cp4.Y} {ReferenceEquals(cp2, cp4)}");
        var wf = new WideText { Id = 5, Value = 2.5, Flag = true, Letter = 'z' };
        Marshal.StructureToPtr(wf, mem, true);
        var wf2 = Marshal.PtrToStructure<WideText>(mem);
        Console.WriteLine($"{wf2.Id} {wf2.Value} {wf2.Flag} {wf2.Letter} {Marshal.SizeOf<WideText>()} {Marshal.ReadInt32(mem, 16)} {Marshal.ReadInt16(mem, 20)}");
        Marshal.DestroyStructure<WideText>(mem);
        var fl = new Flags { Default = true, Byte = true, Shade = Color.Green, Where = new Point { X = 3, Y = 4 } };
        Marshal.StructureToPtr(fl, mem, false);
        Console.WriteLine($"{Marshal.ReadInt32(mem, 0)} {Marshal.ReadByte(mem, 4)} {Marshal.ReadInt16(mem, 8)} {Marshal.ReadInt32(mem, 12)} {Marshal.ReadInt32(mem, 16)}");
        Marshal.WriteInt32(mem, 0, 7);
        Marshal.WriteByte(mem, 4, 0);
        var fl2 = Marshal.PtrToStructure<Flags>(mem);
        Console.WriteLine($"{fl2.Default} {fl2.Byte} {fl2.Shade} {fl2.Where.X} {fl2.Where.Y}");
        var pk = new Packed { Tag = 1, Count = 2, Extra = 3 };
        Marshal.StructureToPtr(pk, mem, false);
        Console.WriteLine($"{Marshal.ReadByte(mem, 0)} {Marshal.ReadInt32(mem, 1)} {Marshal.ReadInt16(mem, 5)}");
        var pk2 = Marshal.PtrToStructure<Packed>(mem);
        Console.WriteLine($"{pk2.Tag} {pk2.Count} {pk2.Extra}");
        var ov = new Overlay { Whole = 0x00020001, Tail = 5 };
        Marshal.StructureToPtr(ov, mem, false);
        var ov2 = Marshal.PtrToStructure<Overlay>(mem);
        Console.WriteLine($"{ov2.Whole:X} {ov2.Low} {ov2.High} {ov2.Tail}");
        Console.WriteLine(Marshal.PtrToStructure<int>(mem));
        Marshal.StructureToPtr(1234, mem, false);
        Console.WriteLine(Marshal.ReadInt32(mem));
        Try("null ptr", () => Marshal.PtrToStructure<Point>(IntPtr.Zero));
        Try("null ptr value", () => Console.WriteLine(Marshal.PtrToStructure<Point>(IntPtr.Zero).X));
        Console.WriteLine(Marshal.PtrToStructure<ClassPoint>(IntPtr.Zero) is null);
        Console.WriteLine(Marshal.PtrToStructure(IntPtr.Zero, typeof(Point)) is null);
        Try("StructureToPtr null", () => Marshal.StructureToPtr((object)null, mem, false));
        Try("StructureToPtr null ptr", () => Marshal.StructureToPtr(new Point(), IntPtr.Zero, false));
        Try("PtrToStructure null structure", () => Marshal.PtrToStructure(mem, (object)null));
        Try("StructureToPtr generic", () => Marshal.StructureToPtr(new Generic<int> { Value = 5, Other = 6 }, mem, false));
        Try("PtrToStructure<generic>", () => Marshal.PtrToStructure<Generic<int>>(mem));
        Try("PtrToStructure generic type", () => Marshal.PtrToStructure(mem, typeof(Generic<int>)));
        Try("DestroyStructure generic", () => Marshal.DestroyStructure<Generic<int>>(mem));
        Try("DestroyStructure generic type", () => Marshal.DestroyStructure(mem, typeof(Generic<int>)));
        Try("DestroyStructure null ptr", () => Marshal.DestroyStructure<Point>(IntPtr.Zero));
        Try("DestroyStructure null type", () => Marshal.DestroyStructure(mem, null));
        Try("PtrToStructure null type", () => Marshal.PtrToStructure(mem, null));
        Marshal.WriteInt32(mem, 0, 1);
        Console.WriteLine(Marshal.PtrToStructure<bool>(mem));
        Console.WriteLine(Marshal.PtrToStructure<Color>(mem));
        Marshal.FreeHGlobal(mem);
    }

    static void Dump(IntPtr m, string what)
    {
        Console.Write(what + ":");
        for (int i = 0; i < 16; i++) Console.Write(" " + Marshal.ReadByte(m, i).ToString("X2"));
        Console.WriteLine();
    }

    static void Fill(IntPtr m) { for (int i = 0; i < 32; i++) Marshal.WriteByte(m, i, 0xFF); }

    // Converted layouts (bool) are cleared before the fields are written and by DestroyStructure; blittable ones are copied as they are.
    static void Padding()
    {
        IntPtr m = Marshal.AllocHGlobal(32);
        Fill(m); Marshal.StructureToPtr(new Blit { A = 1, B = 2 }, m, false); Dump(m, "blittable");
        Fill(m); Marshal.DestroyStructure<Blit>(m); Dump(m, "blittable destroy");
        Fill(m); Marshal.StructureToPtr(new Padded { A = 1, B = 2, F = true }, m, false); Dump(m, "converted");
        Fill(m); Marshal.StructureToPtr(new Padded { A = 1, B = 2, F = true }, m, true); Dump(m, "converted, fDeleteOld");
        Fill(m); Marshal.StructureToPtr(new PaddedClass { A = 1, B = 2, F = true }, m, false); Dump(m, "converted class");
        Fill(m); Marshal.DestroyStructure<PaddedClass>(m); Dump(m, "converted class destroy");
        Fill(m); Marshal.WriteInt32(m, 0, 0); Marshal.WriteInt32(m, 4, 0x01010101); Marshal.WriteInt32(m, 8, 0);
        var padded = Marshal.PtrToStructure<Padded>(m);
        Console.WriteLine($"{padded.A} {padded.B} {padded.F}");
        Fill(m); Marshal.WriteInt32(m, 8, 256);
        Console.WriteLine(Marshal.PtrToStructure<Padded>(m).F);
        Marshal.FreeHGlobal(m);
    }

    static void Main()
    {
        Structs();
        Padding();
    }
}
