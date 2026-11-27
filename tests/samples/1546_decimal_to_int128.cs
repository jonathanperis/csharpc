// decimal -> Int128 / UInt128 (#182): truncation toward zero, the full 96-bit range, and OverflowException for a negative UInt128.
using System;

static class Program
{
    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine($"{label}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static T Generic<T>(decimal d) where T : System.Numerics.INumberBase<T> => T.CreateChecked(d);

    static void Main()
    {
        Try("int128 neg", () => (Int128)(-123456789012.75m));
        Try("int128 max decimal", () => (Int128)decimal.MaxValue);
        Try("int128 min decimal", () => (Int128)decimal.MinValue);
        Try("int128 small", () => (Int128)0.999m);
        Try("int128 neg small", () => (Int128)(-0.999m));
        Try("uint128", () => (UInt128)12345.9m);
        Try("uint128 max decimal", () => (UInt128)decimal.MaxValue);
        Try("uint128 neg fraction", () => (UInt128)(-0.5m));
        Try("uint128 neg", () => (UInt128)(-1m));
        Try("round trip", () => (decimal)(Int128)(-77m));
        Try("generic int128", () => Generic<Int128>(-42.42m));
        Try("generic uint128", () => Generic<UInt128>(42.42m));
    }
}
