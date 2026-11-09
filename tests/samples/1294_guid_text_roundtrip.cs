// Guid text formatting and parsing (every format specifier, upper/lower digits, braces, parentheses, the hex form), the
// span and UTF-8 overloads, and a Guid inside an interpolated string and a StringBuilder. In the framework's IL this code is
// written once over IUtfChar<TChar> (char and byte), which the primitives implement inside CoreLib: tools/il-first/slicer
// (tests/slicer.sh) compiles the same program from a slice of that IL.
using System;
using System.Text;

class Program
{
    static void Main()
    {
        var g = new Guid("0123456789abcdef0123456789abcdef");
        foreach (var f in new[] { "N", "D", "B", "P", "X", "n", "d" })
            Console.WriteLine(f + ": " + g.ToString(f));
        Console.WriteLine(g.ToString());
        Console.WriteLine($"interpolated {g} and {g:N}");
        var sb = new StringBuilder();
        sb.Append($"sb {g:B}").AppendFormat(" {0:P}", g);
        Console.WriteLine(sb.ToString());

        Console.WriteLine(Guid.Parse("{01234567-89AB-CDEF-0123-456789ABCDEF}") == g);
        Console.WriteLine(Guid.Parse("(01234567-89ab-cdef-0123-456789abcdef)") == g);
        Console.WriteLine(Guid.Parse("{0x01234567,0x89ab,0xcdef,{0x01,0x23,0x45,0x67,0x89,0xab,0xcd,0xef}}") == g);
        Console.WriteLine(Guid.ParseExact("0123456789abcdef0123456789abcdef", "N") == g);
        foreach (var bad in new[] { "", "xyz", "0123456789abcdef0123456789abcde", "01234567-89ab-cdef-0123-456789abcdeg" })
            Console.WriteLine($"TryParse({bad.Length}): {Guid.TryParse(bad, out var r)} {r}");
        try { Guid.Parse("not a guid"); } catch (FormatException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

        Span<char> chars = stackalloc char[36];
        Console.WriteLine(g.TryFormat(chars, out int written) + " " + written + " " + new string(chars.Slice(0, written)));
        Console.WriteLine(g.TryFormat(chars.Slice(0, 10), out written) + " " + written);
        Span<byte> utf8 = stackalloc byte[36];
        Console.WriteLine(g.TryFormat(utf8, out written, "N") + " " + written + " " + Encoding.ASCII.GetString(utf8.Slice(0, written)));

        var bytes = g.ToByteArray();
        Console.WriteLine(BitConverter.ToString(bytes));
        Console.WriteLine(new Guid(bytes) == g);
        Console.WriteLine(Guid.Empty);
    }
}
