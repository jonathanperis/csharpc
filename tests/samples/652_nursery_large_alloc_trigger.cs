// A program that only allocates large arrays must still collect: csharpc's --gc-nursery counted
// only small objects in young chunks toward its collection trigger, so this never collected and
// grew without bound (ADR 0004). .NET collects (gen 2 for the large object heap, which counts as a
// gen 0 collection too).
using System;

static class Program
{
    static void Main()
    {
        long checksum = 0;
        object[]? keep = null;
        for (var i = 0; i < 1200; i++)
        {
            var block = new byte[1 << 20];
            block[i % block.Length] = (byte)i;
            checksum += block[i % block.Length];
            var refs = new object[40000];  // large, holds references
            refs[i % refs.Length] = block;
            if (i % 100 == 0) keep = refs;
        }
        Console.WriteLine(checksum);
        Console.WriteLine(keep!.Length);
        Console.WriteLine(GC.CollectionCount(0) > 0);
        Console.WriteLine(GC.CollectionCount(2) > 0);
    }
}
