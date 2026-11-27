// Unary operators through a type parameter (-x, checked(-x), +x, ~x where T implements the operator interfaces): on a user struct and on
// the .NET 11 number types csharpc serves from the framework's IL (BFloat16, Decimal32/64/128). Both used to be "operator ... not
// supported" diagnostics: the interface's static abstract operator was looked up as a library operator.
using System;
using System.Globalization;
using System.Numerics;

static class Program
{
    static T Negate<T>(T x) where T : IUnaryNegationOperators<T, T> => -x;
    static T Plus<T>(T x) where T : IUnaryPlusOperators<T, T> => +x;
    static T Complement<T>(T x) where T : IBitwiseOperators<T, T, T> => ~x;
    static T CheckedNegate<T>(T x) where T : IUnaryNegationOperators<T, T> => checked(-x);

    static string Text<T>(T v) where T : INumberBase<T> => v.ToString(null, CultureInfo.InvariantCulture);

    static void Show<T>(string name, T x) where T : INumberBase<T> =>
        Console.WriteLine($"{name}: -x={Text(Negate(x))} +x={Text(Plus(x))} checked -x={Text(CheckedNegate(x))}");

    static void Main()
    {
        Show("Money", new Money(1250));
        Show("BFloat16", (BFloat16)2.5f);
        Show("Decimal32", Decimal32.Parse("-7.25", CultureInfo.InvariantCulture));
        Show("Decimal64", Decimal64.Parse("0", CultureInfo.InvariantCulture));
        Show("Decimal128", Decimal128.Parse("123456789012345678901234.5", CultureInfo.InvariantCulture));
        Show("int", 41);
        Console.WriteLine($"~Flags: {Complement(new Flags(0b1010)).Bits}");
        try { Console.WriteLine(CheckedNegate(new Money(long.MinValue))); }
        catch (OverflowException e) { Console.WriteLine("checked -Money.Min: " + e.GetType().Name); }
    }
}

readonly struct Money(long cents) : INumberBase<Money>
{
    public readonly long Cents = cents;
    public static Money operator -(Money a) => new(-a.Cents);
    public static Money operator checked -(Money a) => new(checked(-a.Cents));
    public static Money operator +(Money a) => a;
    public static Money operator ++(Money a) => new(a.Cents + 1);
    public static Money operator --(Money a) => new(a.Cents - 1);
    public static Money operator +(Money a, Money b) => new(a.Cents + b.Cents);
    public static Money operator -(Money a, Money b) => new(a.Cents - b.Cents);
    public static Money operator *(Money a, Money b) => new(a.Cents * b.Cents / 100);
    public static Money operator /(Money a, Money b) => new(a.Cents * 100 / b.Cents);
    public static bool operator ==(Money a, Money b) => a.Cents == b.Cents;
    public static bool operator !=(Money a, Money b) => a.Cents != b.Cents;
    public override string ToString() => ToString(null, null);
    public string ToString(string? format, IFormatProvider? provider) => (Cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var s = ToString(null, provider);
        charsWritten = s.Length <= destination.Length ? s.Length : 0;
        return s.AsSpan().TryCopyTo(destination);
    }
    public bool Equals(Money other) => Cents == other.Cents;
    public override bool Equals(object? obj) => obj is Money m && Equals(m);
    public override int GetHashCode() => Cents.GetHashCode();
    public static Money One => new(100);
    public static int Radix => 10;
    public static Money Zero => new(0);
    public static Money AdditiveIdentity => Zero;
    public static Money MultiplicativeIdentity => One;
    public static Money Abs(Money v) => new(Math.Abs(v.Cents));
    public static bool IsCanonical(Money v) => true;
    public static bool IsComplexNumber(Money v) => false;
    public static bool IsEvenInteger(Money v) => v.Cents % 200 == 0;
    public static bool IsFinite(Money v) => true;
    public static bool IsImaginaryNumber(Money v) => false;
    public static bool IsInfinity(Money v) => false;
    public static bool IsInteger(Money v) => v.Cents % 100 == 0;
    public static bool IsNaN(Money v) => false;
    public static bool IsNegative(Money v) => v.Cents < 0;
    public static bool IsNegativeInfinity(Money v) => false;
    public static bool IsNormal(Money v) => v.Cents != 0;
    public static bool IsOddInteger(Money v) => IsInteger(v) && !IsEvenInteger(v);
    public static bool IsPositive(Money v) => v.Cents >= 0;
    public static bool IsPositiveInfinity(Money v) => false;
    public static bool IsRealNumber(Money v) => true;
    public static bool IsSubnormal(Money v) => false;
    public static bool IsZero(Money v) => v.Cents == 0;
    public static Money MaxMagnitude(Money x, Money y) => Math.Abs(x.Cents) >= Math.Abs(y.Cents) ? x : y;
    public static Money MaxMagnitudeNumber(Money x, Money y) => MaxMagnitude(x, y);
    public static Money MinMagnitude(Money x, Money y) => Math.Abs(x.Cents) <= Math.Abs(y.Cents) ? x : y;
    public static Money MinMagnitudeNumber(Money x, Money y) => MinMagnitude(x, y);
    public static Money Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => new((long)(decimal.Parse(s, style, provider) * 100));
    public static Money Parse(string s, NumberStyles style, IFormatProvider? provider) => Parse(s.AsSpan(), style, provider);
    public static Money Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Number, provider);
    public static Money Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), NumberStyles.Number, provider);
    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Money result)
    {
        var ok = decimal.TryParse(s, style, provider, out var d);
        result = new((long)(d * 100));
        return ok;
    }
    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Money result) => TryParse(s.AsSpan(), style, provider, out result);
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Money result) => TryParse(s, NumberStyles.Number, provider, out result);
    public static bool TryParse(string? s, IFormatProvider? provider, out Money result) => TryParse(s.AsSpan(), NumberStyles.Number, provider, out result);
    static bool INumberBase<Money>.TryConvertFromChecked<TOther>(TOther value, out Money result) { result = default; return false; }
    static bool INumberBase<Money>.TryConvertFromSaturating<TOther>(TOther value, out Money result) { result = default; return false; }
    static bool INumberBase<Money>.TryConvertFromTruncating<TOther>(TOther value, out Money result) { result = default; return false; }
    static bool INumberBase<Money>.TryConvertToChecked<TOther>(Money value, out TOther result) { result = default!; return false; }
    static bool INumberBase<Money>.TryConvertToSaturating<TOther>(Money value, out TOther result) { result = default!; return false; }
    static bool INumberBase<Money>.TryConvertToTruncating<TOther>(Money value, out TOther result) { result = default!; return false; }
}

readonly struct Flags(int bits) : IBitwiseOperators<Flags, Flags, Flags>
{
    public readonly int Bits = bits;
    public static Flags operator ~(Flags a) => new(~a.Bits & 0xF);
    public static Flags operator &(Flags a, Flags b) => new(a.Bits & b.Bits);
    public static Flags operator |(Flags a, Flags b) => new(a.Bits | b.Bits);
    public static Flags operator ^(Flags a, Flags b) => new(a.Bits ^ b.Bits);
}
