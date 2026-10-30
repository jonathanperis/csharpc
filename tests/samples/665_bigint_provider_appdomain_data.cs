// BigInteger.Parse/ToString with an IFormatProvider (invariant symbols) and AppDomain.GetData.
using System;
using System.Globalization;
using System.Numerics;

class Program
{
    static void Main()
    {
        var inv = CultureInfo.InvariantCulture;
        var big = BigInteger.Parse("-123456789012345678901234567890", inv);
        Console.WriteLine(big.ToString("D", inv));
        Console.WriteLine(big.ToString("N0", inv));
        Console.WriteLine(BigInteger.Parse("  +42 ", NumberFormatInfo.InvariantInfo).ToString("X", inv));
        try { BigInteger.Parse("4x2", inv); } catch (FormatException e) { Console.WriteLine(e.GetType().Name); }
        Console.WriteLine(AppDomain.CurrentDomain.GetData("csharpc.no.such.key") == null);
        AppContext.SetData("my.key", 5);
        Console.WriteLine(AppDomain.CurrentDomain.GetData("my.key"));
    }
}
