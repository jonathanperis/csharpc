// Encoding.GetEncoder() (Convert with partial output and split surrogate pairs), HtmlEncoder.Default,
// string.Format over a CompositeFormat and a span, Path.Combine/Join over spans, Math.DivRem overloads
// and MemoryExtensions.Replace.
using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

class Program
{
    static void Main()
    {
        var encoder = Encoding.UTF8.GetEncoder();
        Span<byte> bytes = stackalloc byte[5];
        encoder.Convert("héllo wörld".AsSpan(), bytes, true, out int charsUsed, out int bytesUsed, out bool completed);
        Console.WriteLine($"{charsUsed} {bytesUsed} {completed} {Encoding.UTF8.GetString(bytes.Slice(0, bytesUsed))}");
        encoder.Convert(" wörld".AsSpan(), bytes, true, out charsUsed, out bytesUsed, out completed);
        Console.WriteLine($"{charsUsed} {bytesUsed} {completed}");

        string emoji = "a\U0001F600b";
        Span<byte> big = stackalloc byte[16];
        encoder.Reset();
        encoder.Convert(emoji.AsSpan(0, 2), big, false, out charsUsed, out bytesUsed, out completed);
        Console.WriteLine($"{charsUsed} {bytesUsed} {completed}");
        encoder.Convert(emoji.AsSpan(2), big, true, out charsUsed, out bytesUsed, out completed);
        Console.WriteLine($"{charsUsed} {bytesUsed} {completed} {BitConverter.ToString(big.Slice(0, bytesUsed).ToArray())}");
        Console.WriteLine($"{encoder.GetByteCount("ab\uD83D".AsSpan(), false)} {encoder.GetByteCount("ab\uD83D".AsSpan(), true)}");
        try { encoder.Convert("é".AsSpan(), bytes.Slice(0, 1), true, out _, out _, out _); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + " " + e.ParamName); }
        var chars = "xéy".ToCharArray();
        var output = new byte[8];
        Console.WriteLine($"{encoder.GetBytes(chars, 0, chars.Length, output, 0, true)} {BitConverter.ToString(output, 0, 4)}");

        Console.WriteLine(HtmlEncoder.Default.Encode("<a href=\"x?y=1&z='2'\">café + 😀</a>"));
        Console.WriteLine(HtmlEncoder.Default.Encode("plain text"));

        var format = CompositeFormat.Parse("{0}-{1,4}|{2:X}");
        ReadOnlySpan<object> args = new object[] { "a", 7, 255 };
        Console.WriteLine(string.Format(null, format, args));

        ReadOnlySpan<string> parts = new[] { "a", "b", "c.txt" };
        Console.WriteLine($"{Path.Combine(parts)} {Path.Join(parts)}".Replace(Path.DirectorySeparatorChar, '/'));

        Console.WriteLine($"{Math.DivRem(17u, 5u)} {Math.DivRem(17UL, 5UL)} {Math.DivRem((byte)17, (byte)5)} {Math.DivRem((short)-17, (short)5)} {Math.DivRem(-17L, 5L, out long rem)} {rem}");

        Span<char> destination = stackalloc char[8];
        "a-b-c".AsSpan().Replace(destination, '-', '+');
        Console.WriteLine(new string(destination.Slice(0, 5)));
        Span<char> inPlace = "x.y.z".ToCharArray();
        inPlace.Replace('.', '/');
        Console.WriteLine(new string(inPlace));
        try { "abcdef".AsSpan().Replace(destination.Slice(0, 3), 'a', 'b'); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
    }
}
