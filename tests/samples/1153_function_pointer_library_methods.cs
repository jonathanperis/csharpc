#:property AllowUnsafeBlocks=true
using System;
using System.Collections.Generic;
using System.Globalization;

// `&M` of a library method: static methods of the BCL taken as function pointers (managed calling convention), with
// value, reference, by-ref and void signatures, generic instantiations, and across a call to a helper.
unsafe class P
{
    static T Apply<T>(delegate*<T, T> f, T x) => f(x);
    static int Fold(delegate*<int, int, int> f, int[] values)
    {
        var acc = values[0];
        for (int i = 1; i < values.Length; i++) acc = f(acc, values[i]);
        return acc;
    }

    static void Main()
    {
        delegate*<double, double> sqrt = &Math.Sqrt;
        Console.WriteLine(sqrt(2));
        delegate*<int, int, int> max = &Math.Max;
        Console.WriteLine(max(3, 9));
        Console.WriteLine(Fold(&Math.Min, [5, 3, 8, 1, 9]));
        Console.WriteLine(Fold(&Math.Max, [5, 3, 8, 1, 9]));
        delegate*<string?, bool> empty = &string.IsNullOrEmpty;
        Console.WriteLine($"{empty(null)} {empty("")} {empty("x")}");
        delegate*<string, string, string> concat = &string.Concat;
        Console.WriteLine(concat("a", "b"));
        delegate*<char, bool> digit = &char.IsDigit;
        Console.WriteLine($"{digit('7')} {digit('x')}");
        delegate*<string, int> parse = &int.Parse;
        Console.WriteLine(parse("123") + 1);
        delegate*<string, out int, bool> tryParse = &int.TryParse;
        Console.WriteLine($"{tryParse("42", out var n)} {n} {tryParse("zz", out n)} {n}");
        delegate*<string, void> write = &Console.WriteLine;
        write("written through a pointer");
        delegate*<int, int> abs = &Math.Abs;
        Console.WriteLine(abs(-17));
        Console.WriteLine(Apply(&Math.Abs, -2.5));
        delegate*<long, long, long> rem = &Math.Max;
        Console.WriteLine(rem(1L << 40, 7));
        delegate*<string, string, int> compare = &string.CompareOrdinal;
        Console.WriteLine(compare("a", "b"));
        delegate*<int, int> sign = &Math.Sign;
        Console.WriteLine(sign(-9));
        delegate*<string, int> length = &Length;
        Console.WriteLine(length("four"));
        Console.WriteLine((nint)sqrt != 0 && sqrt == (delegate*<double, double>)(nint)sqrt);
    }

    static int Length(string s) => s.Length;
}
