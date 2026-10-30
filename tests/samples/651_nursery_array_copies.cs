// Fresh objects copied into arrays that already survived collections: Span<T>.CopyTo, Array.Copy of
// structs that hold references, Array.Resize, Array.Fill, Dictionary growth and struct stores.
// Under csharpc's --gc-nursery each of these is an old-to-young store a minor collection has to
// see (ADR 0004); a missed one frees the objects and the checks below print wrong values.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

sealed class Box
{
    public readonly int Value;
    public readonly string Name;
    public Box(int value) { Value = value; Name = "box" + value; }
    public bool Ok(int expected) => Value == expected && Name == "box" + expected;
}

struct Pair
{
    public string Key;
    public Box Value;
    public long Tag;
}

sealed class Holder
{
    public Pair Inline;
    public KeyValuePair<string, Box> Kv;
}

sealed class Targets
{
    public object[] Span = new object[64];
    public KeyValuePair<string, Box>[] Kv = new KeyValuePair<string, Box>[64];
    public Pair[] Pairs = new Pair[64];
    public Box[] Filled = new Box[64];
    public Holder Holder = new Holder();
    public Dictionary<string, Box> Dict = new Dictionary<string, Box>();
    public Box[] Big = new Box[5000];  // a large object
    public Box[] Resized = new Box[40];
}

static class Program
{
    static long s_sink;

    // Garbage in the same size classes as Box and its strings, so freed slots are reused at once.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Churn(int megabytes)
    {
        long bytes = (long)megabytes * 1024 * 1024;
        for (long done = 0; done < bytes; done += 64)
        {
            var a = new int[4];
            a[0] = -1; a[1] = -2; a[2] = -3; a[3] = (int)done;
            var b = new object[1];
            b[0] = a;
            s_sink += a.Length + b.Length;
        }
    }

    // Overwrites the stack below the caller, so no stale copy of a young reference stays visible to
    // the conservative scan once the helpers that made them have returned.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Scrub()
    {
        Span<long> words = stackalloc long[4096];
        words.Fill(1);
        s_sink += words[100];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void FillYoung(Targets t)
    {
        var src = new object[48];
        for (var i = 0; i < src.Length; i++) src[i] = new Box(i);
        src.AsSpan().CopyTo(t.Span);
        var src2 = new Box[16];
        for (var i = 0; i < src2.Length; i++) src2[i] = new Box(500 + i);
        new ReadOnlySpan<object>(src2).TryCopyTo(t.Span.AsSpan(48));

        var kv = new KeyValuePair<string, Box>[64];
        for (var i = 0; i < kv.Length; i++) kv[i] = new KeyValuePair<string, Box>("k" + i, new Box(1000 + i));
        Array.Copy(kv, t.Kv, kv.Length);

        for (var i = 0; i < t.Pairs.Length; i++) t.Pairs[i] = new Pair { Key = "p" + i, Value = new Box(2000 + i), Tag = i };
        Array.Fill(t.Filled, new Box(3000));
        t.Holder.Inline = new Pair { Key = "inline", Value = new Box(4000), Tag = 7 };
        t.Holder.Kv = new KeyValuePair<string, Box>("kv", new Box(4001));
        for (var i = 0; i < 300; i++) t.Dict["d" + i] = new Box(6000 + i);
        for (var i = 0; i < t.Big.Length; i += 7) t.Big[i] = new Box(7000 + i);
        for (var i = 0; i < t.Resized.Length; i++) t.Resized[i] = new Box(8000 + i);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Grow(Targets t)
    {
        Array.Resize(ref t.Resized, 3000);  // a new (large) array holding the old young elements
        for (var i = 40; i < 60; i++) t.Resized[i] = new Box(8000 + i);
    }

    static int Count(bool ok, ref int bad) { if (!ok) bad++; return ok ? 1 : 0; }

    static void Main()
    {
        var t = new Targets();
        Churn(80);  // the containers are old now
        FillYoung(t);
        Scrub();
        Churn(80);  // minor collections: what they miss is freed
        Grow(t);
        Scrub();
        Churn(80);
        GC.Collect();  // a full collection hands the freed slots out again...
        Churn(80);     // ...to garbage that overwrites them

        int bad = 0, good = 0;
        for (var i = 0; i < 48; i++) good += Count(t.Span[i] is Box b && b.Ok(i), ref bad);
        for (var i = 48; i < 64; i++) good += Count(t.Span[i] is Box b && b.Ok(500 + i - 48), ref bad);
        for (var i = 0; i < 64; i++) good += Count(t.Kv[i].Key == "k" + i && t.Kv[i].Value.Ok(1000 + i), ref bad);
        for (var i = 0; i < 64; i++) good += Count(t.Pairs[i].Key == "p" + i && t.Pairs[i].Value.Ok(2000 + i) && t.Pairs[i].Tag == i, ref bad);
        for (var i = 0; i < 64; i++) good += Count(t.Filled[i].Ok(3000), ref bad);
        good += Count(t.Holder.Inline.Key == "inline" && t.Holder.Inline.Value.Ok(4000), ref bad);
        good += Count(t.Holder.Kv.Key == "kv" && t.Holder.Kv.Value.Ok(4001), ref bad);
        for (var i = 0; i < 300; i++) good += Count(t.Dict["d" + i].Ok(6000 + i), ref bad);
        for (var i = 0; i < t.Big.Length; i += 7) good += Count(t.Big[i].Ok(7000 + i), ref bad);
        for (var i = 0; i < 60; i++) good += Count(t.Resized[i].Ok(8000 + i), ref bad);
        Console.WriteLine($"good {good}, bad {bad}, resized {t.Resized.Length}");
        Console.WriteLine(s_sink > 0);
    }
}
