// Guid through the paths the framework's code adds over a UTF-16-only port: UTF-8 formatting and parsing (IUtf8SpanFormattable,
// IUtf8SpanParsable), the big-endian byte constructor/ToByteArray/TryWriteBytes, Version and Variant, CreateVersion7's layout,
// the comparison operators, and the exact exception messages of malformed text. New Guids are only checked for their bits.
using System;
using System.Text;

static class Program
{
    static void Main()
    {
        var g = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
        foreach (var format in new[] { "D", "N", "B", "P", "X" })
        {
            Span<byte> utf8 = stackalloc byte[80];
            bool ok = ((IUtf8SpanFormattable)g).TryFormat(utf8, out int written, format, null);
            Console.WriteLine($"utf8 {format}: {ok} {written} {Encoding.UTF8.GetString(utf8[..written])}");
        }
        Span<byte> small = stackalloc byte[10];
        Console.WriteLine($"utf8 too small: {((IUtf8SpanFormattable)g).TryFormat(small, out int none, "D", null)} {none}");
        var parsed = Guid.Parse(Encoding.UTF8.GetBytes("{0f8fad5b-d9cb-469f-a165-70867728950e}"));
        Console.WriteLine($"utf8 parse: {parsed == g}, TryParse utf8 bad: {Guid.TryParse("zz"u8, out _)}");

        var bytes = g.ToByteArray(bigEndian: true);
        Console.WriteLine($"big-endian bytes: {Convert.ToHexString(bytes)}");
        Console.WriteLine($"little-endian bytes: {Convert.ToHexString(g.ToByteArray())}");
        Console.WriteLine($"from big-endian: {new Guid(bytes, bigEndian: true) == g}, as little-endian: {new Guid(bytes)}");
        Span<byte> dest = stackalloc byte[16];
        Console.WriteLine($"TryWriteBytes big-endian: {g.TryWriteBytes(dest, bigEndian: true, out int w)} {w} {Convert.ToHexString(dest)}");
        Console.WriteLine($"Version {g.Version}, Variant {g.Variant}; Empty {Guid.Empty.Version}/{Guid.Empty.Variant}; AllBitsSet {Guid.AllBitsSet.Version}/{Guid.AllBitsSet.Variant}");

        var v4 = Guid.NewGuid();
        Console.WriteLine($"NewGuid: version {v4.Version}, variant 10xx {(v4.Variant & 0xC) == 0x8}, distinct {v4 != Guid.NewGuid()}");
        var stamp = new DateTimeOffset(2024, 5, 6, 7, 8, 9, 123, TimeSpan.Zero);
        var v7 = Guid.CreateVersion7(stamp);
        Console.WriteLine($"CreateVersion7: version {v7.Version}, variant 10xx {(v7.Variant & 0xC) == 0x8}, time prefix {v7.ToString("N")[..12]} (ms {stamp.ToUnixTimeMilliseconds():x12})");
        Console.WriteLine($"v7 ordering: {Guid.CreateVersion7(stamp.AddMilliseconds(1)) > v7}");

        var a = new Guid("00000000-0000-0000-0000-000000000001");
        var b = new Guid("00000001-0000-0000-0000-000000000000");
        Console.WriteLine($"a<b {a < b}, a<=b {a <= b}, a>b {a > b}, CompareTo {a.CompareTo(b)}, CompareTo(null) {a.CompareTo(null)}");
        try { a.CompareTo("x"); } catch (ArgumentException e) { Console.WriteLine($"CompareTo(string): {e.Message}"); }

        foreach (var bad in new[] { "", "0f8fad5b-d9cb-469f-a165-70867728950", "0f8fad5b-d9cb-469f-a165-70867728950g", "{0f8fad5b-d9cb-469f-a165-70867728950e", "(0f8fad5b-d9cb-469f-a165-70867728950e}", "0f8fad5bd9cb469fa16570867728950e00" })
        {
            try { Guid.Parse(bad); Console.WriteLine($"'{bad}': parsed"); }
            catch (FormatException e) { Console.WriteLine($"'{bad}': {e.Message}"); }
        }
        foreach (var (text, format) in new[] { ("0f8fad5bd9cb469fa16570867728950e", "D"), ("0f8fad5b-d9cb-469f-a165-70867728950e", "Q"), ("{0x0f8fad5b,0xd9cb,0x469f,{0xa1,0x65,0x70,0x86,0x77,0x28,0x95,0x0e}}", "X") })
        {
            try { Console.WriteLine($"ParseExact '{format}': {Guid.ParseExact(text, format)}"); }
            catch (FormatException e) { Console.WriteLine($"ParseExact '{format}': {e.Message}"); }
        }
        try { g.ToString("Z"); } catch (FormatException e) { Console.WriteLine($"ToString(\"Z\"): {e.Message}"); }
        try { new Guid(new byte[15]); } catch (ArgumentException e) { Console.WriteLine($"15 bytes: {e.Message}"); }
        Console.WriteLine($"hash equal for equal: {g.GetHashCode() == new Guid(g.ToByteArray()).GetHashCode()}");

        // Through its interfaces, statically and on a boxed Guid.
        Console.WriteLine($"IEquatable: {((IEquatable<Guid>)g).Equals(g)}, IComparable<Guid>: {((IComparable<Guid>)g).CompareTo(b)}");
        object boxed = g;
        Console.WriteLine($"boxed is IEquatable<Guid> {boxed is IEquatable<Guid>}, IComparable<Guid> {boxed is IComparable<Guid>}, ISpanFormattable {boxed is ISpanFormattable}, IUtf8SpanFormattable {boxed is IUtf8SpanFormattable}");
        if (boxed is IEquatable<Guid> eq && boxed is IComparable<Guid> cmp && boxed is IFormattable fmt)
            Console.WriteLine($"boxed dispatch: Equals {eq.Equals(g)}, CompareTo {cmp.CompareTo(a)}, ToString {fmt.ToString("N", null)}");
        Console.WriteLine($"EqualityComparer: {System.Collections.Generic.EqualityComparer<Guid>.Default.Equals(g, parsed)}, Comparer: {System.Collections.Generic.Comparer<Guid>.Default.Compare(a, b)}");
        var set = new System.Collections.Generic.HashSet<Guid> { g, a, b, g };
        var list = new System.Collections.Generic.List<Guid> { b, g, a };
        list.Sort();
        Console.WriteLine($"HashSet count {set.Count}, contains {set.Contains(parsed)}; sorted {string.Join(" ", list.ConvertAll(x => x.ToString("N")[..8]))}, IndexOf {list.IndexOf(g)}");
    }
}
