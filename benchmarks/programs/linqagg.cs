// LINQ aggregates and quantifiers over arrays and List<T> (the shapes .NET's Enumerable reads as spans):
// Sum, Average, Min, Max, Contains, SequenceEqual, Count, Any, First, Last, ElementAt and ToArray on
// int, long, float and double sources. Deterministic checksums.
using System;
using System.Collections.Generic;
using System.Linq;

const int N = 4096;
const int Rounds = 2000;

var ints = new int[N];
var longs = new long[N];
var doubles = new double[N];
var floats = new float[N];
uint x = 12345;
for (int i = 0; i < N; i++)
{
    x ^= x << 13; x ^= x >> 17; x ^= x << 5;
    ints[i] = (int)(x % 20001) - 10000;
    longs[i] = (long)ints[i] * 100003;
    doubles[i] = ints[i] / 7.0;
    floats[i] = ints[i] / 3.0f;
}
var intList = new List<int>(ints);
var doubleList = new List<double>(doubles);
var intsCopy = (int[])ints.Clone();
var bytes = new byte[N];
for (int i = 0; i < N; i++) bytes[i] = (byte)ints[i];
var bytesCopy = (byte[])bytes.Clone();

long check = 0;
double dcheck = 0;
for (int r = 0; r < Rounds; r++)
{
    check += ints.Sum() + intList.Sum() + longs.Sum() / 1000;
    check += ints.Min() + ints.Max() + intList.Min() + intList.Max() + longs.Min() / 1000 + longs.Max() / 1000;
    dcheck += doubles.Sum() + doubleList.Sum() + floats.Sum();
    dcheck += doubles.Min() + doubles.Max() + floats.Min() + floats.Max() + doubleList.Max();
    dcheck += ints.Average() + intList.Average() + doubles.Average() + floats.Average();
    check += ints.Contains(r - 1000) ? 1 : 0;
    check += intList.Contains(r) ? 1 : 0;
    check += ints.SequenceEqual(intsCopy) ? 1 : 0;
    check += bytes.SequenceEqual(bytesCopy) ? 1 : 0;
    check += ints.Count() + intList.Count() + (ints.Any() ? 1 : 0);
    check += ints.First() + ints.Last() + intList.Last() + ints.ElementAt(r % N);
    if ((r & 15) == 0) check += ints.ToArray().Length + intList.ToArray()[r % N];
}
Console.WriteLine($"integer checksum: {check}");
Console.WriteLine($"floating checksum: {dcheck:R}");
