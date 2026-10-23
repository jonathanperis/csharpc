// Bounds checks at their edges: index == Length, negative and huge indices, uint/long indices,
// ^n indices, and loops whose array (or its length bound) changes while they run.
using System;

int[] a = { 10, 20, 30, 40, 50 };

Try("a[Length - 1]", () => a[a.Length - 1]);
Try("a[Length]", () => a[a.Length]);
Try("a[-1]", () => a[-1]);
Try("a[int.MinValue]", () => a[int.MinValue]);
Try("a[int.MaxValue]", () => a[int.MaxValue]);
Try("a[uint 4]", () => a[Hide(4u)]);
Try("a[uint 0x80000000]", () => a[Hide(0x80000000u)]);
Try("a[uint.MaxValue]", () => a[Hide(uint.MaxValue)]);
Try("a[long 2]", () => a[Hide(2L)]);
Try("a[long -1]", () => a[Hide(-1L)]);
Try("a[long 2^32 + 1]", () => a[Hide(4294967297L)]);
Try("a[ulong.MaxValue]", () => a[Hide(ulong.MaxValue)]);
Try("a[ulong 2^63]", () => a[Hide(9223372036854775808UL)]);
Try("a[ulong 5]", () => a[Hide(5UL)]);
Try("null[ulong.MaxValue]", () => Hide<int[]>(null!)[Hide(ulong.MaxValue)]);
Try("null[0]", () => Hide<int[]>(null!)[0]);
Try("new int[ulong.MaxValue]", () => new int[Hide(ulong.MaxValue)].Length);
Try("a[^1]", () => a[^1]);
Try("a[^5]", () => a[^5]);
Try("a[^0]", () => a[^0]);
Try("a[^6]", () => a[^6]);
Try("empty[0]", () => (new int[0])[0]);

// A loop that runs one past the end: everything up to the last element is summed, then it throws.
Try("sum to <= Length", () => { for (int i = 0; i <= a.Length; i++) P.S += a[i]; return P.S; });
// Counting down through zero.
Try("sum down to -1", () => { for (int i = a.Length - 1; i >= -1; i--) P.S += a[i]; return P.S; });
// The index arithmetic wraps around int.MaxValue (int arithmetic is unchecked).
Try("wrapping index", () => { int s = 0, n = 0; for (int i = int.MaxValue - 1; n < 4; i++, n++) s += a[i - (int.MaxValue - 1)]; return s; });
Try("i += big step", () => { int s = 0; for (int i = 1; i < a.Length; i += int.MaxValue) s += a[i]; return s; });

// The array variable is replaced inside the loop: the bound must follow the new (shorter) array.
Try("array shrinks in loop", () =>
{
    int[] b = { 1, 2, 3, 4, 5, 6 };
    for (int i = 0; i < 6; i++)
    {
        P.S += b[i] * 100 + b.Length;
        if (i == 2) b = new int[] { 7, 8, 9, 10 };
    }
    return P.S;
});

// foreach captures the array; replacing the variable doesn't change the iteration.
int[] c = { 1, 2, 3 };
int fsum = 0;
foreach (var x in c) { fsum += x; c = new int[] { 100 }; }
Console.WriteLine($"foreach sum {fsum} new length {c.Length}");

// Stores to fields and elements in a loop never change a length.
var holder = new Holder { Items = new int[4] };
for (int i = 0; i < holder.Items.Length; i++)
{
    holder.Count += i;
    holder.Items[i] = holder.Count;
    if (i == 1) holder.Items = new int[3];
}
Console.WriteLine($"holder {holder.Count} [{string.Join(",", holder.Items)}]");

// Struct elements: field stores and whole-element reads of the same memory must see each other.
var points = new Point[3];
for (int i = 0; i < points.Length; i++)
{
    points[i].X = i + 1;
    var copy = points[i];
    points[i].Y = copy.X * 10;
    var again = points[i];
    Console.WriteLine($"point {again.X} {again.Y}");
}

// Strings and spans.
string s = "hello";
Try("s[Length]", () => s[s.Length]);
Try("s[-1]", () => s[-1]);
Try("s[^1]", () => s[^1]);
Try("span[Length]", () => { Span<int> sp = a.AsSpan(1, 3); return sp[sp.Length]; });
Try("span[-1]", () => { Span<int> sp = a.AsSpan(1, 3); return sp[-1]; });
Try("span[2]", () => { Span<int> sp = a.AsSpan(1, 3); sp[2] += 1; return a[3]; });

// Multi-dimensional arrays.
var m = new int[2, 3];
for (int i = 0; i < m.GetLength(0); i++)
    for (int j = 0; j < m.GetLength(1); j++)
        m[i, j] = i * 10 + j;
Console.WriteLine($"md length {m.Length} last {m[1, 2]}");
Try("m[0, 3]", () => m[0, 3]);
Try("m[2, 0]", () => m[2, 0]);
Try("m[-1, 0]", () => m[-1, 0]);
Try("m[ulong.MaxValue, 0]", () => m[Hide(ulong.MaxValue), 0]);

// Jagged: a row may be replaced by a shorter one between accesses.
int[][] jag = { new int[3], new int[1] };
Try("jagged", () =>
{
    for (int i = 0; i < 3; i++)
    {
        jag[0][i] = i + 1;
        P.S += jag[0][i];
        if (i == 0) jag[0] = jag[1];
    }
    return P.S;
});

static T Hide<T>(T value) => value;

static void Try(string label, Func<int> f)
{
    P.S = 0;
    try { Console.WriteLine($"{label}: {f()}"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message} (partial {P.S})"); }
}

class Holder
{
    public int[] Items = null!;
    public int Count;
}

static class P
{
    public static int S;
}

struct Point
{
    public int X, Y;
}
