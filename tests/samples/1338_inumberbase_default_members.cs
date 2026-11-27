// INumberBase<TSelf>'s static default members on a type that doesn't implement them itself: UTF-8 Parse/TryParse/TryParsePartial
// (transcoded to the type's UTF-16 parser), IUtf8SpanParsable's Parse/TryParse, CreateChecked/Saturating/Truncating (through the type's
// TryConvert* members) and MultiplyAddEstimate. The reference pack declares them without a body; csharpc serves CoreLib's.
using System;
using System.Globalization;
using System.Numerics;
using System.Text;

static class Program
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static string ParseUtf8<T>(string s, NumberStyles style) where T : INumberBase<T>
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var ok = T.TryParse(bytes, style, Inv, out var value);
        string parsed;
        try { parsed = T.Parse(bytes, style, Inv).ToString(null, Inv); }
        catch (Exception e) when (e is FormatException or OverflowException) { parsed = e.GetType().Name; }
        return $"TryParse={ok} {value.ToString(null, Inv)} Parse={parsed}";
    }

    static string ParseUtf8Parsable<T>(string s) where T : IUtf8SpanParsable<T>, IFormattable
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var ok = T.TryParse(bytes, Inv, out var value);
        string parsed;
        try { parsed = T.Parse(bytes, Inv).ToString(null, Inv); }
        catch (Exception e) when (e is FormatException or OverflowException) { parsed = e.GetType().Name; }
        return $"TryParse={ok} {value?.ToString(null, Inv)} Parse={parsed}";
    }

    static string Partial<T>(string s) where T : INumberBase<T>
    {
        var ok16 = T.TryParsePartial(s.AsSpan(), NumberStyles.Integer, Inv, out var v16, out var consumed16);
        var ok8 = T.TryParsePartial(Encoding.UTF8.GetBytes(s), NumberStyles.Integer, Inv, out var v8, out var consumed8);
        return $"utf16 {ok16} {v16.ToString(null, Inv)} {consumed16}, utf8 {ok8} {v8.ToString(null, Inv)} {consumed8}";
    }

    static string Create<T, TOther>(TOther value) where T : INumberBase<T> where TOther : INumberBase<TOther>
    {
        string Try(Func<T> f) { try { return f().ToString(null, Inv); } catch (Exception e) { return e.GetType().Name; } }
        return $"checked={Try(() => T.CreateChecked(value))} saturating={Try(() => T.CreateSaturating(value))} truncating={Try(() => T.CreateTruncating(value))}";
    }

    static void Main()
    {
        foreach (var s in new[] { "42", " -17 ", "1,000", "abc", "", "99999999999" })
        {
            Console.WriteLine($"Small '{s}' Integer: {ParseUtf8<Small>(s, NumberStyles.Integer)}");
            Console.WriteLine($"Small '{s}' Thousands: {ParseUtf8<Small>(s, NumberStyles.Integer | NumberStyles.AllowThousands)}");
            Console.WriteLine($"Small '{s}' IUtf8SpanParsable: {ParseUtf8Parsable<Small>(s)}");
            Console.WriteLine($"Own '{s}': {ParseUtf8<Own>(s, NumberStyles.Integer)}");
        }
        // Non-ASCII and invalid UTF-8: the default member transcodes before parsing, and fails on an invalid sequence.
        Console.WriteLine($"Small fullwidth: {ParseUtf8<Small>("１", NumberStyles.Integer)}");
        Console.WriteLine($"Small invalid utf8: {TryUtf8<Small>(new byte[] { 0x31, 0xC3 })}");
        // Longer than the stack buffer (256 chars): the pooled path.
        Console.WriteLine($"Small long: {ParseUtf8<Small>(new string(' ', 300) + "7", NumberStyles.Integer)}");
        foreach (var s in new[] { "123abc", "-5 tail", "x1", "+8" }) Console.WriteLine($"Small partial '{s}': {Partial<Small>(s)}");

        foreach (var v in new[] { 0, 5, -5, 40000, int.MinValue }) Console.WriteLine($"Small from int {v}: {Create<Small, int>(v)}");
        foreach (var v in new[] { 1.9, -1.9, 1e10, double.NaN }) Console.WriteLine($"Small from double {v.ToString(Inv)}: {Create<Small, double>(v)}");
        foreach (var v in new[] { new Small(12), new Small(-300), new Small(32767) })
        {
            Console.WriteLine($"int from Small {v}: {Create<int, Small>(v)}");
            Console.WriteLine($"sbyte from Small {v}: {Create<sbyte, Small>(v)}");
        }
        Console.WriteLine($"MultiplyAddEstimate: {Mad(new Small(6), new Small(7), new Small(-2))} {Mad(new Small(-3), new Small(3), new Small(9))}");
    }

    static string TryUtf8<T>(byte[] bytes) where T : INumberBase<T> => T.TryParse(bytes, NumberStyles.Integer, Inv, out var v) + " " + v;

    static T Mad<T>(T a, T b, T c) where T : INumberBase<T> => T.MultiplyAddEstimate(a, b, c);
}

// A 16-bit number that implements only INumberBase's required members.
readonly struct Small(short value) : INumberBase<Small>
{
    public readonly short Value = value;
    public static Small One => new(1);
    public static int Radix => 2;
    public static Small Zero => new(0);
    public static Small AdditiveIdentity => Zero;
    public static Small MultiplicativeIdentity => One;
    public static Small Abs(Small v) => new(Math.Abs(v.Value));
    public static bool IsCanonical(Small v) => true;
    public static bool IsComplexNumber(Small v) => false;
    public static bool IsEvenInteger(Small v) => (v.Value & 1) == 0;
    public static bool IsFinite(Small v) => true;
    public static bool IsImaginaryNumber(Small v) => false;
    public static bool IsInfinity(Small v) => false;
    public static bool IsInteger(Small v) => true;
    public static bool IsNaN(Small v) => false;
    public static bool IsNegative(Small v) => v.Value < 0;
    public static bool IsNegativeInfinity(Small v) => false;
    public static bool IsNormal(Small v) => v.Value != 0;
    public static bool IsOddInteger(Small v) => (v.Value & 1) != 0;
    public static bool IsPositive(Small v) => v.Value >= 0;
    public static bool IsPositiveInfinity(Small v) => false;
    public static bool IsRealNumber(Small v) => true;
    public static bool IsSubnormal(Small v) => false;
    public static bool IsZero(Small v) => v.Value == 0;
    public static Small MaxMagnitude(Small x, Small y) => Math.Abs((int)x.Value) >= Math.Abs((int)y.Value) ? x : y;
    public static Small MaxMagnitudeNumber(Small x, Small y) => MaxMagnitude(x, y);
    public static Small MinMagnitude(Small x, Small y) => Math.Abs((int)x.Value) <= Math.Abs((int)y.Value) ? x : y;
    public static Small MinMagnitudeNumber(Small x, Small y) => MinMagnitude(x, y);

    public static Small Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => new(short.Parse(s, style, provider));
    public static Small Parse(string s, NumberStyles style, IFormatProvider? provider) => Parse(s.AsSpan(), style, provider);
    public static Small Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);
    public static Small Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), NumberStyles.Integer, provider);
    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Small result)
    {
        var ok = short.TryParse(s, style, provider, out var v);
        result = new(v);
        return ok;
    }
    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Small result) => TryParse(s.AsSpan(), style, provider, out result);
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Small result) => TryParse(s, NumberStyles.Integer, provider, out result);
    public static bool TryParse(string? s, IFormatProvider? provider, out Small result) => TryParse(s.AsSpan(), NumberStyles.Integer, provider, out result);

    static bool TryConvertFrom<TOther>(TOther value, out Small result, int mode) where TOther : INumberBase<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            var v = (int)(object)value;
            if (mode == 0 && (v < short.MinValue || v > short.MaxValue)) throw new OverflowException();
            result = new(mode == 1 ? (short)Math.Clamp(v, short.MinValue, short.MaxValue) : unchecked((short)v));
            return true;
        }
        if (typeof(TOther) == typeof(double))
        {
            var v = (double)(object)value;
            result = new(mode == 0 ? checked((short)v) : mode == 1 ? (short)Math.Clamp(double.IsNaN(v) ? 0 : v, short.MinValue, short.MaxValue) : unchecked((short)(long)(double.IsNaN(v) ? 0 : v)));
            return true;
        }
        result = default;
        return false;
    }
    static bool TryConvertTo<TOther>(Small value, out TOther result, int mode) where TOther : INumberBase<TOther>
    {
        if (typeof(TOther) == typeof(int)) { result = (TOther)(object)(int)value.Value; return true; }
        if (typeof(TOther) == typeof(sbyte))
        {
            var v = value.Value;
            if (mode == 0 && (v < sbyte.MinValue || v > sbyte.MaxValue)) throw new OverflowException();
            result = (TOther)(object)(mode == 1 ? (sbyte)Math.Clamp(v, sbyte.MinValue, sbyte.MaxValue) : unchecked((sbyte)v));
            return true;
        }
        result = default!;
        return false;
    }
    static bool INumberBase<Small>.TryConvertFromChecked<TOther>(TOther value, out Small result) => TryConvertFrom(value, out result, 0);
    static bool INumberBase<Small>.TryConvertFromSaturating<TOther>(TOther value, out Small result) => TryConvertFrom(value, out result, 1);
    static bool INumberBase<Small>.TryConvertFromTruncating<TOther>(TOther value, out Small result) => TryConvertFrom(value, out result, 2);
    static bool INumberBase<Small>.TryConvertToChecked<TOther>(Small value, out TOther result) => TryConvertTo(value, out result, 0);
    static bool INumberBase<Small>.TryConvertToSaturating<TOther>(Small value, out TOther result) => TryConvertTo(value, out result, 1);
    static bool INumberBase<Small>.TryConvertToTruncating<TOther>(Small value, out TOther result) => TryConvertTo(value, out result, 2);

    public static Small operator +(Small a, Small b) => new((short)(a.Value + b.Value));
    public static Small operator -(Small a, Small b) => new((short)(a.Value - b.Value));
    public static Small operator *(Small a, Small b) => new((short)(a.Value * b.Value));
    public static Small operator /(Small a, Small b) => new((short)(a.Value / b.Value));
    public static Small operator ++(Small a) => new((short)(a.Value + 1));
    public static Small operator --(Small a) => new((short)(a.Value - 1));
    public static Small operator +(Small a) => a;
    public static Small operator -(Small a) => new((short)-a.Value);
    public static bool operator ==(Small a, Small b) => a.Value == b.Value;
    public static bool operator !=(Small a, Small b) => a.Value != b.Value;
    public bool Equals(Small other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Small s && Equals(s);
    public override int GetHashCode() => Value;
    public override string ToString() => "S" + Value.ToString(CultureInfo.InvariantCulture);
    public string ToString(string? format, IFormatProvider? provider) => "S" + Value.ToString(format, provider);
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        if (destination.IsEmpty) return false;
        destination[0] = 'S';
        var ok = Value.TryFormat(destination[1..], out var written, format, provider);
        charsWritten = ok ? written + 1 : 0;
        return ok;
    }
}

// Implements the UTF-8 TryParse itself: its own member wins over INumberBase's default one.
readonly struct Own(int value) : INumberBase<Own>
{
    public readonly int Value = value;
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out Own result)
    {
        result = new(utf8Text.Length * 1000);
        return true;
    }
    public static Own Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider) => new(-utf8Text.Length);

    public static Own One => new(1);
    public static int Radix => 10;
    public static Own Zero => new(0);
    public static Own AdditiveIdentity => Zero;
    public static Own MultiplicativeIdentity => One;
    public static Own Abs(Own v) => new(Math.Abs(v.Value));
    public static bool IsCanonical(Own v) => true;
    public static bool IsComplexNumber(Own v) => false;
    public static bool IsEvenInteger(Own v) => (v.Value & 1) == 0;
    public static bool IsFinite(Own v) => true;
    public static bool IsImaginaryNumber(Own v) => false;
    public static bool IsInfinity(Own v) => false;
    public static bool IsInteger(Own v) => true;
    public static bool IsNaN(Own v) => false;
    public static bool IsNegative(Own v) => v.Value < 0;
    public static bool IsNegativeInfinity(Own v) => false;
    public static bool IsNormal(Own v) => v.Value != 0;
    public static bool IsOddInteger(Own v) => (v.Value & 1) != 0;
    public static bool IsPositive(Own v) => v.Value >= 0;
    public static bool IsPositiveInfinity(Own v) => false;
    public static bool IsRealNumber(Own v) => true;
    public static bool IsSubnormal(Own v) => false;
    public static bool IsZero(Own v) => v.Value == 0;
    public static Own MaxMagnitude(Own x, Own y) => x;
    public static Own MaxMagnitudeNumber(Own x, Own y) => x;
    public static Own MinMagnitude(Own x, Own y) => y;
    public static Own MinMagnitudeNumber(Own x, Own y) => y;
    public static Own Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => new(int.Parse(s, style, provider));
    public static Own Parse(string s, NumberStyles style, IFormatProvider? provider) => Parse(s.AsSpan(), style, provider);
    public static Own Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);
    public static Own Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), NumberStyles.Integer, provider);
    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Own result)
    {
        var ok = int.TryParse(s, style, provider, out var v);
        result = new(v);
        return ok;
    }
    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Own result) => TryParse(s.AsSpan(), style, provider, out result);
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Own result) => TryParse(s, NumberStyles.Integer, provider, out result);
    public static bool TryParse(string? s, IFormatProvider? provider, out Own result) => TryParse(s.AsSpan(), NumberStyles.Integer, provider, out result);
    static bool INumberBase<Own>.TryConvertFromChecked<TOther>(TOther value, out Own result) { result = default; return false; }
    static bool INumberBase<Own>.TryConvertFromSaturating<TOther>(TOther value, out Own result) { result = default; return false; }
    static bool INumberBase<Own>.TryConvertFromTruncating<TOther>(TOther value, out Own result) { result = default; return false; }
    static bool INumberBase<Own>.TryConvertToChecked<TOther>(Own value, out TOther result) { result = default!; return false; }
    static bool INumberBase<Own>.TryConvertToSaturating<TOther>(Own value, out TOther result) { result = default!; return false; }
    static bool INumberBase<Own>.TryConvertToTruncating<TOther>(Own value, out TOther result) { result = default!; return false; }
    public static Own operator +(Own a, Own b) => new(a.Value + b.Value);
    public static Own operator -(Own a, Own b) => new(a.Value - b.Value);
    public static Own operator *(Own a, Own b) => new(a.Value * b.Value);
    public static Own operator /(Own a, Own b) => new(a.Value / b.Value);
    public static Own operator ++(Own a) => new(a.Value + 1);
    public static Own operator --(Own a) => new(a.Value - 1);
    public static Own operator +(Own a) => a;
    public static Own operator -(Own a) => new(-a.Value);
    public static bool operator ==(Own a, Own b) => a.Value == b.Value;
    public static bool operator !=(Own a, Own b) => a.Value != b.Value;
    public bool Equals(Own other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Own o && Equals(o);
    public override int GetHashCode() => Value;
    public string ToString(string? format, IFormatProvider? provider) => "O" + Value.ToString(format, provider);
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        return false;
    }
}
