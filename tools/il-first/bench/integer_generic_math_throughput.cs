// The integer types' static generic-math members through generic code (T.Log2(x) with T = int ...), served from their CoreLib IL (#391):
// the program behind the speed numbers of that change against NativeAOT (tools/il-first/measure-aot.sh --callgrind). Checksums only.
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
            sum += T.Sign(x);
            if (T.IsPow2(x)) sum++;
        }
        return sum;
    }

    static T[] Values<T>(int count) where T : IBinaryInteger<T>
    {
        var values = new T[count];
        uint state = 2463534242;
        for (var i = 0; i < count; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            values[i] = T.CreateTruncating(state & 0x7FFF); // non-negative in every type (Log2 of a negative value throws)
        }
        return values;
    }

    static void Main()
    {
        var ints = Values<int>(1 << 20);
        var uints = Values<uint>(1 << 20);
        var longs = Values<long>(1 << 20);
        var bytes = Values<byte>(1 << 20);
        var shorts = Values<short>(1 << 20);
        var nuints = Values<nuint>(1 << 20);
        long total = 0;
        for (var round = 0; round < 40; round++)
            total += Sum(ints) + Sum(uints) + Sum(longs) + Sum(bytes) + Sum(shorts) + Sum(nuints);
        Console.WriteLine(total);
    }
}
