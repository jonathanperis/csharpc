// MemoryExtensions.IndexOf / IndexOfAny / IndexOfAnyExcept over byte and char spans of every length
// around the vector width, with the match at every position (and none), as the HTTP parser uses them.
using System;
using System.Text;

static class Program
{
    const char LS = (char)0x2028, PS = (char)0x2029;

    static void Main()
    {
        var sb = new StringBuilder();
        long checksum = 0;
        for (int length = 0; length <= 40; length++)
        {
            for (int at = -1; at < length; at++)
            {
                var bytes = new byte[length];
                var chars = new char[length];
                for (int i = 0; i < length; i++) { bytes[i] = (byte)('a' + i % 7); chars[i] = (char)('a' + i % 7); }
                if (at >= 0) { bytes[at] = (byte)'\n'; chars[at] = LS; }
                ReadOnlySpan<byte> b = bytes;
                ReadOnlySpan<char> c = chars;
                int r1 = b.IndexOf((byte)'\n');
                int r2 = b.IndexOfAny((byte)'\r', (byte)'\n');
                int r3 = b.IndexOfAny((byte)'\r', (byte)'\n', (byte)'g');
                int r4 = c.IndexOf(LS);
                int r5 = c.IndexOfAny(PS, LS);
                int r6 = c.IndexOfAny('x', 'y', LS);
                int r7 = b.IndexOfAnyExcept((byte)'a');
                int r8 = b.Slice(Math.Min(1, length)).IndexOfAnyExcept((byte)'b', (byte)'c');
                int r9 = c.IndexOfAnyExcept('a', 'b', 'c');
                int r10 = c.IndexOfAnyExcept('a');
                checksum = checksum * 31 + r1 + 3 * r2 + 5 * r3 + 7 * r4 + 11 * r5 + 13 * r6 + 17 * r7 + 19 * r8 + 23 * r9 + 29 * r10;
                if (length is 0 or 1 or 15 or 16 or 17 or 33 && at is -1 or 0 or 7 or 8 or 15 or 16 or 32)
                    sb.Append($"{length}/{at}: {r1} {r2} {r3} {r4} {r5} {r6} {r7} {r8} {r9} {r10}\n");
            }
        }
        Console.Write(sb);
        Console.WriteLine(checksum);
        Span<byte> all = new byte[50];
        all.Fill((byte)'z');
        Console.WriteLine($"{all.IndexOfAnyExcept((byte)'z')} {all.IndexOfAny((byte)'z', (byte)'y')} {((ReadOnlySpan<byte>)all).IndexOf((byte)'y')}");
        Console.WriteLine($"{"Host: localhost\r\nAccept: */*\r\n".AsSpan().IndexOfAny('\r', '\n')} {"key:value".AsSpan().IndexOfAnyExcept('k', 'e', 'y')}");
    }
}
