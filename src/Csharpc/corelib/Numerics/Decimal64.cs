// System.Numerics.Decimal64 (IEEE 754-2019 decimal floating point, 16 digits, binary integer decimal encoding) as a 64-bit value, after
// dotnet/runtime's Decimal64.cs (release/11.0; see THIRD-PARTY-NOTICES.md). The value is handled as a decimal digit string and a power of
// ten (IeeeDecimal.cs), so the three widths share one engine. Here: the constants, classification, comparison across cohorts (1.0 ==
// 1.00), parsing and formatting with the cohort kept ("1.50" prints "1.50"), the BID encoding, truncation and rounding to a power
// of ten, and the conversions to and from the integers, double, float, System.Decimal and the other decimal widths. Arithmetic, the
// math functions, the DPD encoding and the conversions from and to Half stop with an explicit "not supported" diagnostic.
// Decimal32.cs, Decimal64.cs and Decimal128.cs have the same shape; they differ in the width, the storage and the conversions.
#nullable disable
using System;
using System.Globalization;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Numerics.Decimal64")]
    public readonly unsafe struct Decimal64Impl : IComparable, IComparable<System.Numerics.Decimal64>, IEquatable<System.Numerics.Decimal64>, IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        private readonly ulong _value;

        internal Decimal64Impl(UInt128 bits) { _value = (ulong)bits; }

        private static IeeeSpec Spec => IeeeDecimal.D64;
        internal UInt128 Bits => (UInt128)_value;

        private static System.Numerics.Decimal64 Wrap(UInt128 bits) { var h = new Decimal64Impl(bits); return *(System.Numerics.Decimal64*)&h; }
        private static UInt128 BitsOf(System.Numerics.Decimal64 value) => (*(Decimal64Impl*)&value).Bits;
        private static Decimal64Impl Make(UInt128 bits) => new Decimal64Impl(bits);
        private static NotSupportedException Unsupported(string what) => new NotSupportedException("Decimal64." + what + " is not supported by csharpc yet.");

        private const string PiDigits = "314159265358979323846264338327950288419716939937510", EDigits = "271828182845904523536028747135266249775724709369995", TauDigits = "628318530717958647692528676655900576839433879875021";

        public static System.Numerics.Decimal64 Zero => Wrap(IeeeDecimal.Encode(Spec, false, 0, 0));
        public static System.Numerics.Decimal64 NegativeZero => Wrap(IeeeDecimal.Encode(Spec, true, 0, 0));
        public static System.Numerics.Decimal64 One => Wrap(IeeeDecimal.Encode(Spec, false, 1, 0));
        public static System.Numerics.Decimal64 NegativeOne => Wrap(IeeeDecimal.Encode(Spec, true, 1, 0));
        public static System.Numerics.Decimal64 Epsilon => Wrap(IeeeDecimal.Encode(Spec, false, 1, Spec.EMin));
        public static System.Numerics.Decimal64 MaxValue => Wrap(IeeeDecimal.Encode(Spec, false, IeeeDecimal.MaxSignificand(Spec), Spec.EMax));
        public static System.Numerics.Decimal64 MinValue => Wrap(IeeeDecimal.Encode(Spec, true, IeeeDecimal.MaxSignificand(Spec), Spec.EMax));
        public static System.Numerics.Decimal64 NaN => Wrap(IeeeDecimal.NaN(Spec));
        public static System.Numerics.Decimal64 PositiveInfinity => Wrap(IeeeDecimal.Infinity(Spec, false));
        public static System.Numerics.Decimal64 NegativeInfinity => Wrap(IeeeDecimal.Infinity(Spec, true));
        public static System.Numerics.Decimal64 Pi => Wrap(IeeeDecimal.Round(Spec, false, PiDigits, 1 - PiDigits.Length, false));
        public static System.Numerics.Decimal64 E => Wrap(IeeeDecimal.Round(Spec, false, EDigits, 1 - EDigits.Length, false));
        public static System.Numerics.Decimal64 Tau => Wrap(IeeeDecimal.Round(Spec, false, TauDigits, 1 - TauDigits.Length, false));

        // ---- classification ----
        public static bool IsNaN(System.Numerics.Decimal64 value) => IeeeDecimal.IsNaN(Spec, BitsOf(value));
        public static bool IsInfinity(System.Numerics.Decimal64 value) => IeeeDecimal.IsInfinity(Spec, BitsOf(value));
        public static bool IsPositiveInfinity(System.Numerics.Decimal64 value) => IeeeDecimal.IsInfinity(Spec, BitsOf(value)) && !IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsNegativeInfinity(System.Numerics.Decimal64 value) => IeeeDecimal.IsInfinity(Spec, BitsOf(value)) && IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsFinite(System.Numerics.Decimal64 value) => !IeeeDecimal.IsNaN(Spec, BitsOf(value)) && !IeeeDecimal.IsInfinity(Spec, BitsOf(value));
        public static bool IsNegative(System.Numerics.Decimal64 value) => IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsPositive(System.Numerics.Decimal64 value) => !IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsRealNumber(System.Numerics.Decimal64 value) => !IeeeDecimal.IsNaN(Spec, BitsOf(value));

        // The adjusted exponent (the exponent of the first digit) of a finite non-zero value, or int.MinValue.
        private static int Adjusted(System.Numerics.Decimal64 value, out string digits, out int exponent)
        {
            IeeeDecimal.Decode(Spec, BitsOf(value), out _, out int kind, out UInt128 sig, out exponent);
            digits = IeeeDecimal.DigitsOf(sig);
            return kind != IeeeDecimal.Finite || sig == 0 ? int.MinValue : digits.Length - 1 + exponent;
        }

        public static bool IsNormal(System.Numerics.Decimal64 value) { int adjusted = Adjusted(value, out _, out _); return adjusted != int.MinValue && adjusted >= Spec.EMin + Spec.Digits - 1; }
        public static bool IsSubnormal(System.Numerics.Decimal64 value) { int adjusted = Adjusted(value, out _, out _); return adjusted != int.MinValue && adjusted < Spec.EMin + Spec.Digits - 1; }

        public static bool IsInteger(System.Numerics.Decimal64 value)
        {
            if (!IsFinite(value)) return false;
            Adjusted(value, out string digits, out int exponent);
            if (digits.Length == 0 || exponent >= 0) return true;
            int fraction = -exponent;
            if (fraction > digits.Length) return false;
            for (int i = digits.Length - fraction; i < digits.Length; i++) if (digits[i] != '0') return false;
            return true;
        }

        // The last digit of the integer part: even for a multiple of ten or a zero.
        private static bool IsIntegerWithLastDigit(System.Numerics.Decimal64 value, int parity)
        {
            if (!IsInteger(value)) return false;
            Adjusted(value, out string digits, out int exponent);
            if (digits.Length == 0 || exponent > 0) return parity == 0;
            int at = digits.Length + exponent - 1;
            if (at < 0) return parity == 0;
            return ((digits[at] - '0') & 1) == parity;
        }

        public static bool IsEvenInteger(System.Numerics.Decimal64 value) => IsIntegerWithLastDigit(value, 0);
        public static bool IsOddInteger(System.Numerics.Decimal64 value) => IsIntegerWithLastDigit(value, 1);

        // ---- comparison and identity ----
        public static bool operator ==(Decimal64Impl left, Decimal64Impl right) => IeeeDecimal.NumericEquals(Spec, left.Bits, right.Bits);
        public static bool operator !=(Decimal64Impl left, Decimal64Impl right) => !(left == right);
        public static bool operator <(Decimal64Impl left, Decimal64Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) < 0;
        public static bool operator >(Decimal64Impl left, Decimal64Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) > 0;
        public static bool operator <=(Decimal64Impl left, Decimal64Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) <= 0;
        public static bool operator >=(Decimal64Impl left, Decimal64Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) >= 0;

        public int CompareTo(object obj)
        {
            if (obj is null) return 1;
            if (obj is System.Numerics.Decimal64 other) return CompareTo(other);
            throw new ArgumentException("Object must be of type Decimal64.");
        }

        public int CompareTo(System.Numerics.Decimal64 other)
        {
            UInt128 o = BitsOf(other);
            bool nan = IeeeDecimal.IsNaN(Spec, Bits), otherNaN = IeeeDecimal.IsNaN(Spec, o);
            if (nan || otherNaN) return nan ? (otherNaN ? 0 : -1) : 1;
            return IeeeDecimal.Compare(Spec, Bits, o);
        }

        public override bool Equals(object obj) => obj is System.Numerics.Decimal64 other && Equals(other);

        public bool Equals(System.Numerics.Decimal64 other) =>
            IeeeDecimal.NumericEquals(Spec, Bits, BitsOf(other)) || (IeeeDecimal.IsNaN(Spec, Bits) && IeeeDecimal.IsNaN(Spec, BitsOf(other)));

        public override int GetHashCode() => IeeeDecimal.HashCode(Spec, Bits);

        public static System.Numerics.Decimal64 Abs(System.Numerics.Decimal64 value) => Wrap(BitsOf(value) & ~(((UInt128)1) << (Spec.Bits - 1)));
        public static System.Numerics.Decimal64 CopySign(System.Numerics.Decimal64 value, System.Numerics.Decimal64 sign) =>
            Wrap((BitsOf(value) & ~(((UInt128)1) << (Spec.Bits - 1))) | (BitsOf(sign) & (((UInt128)1) << (Spec.Bits - 1))));
        public static Decimal64Impl operator -(Decimal64Impl value) => Make(value.Bits ^ (((UInt128)1) << (Spec.Bits - 1)));
        public static Decimal64Impl operator +(Decimal64Impl value) => value;

        public static System.Numerics.Decimal64 Max(System.Numerics.Decimal64 x, System.Numerics.Decimal64 y)
        {
            if (IsNaN(x)) return x;
            if (IsNaN(y)) return y;
            int c = IeeeDecimal.Compare(Spec, BitsOf(x), BitsOf(y));
            return c > 0 || (c == 0 && IsPositive(x)) ? x : y;
        }

        public static System.Numerics.Decimal64 Min(System.Numerics.Decimal64 x, System.Numerics.Decimal64 y)
        {
            if (IsNaN(x)) return x;
            if (IsNaN(y)) return y;
            int c = IeeeDecimal.Compare(Spec, BitsOf(x), BitsOf(y));
            return c < 0 || (c == 0 && IsNegative(x)) ? x : y;
        }

        public static System.Numerics.Decimal64 Clamp(System.Numerics.Decimal64 value, System.Numerics.Decimal64 min, System.Numerics.Decimal64 max)
        {
            if (IeeeDecimal.Compare(Spec, BitsOf(min), BitsOf(max)) > 0) throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            return IeeeDecimal.Compare(Spec, BitsOf(value), BitsOf(min)) < 0 ? min : IeeeDecimal.Compare(Spec, BitsOf(value), BitsOf(max)) > 0 ? max : value;
        }

        public static int Sign(System.Numerics.Decimal64 value)
        {
            if (IsNaN(value)) throw new ArithmeticException("Function does not accept floating point Not-a-Number values.");
            return IeeeDecimal.Compare(Spec, BitsOf(value), IeeeDecimal.Encode(Spec, false, 0, 0));
        }

        // ---- rounding to a power of ten ----
        public static System.Numerics.Decimal64 Truncate(System.Numerics.Decimal64 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 2));
        public static System.Numerics.Decimal64 Floor(System.Numerics.Decimal64 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 3));
        public static System.Numerics.Decimal64 Ceiling(System.Numerics.Decimal64 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 4));
        public static System.Numerics.Decimal64 Round(System.Numerics.Decimal64 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 0));
        public static System.Numerics.Decimal64 Round(System.Numerics.Decimal64 value, MidpointRounding mode) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, ModeOf(mode)));
        public static System.Numerics.Decimal64 Round(System.Numerics.Decimal64 value, int digits) => Round(value, digits, MidpointRounding.ToEven);
        public static System.Numerics.Decimal64 Round(System.Numerics.Decimal64 value, int digits, MidpointRounding mode)
        {
            if (digits < 0) throw Unsupported("Round with negative digits");
            return Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), -digits, ModeOf(mode)));
        }

        private static int ModeOf(MidpointRounding mode) => mode switch
        {
            MidpointRounding.ToEven => 0,
            MidpointRounding.AwayFromZero => 1,
            MidpointRounding.ToZero => 2,
            MidpointRounding.ToNegativeInfinity => 3,
            MidpointRounding.ToPositiveInfinity => 4,
            _ => throw new ArgumentException("The value of the argument 'mode' (" + (int)mode + ") is invalid for Enum type 'MidpointRounding'. (Parameter 'mode')"),
        };

        // ---- the arithmetic and math functions: explicit stops ----
        public static Decimal64Impl operator +(Decimal64Impl left, Decimal64Impl right) => throw Unsupported("operator +");
        public static Decimal64Impl operator -(Decimal64Impl left, Decimal64Impl right) => throw Unsupported("operator -");
        public static Decimal64Impl operator *(Decimal64Impl left, Decimal64Impl right) => throw Unsupported("operator *");
        public static Decimal64Impl operator /(Decimal64Impl left, Decimal64Impl right) => throw Unsupported("operator /");
        public static Decimal64Impl operator %(Decimal64Impl left, Decimal64Impl right) => throw Unsupported("operator %");
        public static Decimal64Impl operator ++(Decimal64Impl value) => throw Unsupported("operator ++");
        public static Decimal64Impl operator --(Decimal64Impl value) => throw Unsupported("operator --");
        public static System.Numerics.Decimal64 Sqrt(System.Numerics.Decimal64 value) => throw Unsupported("Sqrt");
        public static System.Numerics.Decimal64 Pow(System.Numerics.Decimal64 x, System.Numerics.Decimal64 y) => throw Unsupported("Pow");
        public static System.Numerics.Decimal64 Exp(System.Numerics.Decimal64 x) => throw Unsupported("Exp");
        public static System.Numerics.Decimal64 Log(System.Numerics.Decimal64 x) => throw Unsupported("Log");
        public static System.Numerics.Decimal64 Log10(System.Numerics.Decimal64 x) => throw Unsupported("Log10");

        // ---- the encoding ----
        public static ulong EncodeBinary(System.Numerics.Decimal64 value) => (ulong)BitsOf(value);
        public static System.Numerics.Decimal64 DecodeBinary(ulong value) => Wrap((UInt128)value);
        public static ulong EncodeDecimal(System.Numerics.Decimal64 value) => throw Unsupported("EncodeDecimal (the DPD encoding)");
        public static System.Numerics.Decimal64 DecodeDecimal(ulong value) => throw Unsupported("DecodeDecimal (the DPD encoding)");

        // ---- conversions ----
        private static Decimal64Impl FromSigned(Int128 value) => Make(IeeeDecimal.FromInt128(Spec, value));
        private static Decimal64Impl FromUnsigned(UInt128 value) => Make(IeeeDecimal.FromUInt128(Spec, false, value));
        private static Decimal64Impl FromDoubleValue(double value) => Make(IeeeDecimal.FromDouble(Spec, value));

        public static implicit operator Decimal64Impl(byte value) => FromUnsigned(value);
        public static implicit operator Decimal64Impl(sbyte value) => FromSigned(value);
        public static implicit operator Decimal64Impl(char value) => FromUnsigned(value);
        public static implicit operator Decimal64Impl(short value) => FromSigned(value);
        public static implicit operator Decimal64Impl(ushort value) => FromUnsigned(value);
        public static implicit operator Decimal64Impl(int value) => FromSigned(value);
        public static implicit operator Decimal64Impl(uint value) => FromUnsigned(value);
        public static explicit operator Decimal64Impl(long value) => FromSigned(value);
        public static explicit operator Decimal64Impl(ulong value) => FromUnsigned(value);
        public static explicit operator Decimal64Impl(nint value) => FromSigned((long)value);
        public static explicit operator Decimal64Impl(nuint value) => FromUnsigned((ulong)value);
        public static explicit operator Decimal64Impl(Int128 value) => FromSigned(value);
        public static explicit operator Decimal64Impl(UInt128 value) => FromUnsigned(value);
        public static explicit operator Decimal64Impl(float value) => FromDoubleValue(value);
        public static explicit operator Decimal64Impl(double value) => FromDoubleValue(value);
        public static explicit operator Decimal64Impl(decimal value) => Make(IeeeDecimal.FromDecimal(Spec, value));

        public static explicit operator double(Decimal64Impl value) => IeeeDecimal.ToDouble(Spec, value.Bits);
        public static explicit operator float(Decimal64Impl value) => IeeeDecimal.ToSingle(Spec, value.Bits);
        public static explicit operator decimal(Decimal64Impl value) => IeeeDecimal.ToDecimal(Spec, value.Bits);
        public static explicit operator sbyte(Decimal64Impl value) => (sbyte)IeeeDecimal.ToSigned(Spec, value.Bits, sbyte.MinValue, sbyte.MaxValue, false);
        public static explicit operator short(Decimal64Impl value) => (short)IeeeDecimal.ToSigned(Spec, value.Bits, short.MinValue, short.MaxValue, false);
        public static explicit operator int(Decimal64Impl value) => (int)IeeeDecimal.ToSigned(Spec, value.Bits, int.MinValue, int.MaxValue, false);
        public static explicit operator long(Decimal64Impl value) => (long)IeeeDecimal.ToSigned(Spec, value.Bits, long.MinValue, long.MaxValue, false);
        public static explicit operator nint(Decimal64Impl value) => (nint)IeeeDecimal.ToSigned(Spec, value.Bits, nint.MinValue, nint.MaxValue, false);
        public static explicit operator Int128(Decimal64Impl value) => IeeeDecimal.ToSigned(Spec, value.Bits, Int128.MinValue, Int128.MaxValue, false);
        public static explicit operator byte(Decimal64Impl value) => (byte)IeeeDecimal.ToUnsigned(Spec, value.Bits, byte.MaxValue, false);
        public static explicit operator ushort(Decimal64Impl value) => (ushort)IeeeDecimal.ToUnsigned(Spec, value.Bits, ushort.MaxValue, false);
        public static explicit operator char(Decimal64Impl value) => (char)IeeeDecimal.ToUnsigned(Spec, value.Bits, char.MaxValue, false);
        public static explicit operator uint(Decimal64Impl value) => (uint)IeeeDecimal.ToUnsigned(Spec, value.Bits, uint.MaxValue, false);
        public static explicit operator ulong(Decimal64Impl value) => (ulong)IeeeDecimal.ToUnsigned(Spec, value.Bits, ulong.MaxValue, false);
        public static explicit operator nuint(Decimal64Impl value) => (nuint)IeeeDecimal.ToUnsigned(Spec, value.Bits, nuint.MaxValue, false);
        public static explicit operator UInt128(Decimal64Impl value) => IeeeDecimal.ToUnsigned(Spec, value.Bits, UInt128.MaxValue, false);
        public static explicit operator checked sbyte(Decimal64Impl value) => (sbyte)IeeeDecimal.ToSigned(Spec, value.Bits, sbyte.MinValue, sbyte.MaxValue, true);
        public static explicit operator checked short(Decimal64Impl value) => (short)IeeeDecimal.ToSigned(Spec, value.Bits, short.MinValue, short.MaxValue, true);
        public static explicit operator checked int(Decimal64Impl value) => (int)IeeeDecimal.ToSigned(Spec, value.Bits, int.MinValue, int.MaxValue, true);
        public static explicit operator checked long(Decimal64Impl value) => (long)IeeeDecimal.ToSigned(Spec, value.Bits, long.MinValue, long.MaxValue, true);
        public static explicit operator checked nint(Decimal64Impl value) => (nint)IeeeDecimal.ToSigned(Spec, value.Bits, nint.MinValue, nint.MaxValue, true);
        public static explicit operator checked Int128(Decimal64Impl value) => IeeeDecimal.ToSigned(Spec, value.Bits, Int128.MinValue, Int128.MaxValue, true);
        public static explicit operator checked byte(Decimal64Impl value) => (byte)IeeeDecimal.ToUnsigned(Spec, value.Bits, byte.MaxValue, true);
        public static explicit operator checked ushort(Decimal64Impl value) => (ushort)IeeeDecimal.ToUnsigned(Spec, value.Bits, ushort.MaxValue, true);
        public static explicit operator checked char(Decimal64Impl value) => (char)IeeeDecimal.ToUnsigned(Spec, value.Bits, char.MaxValue, true);
        public static explicit operator checked uint(Decimal64Impl value) => (uint)IeeeDecimal.ToUnsigned(Spec, value.Bits, uint.MaxValue, true);
        public static explicit operator checked ulong(Decimal64Impl value) => (ulong)IeeeDecimal.ToUnsigned(Spec, value.Bits, ulong.MaxValue, true);
        public static explicit operator checked nuint(Decimal64Impl value) => (nuint)IeeeDecimal.ToUnsigned(Spec, value.Bits, nuint.MaxValue, true);
        public static explicit operator checked UInt128(Decimal64Impl value) => IeeeDecimal.ToUnsigned(Spec, value.Bits, UInt128.MaxValue, true);
        public static implicit operator Decimal128Impl(Decimal64Impl value) => new Decimal128Impl(IeeeDecimal.Convert(Spec, value.Bits, IeeeDecimal.D128));
        public static explicit operator Decimal64Impl(Decimal128Impl value) => Make(IeeeDecimal.Convert(IeeeDecimal.D128, value.Bits, Spec));

        // ---- text ----
        private const NumberStyles DefaultStyle = NumberStyles.Float | NumberStyles.AllowThousands;

        private static string Utf8(ReadOnlySpan<byte> utf8Text) => Encoding.UTF8.GetString(utf8Text);

        private static System.Numerics.Decimal64 ParseCore(string s, NumberStyles style, IFormatProvider provider)
        {
            IeeeDecimal.TryParse(Spec, s, style, provider, out UInt128 bits, true);
            return Wrap(bits);
        }

        private static bool TryParseCore(string s, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal64 result)
        {
            bool ok = IeeeDecimal.TryParse(Spec, s, style, provider, out UInt128 bits, false);
            result = Wrap(bits);
            return ok;
        }

        public static System.Numerics.Decimal64 Parse(string s) => ParseCore(s, DefaultStyle, null);
        public static System.Numerics.Decimal64 Parse(string s, IFormatProvider provider) => ParseCore(s, DefaultStyle, provider);
        public static System.Numerics.Decimal64 Parse(string s, NumberStyles style) => ParseCore(s, style, null);
        public static System.Numerics.Decimal64 Parse(string s, NumberStyles style, IFormatProvider provider) => ParseCore(s, style, provider);
        public static System.Numerics.Decimal64 Parse(ReadOnlySpan<char> s, IFormatProvider provider) => ParseCore(s.ToString(), DefaultStyle, provider);
        public static System.Numerics.Decimal64 Parse(ReadOnlySpan<char> s, NumberStyles style = DefaultStyle, IFormatProvider provider = null) => ParseCore(s.ToString(), style, provider);
        public static System.Numerics.Decimal64 Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = DefaultStyle, IFormatProvider provider = null) => ParseCore(Utf8(utf8Text), style, provider);
        public static System.Numerics.Decimal64 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider) => ParseCore(Utf8(utf8Text), DefaultStyle, provider);

        public static bool TryParse(string s, out System.Numerics.Decimal64 result) => TryParseCore(s, DefaultStyle, null, out result);
        public static bool TryParse(string s, IFormatProvider provider, out System.Numerics.Decimal64 result) => TryParseCore(s, DefaultStyle, provider, out result);
        public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal64 result) => TryParseCore(s, style, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, out System.Numerics.Decimal64 result) => TryParseCore(s.ToString(), DefaultStyle, null, out result);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out System.Numerics.Decimal64 result) => TryParseCore(s.ToString(), DefaultStyle, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal64 result) => TryParseCore(s.ToString(), style, provider, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out System.Numerics.Decimal64 result) => TryParseCore(Utf8(utf8Text), DefaultStyle, null, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider, out System.Numerics.Decimal64 result) => TryParseCore(Utf8(utf8Text), DefaultStyle, provider, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal64 result) => TryParseCore(Utf8(utf8Text), style, provider, out result);

        public override string ToString() => ToString(null, null);
        public string ToString(string format) => ToString(format, null);
        public string ToString(IFormatProvider provider) => ToString(null, provider);
        public string ToString(string format, IFormatProvider provider) => IeeeDecimal.Format(Spec, Bits, format, provider);

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(ToString(format.Length == 0 ? null : format.ToString(), provider), utf8Destination, out bytesWritten);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(ToString(format.Length == 0 ? null : format.ToString(), provider), destination, out charsWritten);
    }
}
