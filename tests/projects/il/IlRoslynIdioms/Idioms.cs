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
}
