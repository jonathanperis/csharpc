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
}
