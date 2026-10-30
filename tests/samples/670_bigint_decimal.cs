// BigInteger <-> decimal: truncation toward zero, sign, the 96-bit limit and .NET's overflow message.
using System;
using System.Numerics;

class Program
{
    static void Main()
    {
        decimal[] values = { 0m, 1.9m, -1.9m, 123456789012345678901234567.89m, decimal.MaxValue, decimal.MinValue, -0.5m };
        foreach (var d in values)
        {
            var b = (BigInteger)d;
            Console.WriteLine($"{d} -> {b} -> {(decimal)b} {new BigInteger(d) == b}");
        }
        var big = BigInteger.Pow(2, 96) - 1;
        Console.WriteLine((decimal)big);
        Console.WriteLine((decimal)(-big));
        try { Console.WriteLine((decimal)(big + 1)); }
        catch (OverflowException e) { Console.WriteLine(e.Message); }
        BigInteger[] large = { BigInteger.Pow(10, 40), BigInteger.Pow(2, 127) - 1, BigInteger.Pow(2, 127), -BigInteger.Pow(2, 127), -BigInteger.Pow(2, 127) - 1 };
        foreach (var value in large)
        {
            try { Console.WriteLine((decimal)value); }
            catch (OverflowException e) { Console.WriteLine(e.Message); }
        }
    }
}
