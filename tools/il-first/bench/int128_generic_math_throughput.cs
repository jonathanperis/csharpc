// Int128's and UInt128's static generic-math members through generic code (T.Log2(x) with T = Int128 ...), served from their CoreLib
// IL (#393): the program behind the speed numbers of that change against NativeAOT (tools/il-first/measure-aot.sh --callgrind). Checksums only.
using System;
using System.Numerics;

static class Program
{
    static long Sum<T>(T[] values) where T : IBinaryInteger<T>
    {
        long sum = 0;
        foreach (var x in values)
        {
            sum += long.CreateTruncating(T.Log2(x));
            sum += long.CreateTruncating(T.PopCount(x));
            sum += long.CreateTruncating(T.LeadingZeroCount(x));
            sum += T.Sign(x);
            if (T.IsPow2(x)) sum++;
            sum += long.CreateSaturating(T.RotateLeft(x, 7) >> 100);
        }
        return sum;
    }

    static T[] Values<T>(int count) where T : IBinaryInteger<T>
    {
        var values = new T[count];
        ulong state = 88172645463325252;
        for (var i = 0; i < count; i++)
        {
            state ^= state << 13; state ^= state >> 7; state ^= state << 17;
            // Non-negative (Log2 of a negative Int128 throws), with bits in both halves.
            values[i] = T.CreateTruncating(((UInt128)(state >> 1) << 64 | state) >> (int)(state % 64));
        }
        return values;
    }

    static void Main()
    {
        var signed = Values<Int128>(1 << 20);
        var unsigned = Values<UInt128>(1 << 20);
        long total = 0;
        for (var round = 0; round < 30; round++)
            total += Sum(signed) + Sum(unsigned);
        Console.WriteLine(total);
    }
}
