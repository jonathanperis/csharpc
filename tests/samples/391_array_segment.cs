// System.ArraySegment<T>: construction and validation, indexer, enumeration (struct and interface),
// Slice/ToArray/CopyTo, equality, the IList/IReadOnlyList/ICollection views, default segments,
// conversions to spans and MemoryExtensions.AsSpan/AsMemory, and BitConverter.ToString.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine($"{label}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void TryAction(string label, Action f)
    {
        try { f(); Console.WriteLine($"{label}: ok"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static string Show<T>(IEnumerable<T> items) => "[" + string.Join(", ", items) + "]";

    static int Sum(ReadOnlySpan<int> span)
    {
        int s = 0;
        foreach (var x in span) s += x;
        return s;
    }

    static void Main()
    {
        int[] data = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var seg = new ArraySegment<int>(data, 2, 5);
        Console.WriteLine($"{seg.Offset} {seg.Count} {seg.Array == data} {seg[0]} {seg[4]}");
        foreach (var x in seg) Console.Write(x + " ");
        Console.WriteLine();
        seg[1] = 30;
        Console.WriteLine(data[3]);
        Try("index 5", () => seg[5]);
        Try("index -1", () => seg[-1]);

        ArraySegment<int> whole = data;
        Console.WriteLine($"{whole.Offset} {whole.Count}");
        var full = new ArraySegment<int>(data);
        Console.WriteLine(full == whole);
        Console.WriteLine(full.Equals(new ArraySegment<int>(data, 0, 10)) + " " + full.Equals(seg) + " " + (full != seg));
        Console.WriteLine(full.Equals((object)whole) + " " + full.Equals((object)data));
        Console.WriteLine(full.GetHashCode() == whole.GetHashCode());

        Try("ctor null", () => new ArraySegment<int>(null));
        Try("ctor null 3", () => new ArraySegment<int>(null, 0, 0));
        Try("ctor offset", () => new ArraySegment<int>(data, -1, 2));
        Try("ctor count", () => new ArraySegment<int>(data, 1, -2));
        Try("ctor window", () => new ArraySegment<int>(data, 8, 3));
        Try("ctor end", () => new ArraySegment<int>(data, 10, 0).Count);

        // Slice / ToArray / CopyTo.
        Console.WriteLine(Show(seg.Slice(2)));
        Console.WriteLine(Show(seg.Slice(1, 3)));
        Console.WriteLine(seg.Slice(5).Count);
        Try("slice 6", () => seg.Slice(6));
        Try("slice 2,4", () => seg.Slice(2, 4));
        Console.WriteLine(Show(seg.ToArray()));
        var dest = new int[7];
        seg.CopyTo(dest);
        Console.WriteLine(Show(dest));
        seg.CopyTo(dest, 2);
        Console.WriteLine(Show(dest));
        var destSeg = new ArraySegment<int>(new int[6], 1, 5);
        seg.CopyTo(destSeg);
        Console.WriteLine(Show(destSeg.Array));
        TryAction("copy short", () => seg.CopyTo(new ArraySegment<int>(new int[4])));
        TryAction("copy to default", () => seg.CopyTo(default(ArraySegment<int>)));
        TryAction("copy array short", () => seg.CopyTo(new int[3]));
        var empty1 = new ArraySegment<int>(data, 3, 0).ToArray();
        var empty2 = ArraySegment<int>.Empty.ToArray();
        Console.WriteLine($"{empty1.Length} {ReferenceEquals(empty1, empty2)} {ArraySegment<int>.Empty.Count} {ArraySegment<int>.Empty.Array.Length}");
        Console.WriteLine(ArraySegment<int>.Empty == ArraySegment<int>.Empty);

        // Default segments.
        ArraySegment<string> none = default;
        Console.WriteLine($"{none.Array == null} {none.Offset} {none.Count}");
        Try("default enumerate", () => none.GetEnumerator());
        Try("default ToArray", () => none.ToArray());
        Try("default Slice", () => none.Slice(0));
        Try("default index", () => none[0]);
        Console.WriteLine(none.GetHashCode());
        Console.WriteLine(none == default(ArraySegment<string>));
        ArraySegment<string> fromNull = (string[])null;
        Console.WriteLine(fromNull.Array == null);
        Console.WriteLine(none.AsSpan().Length + " " + ((ReadOnlySpan<string>)none).IsEmpty);
        Try("default IList", () => ((IList<string>)none).Count);
        Try("default IList index", () => ((IList<string>)none)[0]);

        // Interfaces (boxed segment).
        IList<int> list = seg;
        Console.WriteLine($"{list.Count} {list[1]} {list.IndexOf(4)} {list.IndexOf(0)} {list.Contains(5)} {list.Contains(9)} {list.IsReadOnly}");
        list[0] = 20;
        Console.WriteLine(data[2]);
        TryAction("IList.Add", () => list.Add(1));
        TryAction("IList.Insert", () => list.Insert(0, 1));
        TryAction("IList.RemoveAt", () => list.RemoveAt(0));
        TryAction("IList.Clear", () => list.Clear());
        Try("IList.Remove", () => list.Remove(4));
        Try("IList index", () => list[7]);
        IReadOnlyList<int> rol = seg;
        Console.WriteLine($"{rol.Count} {rol[2]}");
        Try("IReadOnlyList index", () => rol[-1]);
        var copy = new int[8];
        ((ICollection<int>)seg).CopyTo(copy, 1);
        Console.WriteLine(Show(copy));
        Console.WriteLine(Show(seg.Where(x => x % 2 == 0)));
        Console.WriteLine(seg.Sum() + " " + seg.Max() + " " + seg.Count() + " " + seg.First() + " " + seg.Last());
        Console.WriteLine(Show(new List<int>(seg)));
        IEnumerable nonGeneric = seg;
        foreach (object o in nonGeneric) Console.Write(o + ";");
        Console.WriteLine();

        // Enumerator protocol.
        var e = seg.GetEnumerator();
        Try("Current before", () => e.Current);
        while (e.MoveNext()) Console.Write(e.Current + ",");
        Console.WriteLine();
        Try("Current after", () => e.Current);
        Console.WriteLine(e.MoveNext());
        IEnumerator<int> ie = ((IEnumerable<int>)seg).GetEnumerator();
        ie.MoveNext();
        ie.MoveNext();
        Console.WriteLine(ie.Current);
        ie.Reset();
        ie.MoveNext();
        Console.WriteLine(ie.Current);
        ie.Dispose();
        var emptyEnum = ((IEnumerable<int>)new ArraySegment<int>(data, 4, 0)).GetEnumerator();
        Console.WriteLine(emptyEnum.MoveNext());

        // Spans and memory.
        Span<int> span = seg;
        span[0] = 200;
        Console.WriteLine($"{span.Length} {data[2]} {Sum(seg)}");
        ReadOnlySpan<int> ro = seg.Slice(1, 2);
        Console.WriteLine(ro.Length + " " + ro[1]);
        Console.WriteLine(Show(seg.AsSpan().ToArray()));
        Console.WriteLine(Show(seg.AsSpan(3).ToArray()));
        Console.WriteLine(Show(seg.AsSpan(1, 2).ToArray()));
        Console.WriteLine(Show(seg.AsSpan(^2).ToArray()));
        Console.WriteLine(Show(seg.AsSpan(1..^1).ToArray()));
        Try("AsSpan 6", () => seg.AsSpan(6).Length);
        Try("AsSpan 2,4", () => seg.AsSpan(2, 4).Length);
        Memory<int> mem = seg.AsMemory(1);
        Console.WriteLine(mem.Length + " " + mem.Span[0]);
        Console.WriteLine(seg.AsMemory().Length + " " + seg.AsMemory(1, 1).Span[0]);
        Try("AsMemory -1", () => seg.AsMemory(-1).Length);
        Memory<int> implicitMem = seg;
        ReadOnlyMemory<int> implicitRoMem = seg;
        Console.WriteLine(implicitMem.Length + " " + implicitRoMem.Span[4]);

        // Other element types.
        var words = new ArraySegment<string>(new[] { "a", "b", "c", "d" }, 1, 2);
        Console.WriteLine(Show(words) + " " + ((IList<string>)words).IndexOf("c") + " " + ((ICollection<string>)words).Contains("a"));
        var bytes = new byte[] { 0x00, 0x7F, 0xAB, 0xFF, 0x10 };
        var byteSeg = new ArraySegment<byte>(bytes, 1, 3);
        Console.WriteLine(BitConverter.ToString(byteSeg.ToArray()));
        Console.WriteLine(BitConverter.ToString(bytes) + "|" + BitConverter.ToString(bytes, 3) + "|" + BitConverter.ToString(bytes, 1, 2) + "|" + BitConverter.ToString(new byte[0]) + "|");
        Try("ToString null", () => BitConverter.ToString(null));
        Try("ToString start", () => BitConverter.ToString(bytes, 5));
        Try("ToString len", () => BitConverter.ToString(bytes, 1, -1));
        Try("ToString window", () => BitConverter.ToString(bytes, 3, 3));
        Try("ToString end", () => BitConverter.ToString(bytes, 5, 0));
        Console.WriteLine(BitConverter.ToString(new byte[0], 0, 0) + "|");

        var segs = new List<ArraySegment<int>> { seg, whole, seg };
        Console.WriteLine(segs.IndexOf(whole) + " " + segs.LastIndexOf(seg) + " " + segs.Contains(new ArraySegment<int>(data, 2, 4)));
        var grid = new ArraySegment<int>[2];
        grid[1] = seg;
        Console.WriteLine(grid[0].Array == null && grid[1].Count == 5);
        object boxed = seg;
        Console.WriteLine(boxed is ArraySegment<int> back && back == seg);
        Console.WriteLine(boxed is IReadOnlyList<int> ro2 ? ro2.Count : -1);
    }
}
