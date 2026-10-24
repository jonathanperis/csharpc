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
