// Marshal.SizeOf and OffsetOf: the unmanaged layout (sequential, Pack, explicit, bool as BOOL / U1,
// ANSI char as one byte, nested structs and enums), and the ArgumentExceptions for what has no unmanaged size.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
struct Point { public int X; public int Y; }

[StructLayout(LayoutKind.Sequential)]
struct Mixed { public byte Tag; public int Count; public short Extra; public long Big; }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Packed { public byte Tag; public int Count; public short Extra; }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
struct PackedTwo { public byte Tag; public long Wide; public byte Last; }

[StructLayout(LayoutKind.Explicit)]
struct Overlay { [FieldOffset(0)] public int Whole; [FieldOffset(0)] public short Low; [FieldOffset(2)] public short High; [FieldOffset(8)] public long Tail; }

[StructLayout(LayoutKind.Sequential)]
struct WithFixed { public int Id; public double Value; public bool Flag; public char Letter; }

[StructLayout(LayoutKind.Sequential)]
struct Flags
{
    public bool Default;
    [MarshalAs(UnmanagedType.U1)] public bool Byte;
    [MarshalAs(UnmanagedType.Bool)] public bool Win32;
    public Color Shade;
    public Point Where;
}

enum Color : short { Red, Green }

[StructLayout(LayoutKind.Sequential)]
struct Empty { }

[StructLayout(LayoutKind.Sequential)]
sealed class ClassPoint { public int X; public int Y; }

class AutoClass { public int X; }

struct Generic<T> { public T Value; public int Other; }

[StructLayout(LayoutKind.Sequential)]
struct VariantFlag { [MarshalAs(UnmanagedType.VariantBool)] public bool Flag; }

class P
{
    static void Try(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Sizes()
    {
        Console.WriteLine(Marshal.SizeOf<Point>());
        Console.WriteLine(Marshal.SizeOf(typeof(Point)));
        Console.WriteLine(Marshal.SizeOf(new Point()));
        Console.WriteLine(Marshal.SizeOf<Point>(default));
        Console.WriteLine(Marshal.SizeOf<Mixed>());
        Console.WriteLine(Marshal.SizeOf<Packed>());
        Console.WriteLine(Marshal.SizeOf<PackedTwo>());
        Console.WriteLine(Marshal.SizeOf<Overlay>());
        Console.WriteLine(Marshal.SizeOf<WithFixed>());
        Console.WriteLine(Marshal.SizeOf<Flags>());
        Console.WriteLine(Marshal.SizeOf<Empty>());
        Console.WriteLine(Marshal.SizeOf<int>());
        Console.WriteLine(Marshal.SizeOf<double>());
        Console.WriteLine(Marshal.SizeOf<byte>());
        Console.WriteLine(Marshal.SizeOf<bool>());
        Console.WriteLine(Marshal.SizeOf<char>());
        Console.WriteLine(Marshal.SizeOf(typeof(ClassPoint)));
        Console.WriteLine(Marshal.SizeOf(new ClassPoint()));
        Console.WriteLine(Marshal.SizeOf<Guid>());
        Console.WriteLine(Marshal.SizeOf<decimal>());
        Try("enum", () => Marshal.SizeOf(typeof(Color)));
        Try("string", () => Marshal.SizeOf<string>());
        Try("object", () => Marshal.SizeOf(typeof(object)));
        Try("array", () => Marshal.SizeOf<int[]>());
        Try("auto layout class", () => Marshal.SizeOf(typeof(AutoClass)));
        Try("generic", () => Marshal.SizeOf(typeof(System.Collections.Generic.List<int>)));
        Try("generic struct", () => Marshal.SizeOf<Generic<int>>());
        Try("null", () => Marshal.SizeOf((object)null));
        Try("null type", () => Marshal.SizeOf((Type)null));
        Console.WriteLine(Marshal.SizeOf((object)new Generic<int>()));
        Console.WriteLine(Marshal.SizeOf(new Generic<long>()));
    }

    static void Offsets()
    {
        Console.WriteLine(Marshal.OffsetOf<Mixed>("Tag"));
        Console.WriteLine(Marshal.OffsetOf<Mixed>("Count"));
        Console.WriteLine(Marshal.OffsetOf<Mixed>("Extra"));
        Console.WriteLine(Marshal.OffsetOf<Mixed>("Big"));
        Console.WriteLine(Marshal.OffsetOf(typeof(Packed), "Count"));
        Console.WriteLine(Marshal.OffsetOf(typeof(Packed), "Extra"));
        Console.WriteLine(Marshal.OffsetOf(typeof(PackedTwo), "Wide"));
        Console.WriteLine(Marshal.OffsetOf(typeof(PackedTwo), "Last"));
        Console.WriteLine(Marshal.OffsetOf(typeof(Overlay), "Tail"));
        Console.WriteLine(Marshal.OffsetOf(typeof(Overlay), "High"));
        Console.WriteLine(Marshal.OffsetOf<WithFixed>("Flag"));
        Console.WriteLine(Marshal.OffsetOf<WithFixed>("Letter"));
        Console.WriteLine(Marshal.OffsetOf<Flags>("Byte"));
        Console.WriteLine(Marshal.OffsetOf<Flags>("Win32"));
        Console.WriteLine(Marshal.OffsetOf<Flags>("Shade"));
        Console.WriteLine(Marshal.OffsetOf<Flags>("Where"));
        Console.WriteLine(Marshal.OffsetOf<ClassPoint>("Y"));
        Try("missing", () => Marshal.OffsetOf<Mixed>("Nope"));
        Try("null name", () => Marshal.OffsetOf(typeof(Mixed), null));
        Console.WriteLine(Marshal.OffsetOf<Generic<int>>("Other"));
        Console.WriteLine(Marshal.OffsetOf(typeof(Generic<long>), "Other"));
        Try("null type", () => Marshal.OffsetOf((Type)null, "x"));
        Try("variant bool", () => Marshal.SizeOf<VariantFlag>());
    }

    static void Main()
    {
        Sizes();
        Offsets();
    }
}
