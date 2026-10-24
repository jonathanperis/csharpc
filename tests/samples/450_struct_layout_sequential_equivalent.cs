#:property AllowUnsafeBlocks=true
// Structs whose [StructLayout]/[FieldOffset] place every field where the sequential layout does
// (Roslyn's ExtendedSpecialType: Explicit, Size = 1, one byte at offset 0) are accepted; the layout
// is observable through sizeof and reinterpreting casts, so it must be exactly .NET's.
using System.Runtime.InteropServices;

unsafe
{
    var special = new Special(7);
    Console.WriteLine($"{sizeof(Special)} {special} {*(byte*)&special}");

    var pair = new Pair { Low = 0x11223344, High = 0x55667788 };
    Console.WriteLine($"{sizeof(Pair)} {*(long*)&pair:X16}");

    var packed = new Packed { A = 1, B = 2, C = 3 };
    Console.WriteLine($"{sizeof(Packed)} {packed.A + packed.B + packed.C}");

    var sized = new Sized { X = 5, Y = 6 };
    Console.WriteLine($"{sizeof(Sized)} {sized.X * sized.Y}");

    Console.WriteLine($"{sizeof(Empty)}");

    // As dictionary keys and in arrays: equality and hashing see the same fields.
    var counts = new Dictionary<Special, int>();
    foreach (var b in new byte[] { 1, 2, 1, 3, 1 })
        counts[new Special(b)] = counts.TryGetValue(new Special(b), out var n) ? n + 1 : 1;
    foreach (var (key, count) in counts) Console.WriteLine($"{key}: {count}");
    var array = new[] { new Pair { Low = 1, High = 2 }, new Pair { Low = 3, High = 4 } };
    Console.WriteLine(array[1].Low + array[0].High);
}

[StructLayout(LayoutKind.Explicit, Size = 1)]
readonly struct Special : IEquatable<Special>
{
    [FieldOffset(0)] private readonly byte _value;
    public Special(byte value) => _value = value;
    public bool Equals(Special other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Special other && Equals(other);
    public override int GetHashCode() => _value;
    public override string ToString() => $"Special({_value})";
}

[StructLayout(LayoutKind.Explicit)]
struct Pair
{
    [FieldOffset(0)] public int Low;
    [FieldOffset(4)] public int High;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
struct Packed
{
    public byte A;
    public int B;
    public long C;
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
struct Sized
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential, Size = 1)]
struct Empty
{
}
