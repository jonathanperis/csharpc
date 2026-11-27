// Generic code over T : IShiftOperators<T, int, T> instantiated with byte, sbyte, short and ushort (what CoreLib's
// Number.BinaryParser<T> does for hex and binary parsing): the count is masked to the type's width and the shift
// happens in the type, as their own operators do.
using System;
using System.Numerics;

class Program
{
    static T Shl<T>(T v, int n) where T : IShiftOperators<T, int, T> => v << n;
    static T Shr<T>(T v, int n) where T : IShiftOperators<T, int, T> => v >> n;
    static T Ushr<T>(T v, int n) where T : IShiftOperators<T, int, T> => v >>> n;

    static void Show<T>(T v) where T : IShiftOperators<T, int, T>
    {
        foreach (var n in new[] { 0, 1, 3, 7, 8, 9, 15, 16, 17, 31, 33, -1 })
            Console.Write($"{n}:{Shl(v, n)}/{Shr(v, n)}/{Ushr(v, n)} ");
        Console.WriteLine();
    }

    static void Main()
    {
        Show((sbyte)-77);
        Show((sbyte)93);
        Show((byte)0xB5);
        Show((short)-12345);
        Show((ushort)0xC0DE);
        Show(-123456789);
        Show(-1234567890123L);
    }
}
