// BitArray from the framework's IL (#372) on sizes that take its vector paths (Vector128 here; the 256/512-bit ones are not accelerated):
// construction from long bool[] spans, And/Or/Xor/Not over many 32-bit words with a partial last word, CopyTo bool[]/byte[]/int[] at
// offsets, HasAllSet/HasAnySet, shifts, and Length changes that clear the bits past the old end.
using System;
using System.Collections;

static class Program
{
    static string Bits(BitArray b, int max = 80)
    {
        var chars = new char[Math.Min(b.Length, max)];
        for (int i = 0; i < chars.Length; i++) chars[i] = b[i] ? '1' : '0';
        return new string(chars) + (b.Length > max ? "..." : "");
    }

    static int Count(BitArray b)
    {
        int n = 0;
        foreach (bool bit in b) if (bit) n++;
        return n;
    }

    static void Main()
    {
        foreach (var length in new[] { 1, 15, 16, 17, 31, 32, 33, 63, 64, 65, 100, 255, 257, 1000 })
        {
            var source = new bool[length];
            for (int i = 0; i < length; i++) source[i] = (i * 7 + 3) % 5 < 2;
            var a = new BitArray(source);
            var b = new BitArray(length);
            for (int i = 0; i < length; i += 3) b[i] = true;
            var and = ((BitArray)a.Clone()).And(b);
            var or = ((BitArray)a.Clone()).Or(b);
            var xor = ((BitArray)a.Clone()).Xor(b);
            var not = ((BitArray)a.Clone()).Not();
            Console.WriteLine($"{length}: a={Count(a)} and={Count(and)} or={Count(or)} xor={Count(xor)} not={Count(not)} all={a.HasAllSet()} any={a.HasAnySet()} notAll={new BitArray(length, true).HasAllSet()}");
            Console.WriteLine("  " + Bits(xor));

            var back = new bool[length + 3];
            a.CopyTo(back, 2);
            bool same = true;
            for (int i = 0; i < length; i++) same &= back[i + 2] == source[i];
            var bytes = new byte[(length + 7) / 8 + 1];
            a.CopyTo(bytes, 1);
            var ints = new int[(length + 31) / 32];
            a.CopyTo(ints, 0);
            Console.WriteLine($"  bool[] round trip {same}, bytes {Convert.ToHexString(bytes)[..Math.Min(32, bytes.Length * 2)]}, ints[0] {ints[0]:X8}");

            var shifted = ((BitArray)a.Clone()).LeftShift(37);
            var right = ((BitArray)a.Clone()).RightShift(5);
            Console.WriteLine($"  << 37 {Count(shifted)} >> 5 {Count(right)}");
            var grown = (BitArray)a.Clone();
            grown.Length = length + 40;
            grown.SetAll(true);
            grown.Length = length;
            grown.Length = length + 40;
            Console.WriteLine($"  regrown {Count(grown)} of {grown.Length}");
        }
        try { new BitArray(10).And(new BitArray(11)); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { new BitArray(40).CopyTo(new bool[39], 0); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
