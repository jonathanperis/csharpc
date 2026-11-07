// What csharpc compiling itself (with the SDK's generators linked in) needed: BitConverter.TryWriteBytes for every
// primitive (false and nothing written when the destination is short), MemoryExtensions.Sort over spans (default,
// Comparison<T>, a comparer struct; empty and one-element spans; a throwing comparison), and Type.Delimiter.
using System;
using System.Collections.Generic;

struct Descending : IComparer<int>
{
    public int Compare(int x, int y) => y.CompareTo(x);
}

class P
{
    static string Hex(Span<byte> bytes) => Convert.ToHexString(bytes);

    static void Write<T>(string name, Func<Span<byte>, bool> write, int size)
    {
        Span<byte> exact = stackalloc byte[size + 2];
        exact.Fill(0xEE);
        Console.WriteLine(name + " " + write(exact) + " " + Hex(exact));
        Span<byte> shortBuffer = stackalloc byte[size - 1];
        shortBuffer.Fill(0xEE);
        Console.WriteLine(name + " short " + write(shortBuffer) + " " + Hex(shortBuffer));
    }

    static void Main()
    {
        Write<bool>("bool", d => BitConverter.TryWriteBytes(d, true), 1);
        Write<char>("char", d => BitConverter.TryWriteBytes(d, 'A'), 2);
        Write<short>("short", d => BitConverter.TryWriteBytes(d, (short)-2), 2);
        Write<ushort>("ushort", d => BitConverter.TryWriteBytes(d, (ushort)0xABCD), 2);
        Write<int>("int", d => BitConverter.TryWriteBytes(d, -123456789), 4);
        Write<uint>("uint", d => BitConverter.TryWriteBytes(d, 0xDEADBEEFu), 4);
        Write<long>("long", d => BitConverter.TryWriteBytes(d, -1234567890123L), 8);
        Write<ulong>("ulong", d => BitConverter.TryWriteBytes(d, 0x0102030405060708UL), 8);
        Write<float>("float", d => BitConverter.TryWriteBytes(d, 1.5f), 4);
        Write<double>("double", d => BitConverter.TryWriteBytes(d, -2.25), 8);

        int[] numbers = { 5, 3, 9, 1, 7, 3, 8 };
        var span = numbers.AsSpan();
        span.Sort();
        Console.WriteLine(string.Join(",", numbers));
        span.Slice(1, 4).Sort((a, b) => b.CompareTo(a));
        Console.WriteLine(string.Join(",", numbers));
        span.Sort(new Descending());
        Console.WriteLine(string.Join(",", numbers));
        string[] words = { "pear", "fig", "apple", "kiwi" };
        words.AsSpan().Sort(StringComparer.Ordinal);
        Console.WriteLine(string.Join(",", words));
        words.AsSpan().Sort((a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b));
        Console.WriteLine(string.Join(",", words));
        Span<int> empty = default;
        empty.Sort();
        Span<int> one = new int[] { 4 };
        one.Sort((a, b) => throw new InvalidOperationException("not called"));
        Console.WriteLine(one[0]);
        try { new int[] { 2, 1 }.AsSpan().Sort((a, b) => throw new FormatException("boom")); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message + " / " + e.InnerException?.GetType().Name); }
        try { numbers.AsSpan().Sort((Comparison<int>)null!); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        Console.WriteLine(Type.Delimiter);
        Console.WriteLine(typeof(List<int>).Namespace + Type.Delimiter + typeof(List<int>).Name);
    }
}
