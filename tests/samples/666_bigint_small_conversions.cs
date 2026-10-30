// Explicit BigInteger conversions to sbyte, byte, short, ushort and char: checked((T)(int)value) in .NET, so
// values outside Int32 report the Int32 overflow and the others the generic arithmetic overflow.
using System;
using System.Numerics;

class Program
{
    static void T(string what, Func<object> f)
    {
        try { Console.WriteLine($"{what} -> {f()}"); }
        catch (OverflowException e) { Console.WriteLine($"{what} !! {e.Message}"); }
    }

    static void Main()
    {
        BigInteger small = -100, mid = 200, big = 70000, huge = BigInteger.Pow(2, 40);
        T("sbyte -100", () => (sbyte)small);
        T("sbyte 200", () => (sbyte)mid);
        T("byte 200", () => (byte)mid);
        T("byte -100", () => (byte)small);
        T("short 70000", () => (short)big);
        T("ushort 70000", () => (ushort)big);
        T("ushort 200", () => (ushort)mid);
        T("char 200", () => (int)(char)mid);
        T("short 2^40", () => (short)huge);
        T("byte 2^40", () => (byte)huge);
    }
}
