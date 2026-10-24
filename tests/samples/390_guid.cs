// System.Guid: construction, formatting (N/D/B/P/X), parsing (all formats, compat forms, errors),
// bytes, equality, hashing, ordering, and properties of NewGuid / CreateVersion7.
using System;
using System.Collections.Generic;

class Program
{
    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine($"{label}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static T ParseVia<T>(string s) where T : IParsable<T> => T.Parse(s, null);

    static bool TryParseVia<T>(string s, out T value) where T : ISpanParsable<T> => T.TryParse(s.AsSpan(), null, out value);

    static string FormatVia<T>(T value, string format) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[80];
        return value.TryFormat(buffer, out int n, format, null) ? buffer.Slice(0, n).ToString() : "too short";
    }

    static void Main()
    {
        var g = new Guid("CA761232-ED42-11CE-BACD-00AA0057B223");
        Console.WriteLine(g);
        foreach (var f in new[] { "N", "D", "B", "P", "X", "n", "d", "b", "p", "x", "", null })
            Console.WriteLine($"[{f}] {g.ToString(f)}");
        Console.WriteLine($"{g:B} {g:N}");
        Console.WriteLine(string.Format("{0:P}", g));
        Console.WriteLine(Guid.Empty);
        Console.WriteLine(default(Guid) == Guid.Empty);
        Console.WriteLine(Guid.AllBitsSet);
        Console.WriteLine(g.Version + " " + g.Variant);
        Try("bad format", () => g.ToString("Q"));
        Try("long format", () => g.ToString("DD"));

        var h = new Guid(0x12345678, 0x1234, 0x5678, 0x9a, 0xbc, 0xde, 0xf0, 0x01, 0x23, 0x45, 0x67);
        Console.WriteLine(h);
        Console.WriteLine(new Guid(-1, -2, -3, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Console.WriteLine(new Guid(0xFFFFFFFEu, (ushort)65000, (ushort)1, 0, 0, 0, 0, 0, 0, 0, 0));
        byte[] bytes = h.ToByteArray();
        Console.WriteLine(BitConverter.ToString(bytes));
        Console.WriteLine(BitConverter.ToString(h.ToByteArray(bigEndian: true)));
        Console.WriteLine(new Guid(bytes) == h);
        Console.WriteLine(new Guid(h.ToByteArray(true), bigEndian: true) == h);
        Console.WriteLine(new Guid((ReadOnlySpan<byte>)bytes).Equals(h));
        Span<byte> buffer = stackalloc byte[20];
        Console.WriteLine(h.TryWriteBytes(buffer) + " " + buffer[0] + " " + buffer[15]);
        Console.WriteLine(h.TryWriteBytes(buffer.Slice(0, 15)));
        Console.WriteLine(h.TryWriteBytes(buffer, true, out int written) + " " + written + " " + buffer[0]);
        Try("ctor 15 bytes", () => new Guid(new byte[15]));
        Try("ctor null bytes", () => new Guid((byte[])null));
        Try("ctor 7 bytes", () => new Guid(1, 2, 3, new byte[7]));

        // Parsing: every format, case-insensitive hex, surrounding whitespace.
        string[] inputs =
        {
            "ca761232ed4211cebacd00aa0057b223",
            "CA761232-ED42-11CE-BACD-00AA0057B223",
            "{ca761232-ed42-11ce-bacd-00aa0057b223}",
            "(ca761232-ed42-11ce-bacd-00aa0057b223)",
            "{0xca761232,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}}",
            "{0xca761232, 0xed42, 0x11ce, {0xba, 0xcd, 0x0, 0xaa, 0x0, 0x57, 0xb2, 0x23}}",
            "{0x1,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xA,0xB}}",
            "  \tca761232-ed42-11ce-bacd-00aa0057b223\n ",
            "+a761232-0x42-+0x1-bacd-00aa0057b223",
            "0Xa76123-ed42-11ce-0x0d-00aa0057b223",
            "ca761232-ed42-11ce-bacd-00aa0057b22",
            "ca761232-ed42-11ce-bacd-00aa0057b22g",
            "ca761232ed42-11ce-bacd-00aa0057b2234",
            "ca761232ed4211cebacd00aa0057b22z",
            "{ca761232-ed42-11ce-bacd-00aa0057b223)",
            "(ca761232-ed42-11ce-bacd-00aa0057b223",
            "{0xca761232,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x123}}",
            "{0xca761232,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x123456789}}",
            "{0xca761232,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}}x",
            "{0xca761232,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}",
            "{0xca761232,0xed42,0x11ce,0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}}",
            "{ca761232,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}}",
            "{0xca761232,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2}}0000",
            "{0x,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}}xxxx",
            "{0xca7612321,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}}",
            "{0xca76123g,0xed42,0x11ce,{0xba,0xcd,0x00,0xaa,0x00,0x57,0xb2,0x23}}",
            "short",
            "",
        };
        foreach (var s in inputs)
        {
            Try("Parse " + s.Trim(), () => Guid.Parse(s));
            Try("ctor  " + s.Trim(), () => new Guid(s));
            bool ok = Guid.TryParse(s, out Guid r);
            Console.WriteLine($"TryParse {ok} {r}");
        }
        Try("Parse null", () => Guid.Parse((string)null));
        Try("ctor null", () => new Guid((string)null));
        Console.WriteLine(Guid.TryParse((string)null, out _));
        Console.WriteLine(Guid.Parse("ca761232-ed42-11ce-bacd-00aa0057b223".AsSpan()));
        Console.WriteLine(Guid.Parse("ca761232ed4211cebacd00aa0057b223", null));

        // ParseExact / TryParseExact.
        string d = g.ToString("D");
        foreach (var fmt in new[] { "N", "D", "B", "P", "X", "d", "Z", "DD", "" })
        {
            foreach (var shape in new[] { "N", "D", "B", "P", "X" })
            {
                string input = " " + g.ToString(shape) + " ";
                Try($"ParseExact {shape} as {fmt}", () => Guid.ParseExact(input, fmt));
                bool ok = Guid.TryParseExact(input, fmt, out Guid r);
                Console.WriteLine($"TryParseExact {shape} as {fmt}: {ok} {r:N}");
            }
        }
        Try("ParseExact null input", () => Guid.ParseExact(null, "D"));
        Try("ParseExact null format", () => Guid.ParseExact(d, null));
        Console.WriteLine(Guid.TryParseExact(d, null, out _));
        Console.WriteLine(Guid.TryParseExact((string)null, "D", out _));

        // Equality, hashing and ordering.
        var a1 = new Guid("00000001-0000-0000-0000-000000000000");
        var a2 = new Guid("80000000-0000-0000-0000-000000000000");
        var b1 = new Guid("00000000-8000-0000-0000-000000000000");
        var k1 = new Guid("00000000-0000-0000-0000-0000000000ff");
        var list = new List<Guid> { a2, k1, Guid.Empty, g, b1, a1, h, Guid.AllBitsSet };
        list.Sort();
        foreach (var x in list) Console.WriteLine($"{x} {x.GetHashCode()}");
        Console.WriteLine(a1.CompareTo(a2) + " " + a2.CompareTo(a1) + " " + a1.CompareTo(a1) + " " + a1.CompareTo(null));
        Console.WriteLine(((IComparable)a1).CompareTo((object)a2));
        Try("CompareTo string", () => a1.CompareTo("x"));
        Console.WriteLine($"{a1 < a2} {a1 <= a2} {a1 > a2} {a1 >= a2} {a1 <= a1} {a1 >= a1} {a1 < a1} {a1 == a1} {a1 != a2}");
        Console.WriteLine($"{b1 > k1} {k1 < b1}");
        Console.WriteLine(g.Equals((object)new Guid(g.ToString())) + " " + g.Equals((object)"x") + " " + g.Equals(h));
        Console.WriteLine(EqualityComparer<Guid>.Default.Equals(g, new Guid(g.ToByteArray())));
        var dict = new Dictionary<Guid, string> { [g] = "g", [h] = "h" };
        Console.WriteLine(dict[new Guid(g.ToString("N"))] + dict[Guid.Parse(h.ToString("X"))]);
        object boxed = g;
        Console.WriteLine(boxed is Guid unboxed && unboxed == g);
        Console.WriteLine(((IFormattable)g).ToString("N", null));
        Console.WriteLine(((IEquatable<Guid>)g).Equals(g));

        // Generic math-style parsing/formatting and alignment.
        Console.WriteLine(ParseVia<Guid>("{ca761232-ed42-11ce-bacd-00aa0057b223}") == g);
        Console.WriteLine(TryParseVia("nope", out Guid bad) + " " + bad);
        Console.WriteLine(TryParseVia(" ca761232ed4211cebacd00aa0057b223 ", out Guid good) + " " + good);
        Console.WriteLine(FormatVia(g, "N") + " " + FormatVia(g, "x"));
        Console.WriteLine($"[{g,40}] [{g,-40:P}]");

        // TryFormat.
        Span<char> chars = stackalloc char[68];
        Console.WriteLine(g.TryFormat(chars, out int n, "X") + " " + n + " " + chars.Slice(0, n).ToString());
        Console.WriteLine(g.TryFormat(chars.Slice(0, 30), out n) + " " + n);
        Console.WriteLine(g.TryFormat(chars, out n) + " " + chars.Slice(0, n).ToString());
        Span<byte> utf8 = stackalloc byte[40];
        Console.WriteLine(g.TryFormat(utf8, out n, "B") + " " + n + " " + (char)utf8[0] + (char)utf8[1] + (char)utf8[37]);

        // Random GUIDs: only their shape is deterministic.
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        Console.WriteLine(r1 != r2);
        Console.WriteLine(r1 != Guid.Empty);
        Console.WriteLine(r1.ToString().Length + " " + r1.ToString()[14] + " " + r1.Version + " " + (r1.Variant >> 2));
        Console.WriteLine("89ab".Contains(r1.ToString()[19]));
        Console.WriteLine(Guid.Parse(r1.ToString("X")) == r1);
        var v7 = Guid.CreateVersion7();
        var v7b = Guid.CreateVersion7();
        Console.WriteLine(v7.Version + " " + (v7.Variant >> 2) + " " + (v7 != v7b));
        long ms = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
        long stamp = Convert.ToInt64(v7.ToString("N").Substring(0, 12), 16);
        Console.WriteLine(Math.Abs(ms - stamp) < 60000);
    }
}
