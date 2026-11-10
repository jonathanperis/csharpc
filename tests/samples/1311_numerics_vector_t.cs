// System.Numerics.Vector<T> on 16-byte LLVM vectors (NativeAOT's default size), through the operations the framework's LINQ
// uses: loops written for any width (the results do not depend on Vector<T>.Count), construction from a span, Widen, Sum,
// comparisons and ConditionalSelect, CopyTo, Indices, and the too-short-span exceptions.
using System.Numerics;

static int SumInts(int[] a)
{
    var acc = Vector<int>.Zero;
    int i = 0;
    for (; i <= a.Length - Vector<int>.Count; i += Vector<int>.Count) acc += new Vector<int>(a.AsSpan(i));
    int total = Vector.Sum(acc);
    for (; i < a.Length; i++) total += a[i];
    return total;
}

static long SumWidened(int[] a)
{
    var acc = Vector<long>.Zero;
    int i = 0;
    for (; i <= a.Length - Vector<int>.Count; i += Vector<int>.Count)
    {
        Vector.Widen(new Vector<int>(a.AsSpan(i)), out Vector<long> low, out Vector<long> high);
        acc += low + high;
    }
    long total = Vector.Sum(acc);
    for (; i < a.Length; i++) total += a[i];
    return total;
}

static (int Min, int Max) MinMax(int[] a)
{
    var lo = new Vector<int>(a[0]);
    var hi = lo;
    int i = 0;
    for (; i <= a.Length - Vector<int>.Count; i += Vector<int>.Count)
    {
        var v = new Vector<int>(a.AsSpan(i));
        lo = Vector.ConditionalSelect(Vector.LessThan(v, lo), v, lo);
        hi = Vector.Max(hi, v);
    }
    int min = int.MaxValue, max = int.MinValue;
    for (int j = 0; j < Vector<int>.Count; j++) { min = Math.Min(min, lo[j]); max = Math.Max(max, hi[j]); }
    for (; i < a.Length; i++) { min = Math.Min(min, a[i]); max = Math.Max(max, a[i]); }
    return (min, max);
}

static int CountPositive(float[] a)
{
    var ones = Vector<int>.One;
    var acc = Vector<int>.Zero;
    int i = 0;
    for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count)
    {
        var mask = Vector.GreaterThan(new Vector<float>(a.AsSpan(i)), Vector<float>.Zero);
        acc += Vector.BitwiseAnd(Vector.AsVectorInt32(mask), ones);
    }
    int total = Vector.Sum(acc);
    for (; i < a.Length; i++) if (a[i] > 0) total++;
    return total;
}

var data = new int[1003];
for (int i = 0; i < data.Length; i++) data[i] = (i * 7919) % 1009 - 500;
Console.WriteLine($"{SumInts(data)} {SumWidened(data)} {data.Sum()}");
Console.WriteLine(MinMax(data));
var floats = new float[517];
for (int i = 0; i < floats.Length; i++) floats[i] = (i % 5) - 2;
Console.WriteLine($"{CountPositive(floats)} {floats.Count(f => f > 0)}");

var indices = Vector<int>.Indices;
Console.WriteLine($"{indices[0]} {indices[1]} {Vector<long>.Indices[1]} {Vector<byte>.Indices[3]}");
Console.WriteLine(new Vector<int>(7)[0] == 7 && new Vector<short>(-3)[1] == -3);

var copy = new int[Vector<int>.Count + 2];
new Vector<int>(5).CopyTo(copy.AsSpan());
Console.WriteLine($"{copy[0]} {copy[Vector<int>.Count - 1]} {copy[Vector<int>.Count]}");

foreach (var action in new Action[]
{
    () => new Vector<int>(new int[3].AsSpan()),
    () => new Vector<int>(7).CopyTo(new int[3].AsSpan()),
    () => Vector.Create<int>(new int[3].AsSpan()),
})
{
    try { action(); Console.WriteLine("no exception"); }
    catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}
