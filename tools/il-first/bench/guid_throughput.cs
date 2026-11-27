// Guid throughput, the program behind docs/il-first/System.Guid.md (#416): formatting in the five formats (UTF-16 and UTF-8),
// parsing them back, NewGuid, comparisons, hashing and byte conversion. The checksum depends on the fixed inputs only.
using System;

class Program
{
    static void Main()
    {
        long check = 0;
        var seed = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
        Span<char> chars = stackalloc char[68];
        Span<byte> utf8 = stackalloc byte[68];
        Span<byte> bytes = stackalloc byte[16];
        string[] formats = ["N", "D", "B", "P", "X"];
        for (int i = 0; i < 400_000; i++)
        {
            var g = new Guid(i, (short)i, (short)(i >> 3), seed.ToByteArray()[..8]);
            foreach (var f in formats)
            {
                g.TryFormat(chars, out int n, f);
                var back = Guid.ParseExact(chars[..n], f);
                if (back == g) check += n;
            }
            ((IUtf8SpanFormattable)g).TryFormat(utf8, out int u, "D", null);
            if (Guid.TryParse(utf8[..u], out var fromUtf8) && fromUtf8.Equals(g)) check++;
            g.TryWriteBytes(bytes, bigEndian: (i & 1) == 0, out _);
            check += bytes[3] + g.CompareTo(seed) + (g.GetHashCode() & 1);
        }
        for (int i = 0; i < 1_000_000; i++) if (Guid.NewGuid().Version == 4) check++;
        Console.WriteLine(check);
    }
}
