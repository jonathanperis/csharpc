// Vector128<T> through generic code constrained to IEquatable<T> (the way CoreLib's Ordinal.EqualsIgnoreCase_Vector<TVector> compares
// vectors): the constrained Equals is Vector128's own lane-wise equality.
using System;
using System.Runtime.Intrinsics;

class Program
{
    static bool Same<T>(T a, T b) where T : IEquatable<T> => a.Equals(b);

    static void Main()
    {
        var a = Vector128.Create((ushort)0x0F0F, 1, 2, 3, 4, 5, 6, 0xFFFF);
        var b = Vector128.Create((ushort)0x00FF, 0x20, 0x20, 0x20, 0, 0, 7, 1);
        Console.WriteLine(Same(a, a) + " " + Same(a, b) + " " + Same(a | Vector128<ushort>.Zero, a));
        var c = Vector128.Create(1, -2, 3, int.MinValue);
        Console.WriteLine(Same(c | c, c) + " " + Same(c, c + c));
        var d = Vector128.Create((byte)7);
        Console.WriteLine(Same(d, Vector128.Create((byte)7)) + " " + Same(d, Vector128<byte>.Zero));
    }
}
