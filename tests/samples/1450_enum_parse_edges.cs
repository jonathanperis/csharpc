// Enum.Parse / Enum.TryParse edge cases: white space around names and numbers, signs, overflow of the
// underlying type, flags lists, case-insensitive names beyond ASCII, and the exceptions .NET throws.
using System;

enum Color { Red = 1, Green = 2, Blue = 4 }
[Flags] enum Perm : byte { None = 0, Read = 1, Write = 2, Exec = 4, Ärger = 8 }
enum Small : sbyte { Low = -5, High = 100 }
enum Wide : ulong { Max = ulong.MaxValue }
enum Big : long { Min = long.MinValue }
enum Cased { Beta = 2, beta = 1 }

static class Program
{
    static void Try<T>(string text, bool ignoreCase = false) where T : struct, Enum
    {
        bool ok = Enum.TryParse<T>(text, ignoreCase, out T value);
        Console.WriteLine($"TryParse<{typeof(T).Name}>({Show(text)}, {ignoreCase}) = {ok} {value}");
        try
        {
            T parsed = Enum.Parse<T>(text, ignoreCase);
            Console.WriteLine($"  Parse = {parsed}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  Parse threw {e.GetType().Name}: {e.Message}");
        }
        try
        {
            object boxed = Enum.Parse(typeof(T), text, ignoreCase);
            Console.WriteLine($"  Parse(Type) = {boxed}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  Parse(Type) threw {e.GetType().Name}: {e.Message}");
        }
    }

    static string Show(string s) => s == null ? "null" : "\"" + s.Replace("\t", "\\t").Replace("\u00A0", "\\u00A0").Replace("\n", "\\n") + "\"";

    static void Main()
    {
        foreach (var s in new[] { "Red", "red", " Red ", "\tRed\n", "\u00A0Red\u00A0", "Red, Blue", "Red,Blue", "Red,,Blue", "Red, 2", "", "   ", "\t",
                     "5", "+5", " 5 ", "\t5", "5\t", "-1", "0x10", "1e3", "5.0", "1,000", "2147483647", "2147483648", "-2147483649", "+-1", "- 1", "1 2", "Purple" })
        {
            Try<Color>(s);
        }
        Try<Color>("RED", true);
        Try<Color>("rEd, bLUE", true);
        foreach (var s in new[] { "Read, Write", "Read|Write", "7", "255", "256", "-1", "Exec,Read", "ärger", "ÄRGER", "Ärger" })
        {
            Try<Perm>(s);
            Try<Perm>(s, true);
        }
        foreach (var s in new[] { "Low", "-5", "-128", "-129", "127", "128", "High" })
            Try<Small>(s);
        foreach (var s in new[] { "Max", "18446744073709551615", "18446744073709551616", "-1", "0" })
            Try<Wide>(s);
        foreach (var s in new[] { "Min", "-9223372036854775808", "-9223372036854775809", "9223372036854775807", "9223372036854775808" })
            Try<Big>(s);
        foreach (var s in new[] { "BETA", "Beta", "beta" })
            Try<Cased>(s, true);
        try
        {
            Enum.Parse<Color>(null);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Parse(null) threw {e.GetType().Name}: {e.Message}");
        }
        Console.WriteLine(Enum.TryParse<Color>((string)null, out var none) + " " + none);
        Console.WriteLine(Enum.TryParse<Color>(" Green ".AsSpan(), out var fromSpan) + " " + fromSpan);
    }
}
