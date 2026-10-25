using System.Runtime.InteropServices;

namespace IlRoslynIdioms;

/// <summary>Roslyn's ExtendedSpecialType: an explicit layout that is the sequential one (one byte, Size = 1).</summary>
[StructLayout(LayoutKind.Explicit, Size = 1)]
public readonly struct Special : IEquatable<Special>
{
    [FieldOffset(0)] private readonly byte _value;
    public Special(int value) => _value = (byte)value;
    public static implicit operator Special(int value) => new(value);
    public bool Equals(Special other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Special other && Equals(other);
    public override int GetHashCode() => _value;
    public override string ToString() => $"Special({_value})";
    public static unsafe int Size => sizeof(Special);
}

public static class Decimals
{
    // C# compiles 0m, 1m and -1m (and decimal.MaxValue/MinValue) as loads of decimal's static fields.
    public static decimal Zero => 0m;
    public static decimal One => 1m;
    public static decimal MinusOne => -1m;
    public static decimal Max => decimal.MaxValue;
    public static decimal Min => decimal.MinValue;
    public static decimal Sum(IEnumerable<decimal> values)
    {
        var total = 0m;
        foreach (var v in values) total += v;
        return total == 0m ? -1m : total;
    }

    // Roslyn's ConstantValue round-trips decimals through their bits.
    public static decimal FromBits(decimal d) => new decimal(decimal.GetBits(d));
}

/// <summary>Rectangular arrays (Roslyn's MethodTypeInferrer keeps a Dependency[,] matrix): IL calls T[,]::.ctor/Get/Set/Address.</summary>
public static class Matrices
{
    public enum Dependency : byte { Unknown, NotDependent, Direct, Indirect }

    public struct Cell { public int Row; public long Weight; }

    public static string Closure(int size)
    {
        var deps = new Dependency[size, size];
        for (var i = 0; i < size; i++)
            for (var j = 0; j < size; j++)
                deps[i, j] = i == j ? Dependency.NotDependent : j == i + 1 ? Dependency.Direct : Dependency.Unknown;
        for (var k = 0; k < size; k++)
            for (var i = 0; i < size; i++)
                for (var j = 0; j < size; j++)
                    if (deps[i, j] == Dependency.Unknown && deps[i, k] != Dependency.Unknown && deps[i, k] != Dependency.NotDependent
                        && deps[k, j] != Dependency.Unknown && deps[k, j] != Dependency.NotDependent)
                        deps[i, j] = Dependency.Indirect;
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++) text.Append((int)deps[i, j]);
            text.Append('|');
        }
        return text.ToString() + deps.GetLength(0) + "x" + deps.GetLength(1) + " " + deps.Length;
    }

    public static long Cells()
    {
        var cells = new Cell[2, 3];
        ref var cell = ref cells[1, 2];
        cell.Row = 1;
        cell.Weight = 40;
        cells[0, 1].Weight += 2;
        var bytes = new byte[2, 2];
        bytes[1, 1] = 200;
        var cube = new int[2, 2, 2];
        cube[1, 0, 1] = 7;
        return cells[1, 2].Weight + cells[0, 1].Weight + bytes[1, 1] + cube[1, 0, 1] + cells[1, 2].Row;
    }

    public static string OutOfRange()
    {
        var grid = new int[2, 3];
        try { return grid[2, 0].ToString(); }
        catch (IndexOutOfRangeException e) { return e.Message; }
    }
}

/// <summary>Collection expressions and params spans over non-constant elements: the compiler's &lt;&gt;y__InlineArrayN&lt;T&gt;.</summary>
public static class InlineArrays
{
    public static string Join(string a, string b, string c, string d, string e)
    {
        ReadOnlySpan<string> parts = [a, b, c, d, e];
        var text = "";
        foreach (var part in parts) text += part;
        return text + parts.Length;
    }

    public static int Sum(params ReadOnlySpan<int> values)
    {
        var sum = 0;
        foreach (var v in values) sum += v;
        return sum;
    }

    public static int SumOf(int x, int y, int z) => Sum(x, y, z, x * y, y * z, 1, 2);

    // More elements than the BCL's InlineArray2..16 cover: the compiler synthesizes <>y__InlineArray18<T>
    // (Roslyn's own assemblies, built by an older compiler, have them for small N too).
    public static int Many(int x) => Sum(x, x + 1, x + 2, x + 3, x + 4, x + 5, x + 6, x + 7, x + 8, x + 9,
        x + 10, x + 11, x + 12, x + 13, x + 14, x + 15, x + 16, x + 17);

    public static string Describe(object first, object second) => string.Join("|", (ReadOnlySpan<object>)[first, second, first]);
}

/// <summary>Roslyn's SegmentedArray&lt;T&gt; / the compiler's &lt;&gt;z__ReadOnlyArray&lt;T&gt;: interface members forwarded to a T[] field.</summary>
public sealed class ArrayWrapper<T> : IList<T>, System.Collections.IList
{
    private readonly T[] _items;
    public ArrayWrapper(T[] items) => _items = items;
    public T this[int index] { get => _items[index]; set => _items[index] = value; }
    public int Count => _items.Length;
    public bool IsReadOnly => true;
    public void Add(T item) => throw new NotSupportedException();
    public void Clear() => ((ICollection<T>)_items).Clear();
    public bool Contains(T item) => ((ICollection<T>)_items).Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => ((ICollection<T>)_items).CopyTo(array, arrayIndex);
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    public int IndexOf(T item) => ((IList<T>)_items).IndexOf(item);
    public void Insert(int index, T item) => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    int System.Collections.IList.Add(object? value) => throw new NotSupportedException();
    void System.Collections.IList.Clear() => ((System.Collections.IList)_items).Clear();
    bool System.Collections.IList.Contains(object? value) => ((System.Collections.IList)_items).Contains(value);
    int System.Collections.IList.IndexOf(object? value) => ((System.Collections.IList)_items).IndexOf(value);
    void System.Collections.IList.Insert(int index, object? value) => throw new NotSupportedException();
    void System.Collections.IList.Remove(object? value) => throw new NotSupportedException();
    void System.Collections.IList.RemoveAt(int index) => throw new NotSupportedException();
    bool System.Collections.IList.IsFixedSize => true;
    object? System.Collections.IList.this[int index] { get => _items[index]; set => _items[index] = (T)value!; }
    void System.Collections.ICollection.CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
    bool System.Collections.ICollection.IsSynchronized => false;
    object System.Collections.ICollection.SyncRoot => this;
}

/// <summary>Fixed-size buffers, as in Roslyn's copy of XxHash128's State (accumulators, buffer, secret).</summary>
public unsafe struct HashState
{
    public fixed ulong Accumulators[8];
    public fixed byte Buffer[20];
    public int Length;

    public static string Run()
    {
        var state = new HashState();
        for (var i = 0; i < 8; i++) state.Accumulators[i] = (ulong)(i * i) << 33;
        for (var i = 0; i < 20; i++) state.Buffer[i] = (byte)(i * 7);
        state.Length = 20;
        var copy = state; // the buffers are part of the value
        copy.Accumulators[0] = 99;
        ulong sum = 0;
        ulong* acc = state.Accumulators;
            for (var i = 0; i < 8; i++) sum += acc[i] >> 33;
        return $"{sum} {state.Accumulators[0]} {copy.Accumulators[0]} {state.Buffer[19]} {copy.Buffer[3]} {sizeof(HashState)} {state.Length}";
    }
}

/// <summary>Roslyn's RealParser.FloatUnion / DoubleUnion: reinterpreting bits through overlapping fields.</summary>
public static class Unions
{
    [StructLayout(LayoutKind.Explicit)]
    private struct FloatUnion
    {
        [FieldOffset(0)] public uint IntData;
        [FieldOffset(0)] public float FloatData;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DoubleUnion
    {
        [FieldOffset(0)] public double DoubleData;
        [FieldOffset(0)] public ulong LongData;
        [FieldOffset(0)] public byte FirstByte;
    }

    public static string Run()
    {
        var f = new FloatUnion { IntData = 0x3FC00000 };
        var g = f;
        g.FloatData = -2.5f;
        var d = new DoubleUnion { DoubleData = 1.0 };
        return $"{f.FloatData} {g.IntData:X8} {d.LongData:X16} {d.FirstByte} {unsafeSize()}";

        static unsafe int unsafeSize() => sizeof(FloatUnion) * 100 + sizeof(DoubleUnion);
    }
}

/// <summary>Pointer idioms of Roslyn's hashing and event-source code.</summary>
public static unsafe class Pointers
{
    // `fixed` over a string: the string's address plus RuntimeHelpers.OffsetToStringData.
    public static int SumChars(string s)
    {
        var sum = 0;
        fixed (char* p = s)
            for (var i = 0; i < s.Length; i++) sum += p[i];
        return sum;
    }

    // MemoryMarshal.GetReference / GetArrayDataReference + Unsafe.Add (System.Collections.Frozen's hashing).
    public static int SumBytes(ReadOnlySpan<byte> span)
    {
        ref byte first = ref MemoryMarshal.GetReference(span);
        var sum = 0;
        for (var i = 0; i < span.Length; i++) sum += System.Runtime.CompilerServices.Unsafe.Add(ref first, i);
        return sum;
    }

    public static int FirstOf(int[] values) => MemoryMarshal.GetArrayDataReference(values);

    // `fixed` over a span: ReadOnlySpan<T>.GetPinnableReference (a null pointer for an empty span).
    public static string Pinned(ReadOnlySpan<byte> span)
    {
        fixed (byte* p = span)
            return p == null ? "null" : (p[0] + p[span.Length - 1]).ToString();
    }

    // A library static readonly field passed by reference: `ldsflda DateTime::MinValue`.
    private static long TicksOf(in DateTime value) => value.Ticks;
    public static long MinTicks() => TicksOf(in DateTime.MinValue) + TicksOf(in DateTime.MaxValue) % 1000;
}

/// <summary>
/// Calls the C# compiler lowers through boxes and function pointers: `HasFlag` on two boxed enums,
/// delegates bound with `ldvirtftn` to interface and virtual methods (a null receiver throws
/// NullReferenceException there), ValueType.Equals over a struct holding a CancellationToken.
/// </summary>
public static class DelegateIdioms
{
    [Flags] public enum Access { None = 0, Read = 1, Write = 2, Both = 3 }
    public interface ISource { int Next(); string Name(string prefix); }
    public sealed class Counter : ISource { private int _n; public int Next() => ++_n; public string Name(string prefix) => prefix + "counter"; }
    public class Shape { public virtual string Kind() => "shape"; }
    public sealed class Circle : Shape { public override string Kind() => "circle"; }
    public struct Pending { public System.Threading.CancellationToken Token; public int Id; }

    public static bool Has(Access value, Access flag) => value.HasFlag(flag);

    public static string Bind(ISource source)
    {
        Func<int> next = source.Next;
        Func<string, string> name = source.Name;
        return $"{next()} {next()} {name("a ")}";
    }

    public static string Kind(Shape shape) { Func<string> kind = shape.Kind; return kind(); }

    public static string BindNull()
    {
        ISource? none = null;
        try { Func<int> next = none!.Next; return "bound"; }
        catch (NullReferenceException) { return "NullReferenceException"; }
    }

    public static string Equal()
    {
        var source = new System.Threading.CancellationTokenSource();
        object a = new Pending { Token = source.Token, Id = 1 }, b = new Pending { Token = source.Token, Id = 1 };
        object c = new Pending { Token = default, Id = 1 };
        return $"{a.Equals(b)} {a.Equals(c)} {c.Equals(new Pending { Id = 1 })}";
    }
}
