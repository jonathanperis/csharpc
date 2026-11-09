// MemoryExtensions searches over spans of every length up to 70 elements and every element size, with the needle at every
// position, absent, and repeated: the 16-byte block loops, their unrolled tails and the head/tail overlap of the framework's
// vectorized helpers (and the scalar loops) must give the answers of an element-by-element reference. tests/slicer.sh compiles
// the same program with MemoryExtensions taken from the framework's IL, where these run on Vector128 (issue #252).
using System;

int failures = 0, checks = 0;

void Check<T>(T[] data, T needle, int length) where T : IEquatable<T>
{
    var span = data.AsSpan(0, length);
    int first = -1, last = -1;
    for (int i = 0; i < length; i++)
        if (data[i].Equals(needle)) { if (first < 0) first = i; last = i; }
    checks += 3;
    if (span.IndexOf(needle) != first) failures++;
    if (span.LastIndexOf(needle) != last) failures++;
    if (span.Contains(needle) != (first >= 0)) failures++;
}

void Run<T>(Func<int, T> make) where T : IEquatable<T>
{
    var data = new T[70];
    for (int length = 0; length <= 70; length++)
    {
        for (int i = 0; i < data.Length; i++) data[i] = make(i % 9 + 1);
        // absent
        Check(data, make(200 > 0 ? 100 : 0), length);
        for (int at = 0; at < length; at++)
        {
            var saved = data[at];
            data[at] = make(50);
            Check(data, make(50), length);
            if (at + 3 < length) { var other = data[at + 3]; data[at + 3] = make(50); Check(data, make(50), length); data[at + 3] = other; }
            data[at] = saved;
        }
    }
}

Run(i => (byte)i);
Run(i => (sbyte)i);
Run(i => (short)(i * 100));
Run(i => (ushort)(i * 100));
Run(i => i * 1000);
Run(i => (uint)i * 1000u);
Run(i => i * 100000000000L);
Run(i => (ulong)i * 100000000000UL);
Run(i => (char)('a' + i));

// IndexOfAny, IndexOfAnyExcept, SequenceEqual, StartsWith, Count
var bytes = new byte[67];
for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 11);
int any = 0, anyExcept = 0, equal = 0;
for (int length = 0; length <= bytes.Length; length++)
{
    var s = bytes.AsSpan(0, length);
    any += s.IndexOfAny((byte)7, (byte)9) + s.IndexOfAny((byte)100, (byte)101, (byte)10);
    anyExcept += s.IndexOfAnyExcept((byte)0) + s.IndexOfAnyExcept((byte)0, (byte)1);
    var copy = s.ToArray();
    if (s.SequenceEqual(copy)) equal++;
    if (length > 0) { copy[length - 1] ^= 1; if (!s.SequenceEqual(copy)) equal++; copy[length - 1] ^= 1; copy[0] ^= 4; if (!s.SequenceEqual(copy)) equal++; }
    if (s.StartsWith(bytes.AsSpan(0, length / 2))) equal++;
}
Console.WriteLine($"checks={checks} failures={failures}");
Console.WriteLine($"any={any} anyExcept={anyExcept} equal={equal}");
Console.WriteLine($"ints: {new[] { 5, 6, 7, 8, 9 }.AsSpan().IndexOf(8)} {new[] { 5, 6, 7, 8, 9 }.AsSpan().LastIndexOf(5)} {new int[0].AsSpan().IndexOf(1)}");
