// System.Collections.BitArray: construction, get/set, logic, shifts across Int32 boundaries, Length changes,
// CopyTo into each supported array type, counts, the enumerator, and every error path's type and message.
using System;
using System.Collections;
using System.Linq;

class Program
{
    static string S(BitArray b) => new string(b.Cast<bool>().Select(x => x ? '1' : '0').ToArray());

    static void Try(string what, Action a)
    {
        try { a(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var a = new BitArray(new[] { true, false, true, true, false });
        Console.WriteLine($"{S(a)} {a.Count} {a.Length} {a[2]} {a.Get(1)} {a.HasAllSet()} {a.HasAnySet()}");
        var bytes = new BitArray(new byte[] { 0x81, 0x03 });
        Console.WriteLine(S(bytes));
        var ints = new BitArray(new[] { -1, 5 });
        Console.WriteLine(S(ints));
        Console.WriteLine(S(new BitArray(3, true)) + " " + S(new BitArray(0)) + " " + new BitArray(0).HasAllSet() + " " + new BitArray(0).HasAnySet());

        var x = new BitArray(new[] { true, true, false, false });
        var y = new BitArray(new[] { true, false, true, false });
        Console.WriteLine(S(((BitArray)x.Clone()).And(y)) + " " + S(((BitArray)x.Clone()).Or(y)) + " " + S(((BitArray)x.Clone()).Xor(y)) + " " + S(((BitArray)x.Clone()).Not()));

        var big = new BitArray(70);
        big[0] = big[33] = big[65] = big[69] = true;
        Console.WriteLine(S(((BitArray)big.Clone()).LeftShift(3)));
        Console.WriteLine(S(((BitArray)big.Clone()).RightShift(33)));
        Console.WriteLine(S(((BitArray)big.Clone()).LeftShift(32)));
        Console.WriteLine(S(((BitArray)big.Clone()).RightShift(64)));
        Console.WriteLine(S(((BitArray)big.Clone()).LeftShift(100)));
        Console.WriteLine(S(((BitArray)big.Clone()).RightShift(0)));

        var len = new BitArray(new[] { true, true, true });
        len.Length = 6; Console.WriteLine(S(len));
        len.Length = 2; Console.WriteLine(S(len));
        len.Length = 4; Console.WriteLine(S(len));

        var src = new BitArray(new[] { true, false, true, true, false, true, true, true, true, false });
        var iarr = new int[3]; src.CopyTo(iarr, 1); Console.WriteLine(string.Join(",", iarr));
        var barr = new byte[3]; src.CopyTo(barr, 0); Console.WriteLine(string.Join(",", barr));
        var bools = new bool[12]; src.CopyTo(bools, 2); Console.WriteLine(string.Join("", bools.Select(b => b ? 1 : 0)));

        var e = src.GetEnumerator();
        Try("Current before MoveNext", () => _ = e.Current);
        e.MoveNext(); Console.WriteLine(e.Current);
        while (e.MoveNext()) { }
        Try("Current after end", () => _ = e.Current);
        src[0] = false;
        Try("MoveNext after change", () => e.MoveNext());
        Try("Reset after change", () => e.Reset());

        Try("Get(-1)", () => src.Get(-1));
        Try("Set(10)", () => src.Set(10, true));
        Try("new BitArray(-1)", () => new BitArray(-1));
        Try("And(different length)", () => src.And(new BitArray(3)));
        Try("CopyTo(long[])", () => src.CopyTo(new long[10], 0));
        Try("CopyTo(int[] too small)", () => src.CopyTo(new int[1], 1));
        Try("CopyTo(bool[] too small)", () => src.CopyTo(new bool[5], 0));
        Try("CopyTo(int[,])", () => src.CopyTo(new int[2, 2], 0));
        Try("CopyTo(null)", () => src.CopyTo(null!, 0));
        Try("CopyTo(index -1)", () => src.CopyTo(new int[4], -1));
        Try("Length = -1", () => src.Length = -1);
        Try("RightShift(-1)", () => src.RightShift(-1));
        Try("new BitArray((int[])null)", () => new BitArray((int[])null!));
    }
}
