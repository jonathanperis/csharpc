// System.Numerics.Decimal128 (IEEE 754-2019 decimal floating point, 34 digits, binary integer decimal encoding) as a 128-bit value, after
// dotnet/runtime's Decimal128.cs (release/11.0; see THIRD-PARTY-NOTICES.md). The value is handled as a decimal digit string and a power of
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
    [Shadows("System.Numerics.Decimal128")]
    public readonly unsafe struct Decimal128Impl : IComparable, IComparable<System.Numerics.Decimal128>, IEquatable<System.Numerics.Decimal128>, IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        private readonly ulong _lower;
        private readonly ulong _upper;

        internal Decimal128Impl(UInt128 bits) { _lower = (ulong)bits; _upper = (ulong)(bits >> 64); }

        private static IeeeSpec Spec => IeeeDecimal.D128;
        internal UInt128 Bits => new UInt128(_upper, _lower);

        private static System.Numerics.Decimal128 Wrap(UInt128 bits) { var h = new Decimal128Impl(bits); return *(System.Numerics.Decimal128*)&h; }
        private static UInt128 BitsOf(System.Numerics.Decimal128 value) => (*(Decimal128Impl*)&value).Bits;
        private static Decimal128Impl Make(UInt128 bits) => new Decimal128Impl(bits);
        private static NotSupportedException Unsupported(string what) => new NotSupportedException("Decimal128." + what + " is not supported by csharpc yet.");

        private const string PiDigits = "314159265358979323846264338327950288419716939937510", EDigits = "271828182845904523536028747135266249775724709369995", TauDigits = "628318530717958647692528676655900576839433879875021";

        public static System.Numerics.Decimal128 Zero => Wrap(IeeeDecimal.Encode(Spec, false, 0, 0));
        public static System.Numerics.Decimal128 NegativeZero => Wrap(IeeeDecimal.Encode(Spec, true, 0, 0));
        public static System.Numerics.Decimal128 One => Wrap(IeeeDecimal.Encode(Spec, false, 1, 0));
        public static System.Numerics.Decimal128 NegativeOne => Wrap(IeeeDecimal.Encode(Spec, true, 1, 0));
        public static System.Numerics.Decimal128 Epsilon => Wrap(IeeeDecimal.Encode(Spec, false, 1, Spec.EMin));
        public static System.Numerics.Decimal128 MaxValue => Wrap(IeeeDecimal.Encode(Spec, false, IeeeDecimal.MaxSignificand(Spec), Spec.EMax));
        public static System.Numerics.Decimal128 MinValue => Wrap(IeeeDecimal.Encode(Spec, true, IeeeDecimal.MaxSignificand(Spec), Spec.EMax));
        public static System.Numerics.Decimal128 NaN => Wrap(IeeeDecimal.NaN(Spec));
        public static System.Numerics.Decimal128 PositiveInfinity => Wrap(IeeeDecimal.Infinity(Spec, false));
        public static System.Numerics.Decimal128 NegativeInfinity => Wrap(IeeeDecimal.Infinity(Spec, true));
        public static System.Numerics.Decimal128 Pi => Wrap(IeeeDecimal.Round(Spec, false, PiDigits, 1 - PiDigits.Length, false));
        public static System.Numerics.Decimal128 E => Wrap(IeeeDecimal.Round(Spec, false, EDigits, 1 - EDigits.Length, false));
        public static System.Numerics.Decimal128 Tau => Wrap(IeeeDecimal.Round(Spec, false, TauDigits, 1 - TauDigits.Length, false));

        // ---- classification ----
        public static bool IsNaN(System.Numerics.Decimal128 value) => IeeeDecimal.IsNaN(Spec, BitsOf(value));
        public static bool IsInfinity(System.Numerics.Decimal128 value) => IeeeDecimal.IsInfinity(Spec, BitsOf(value));
        public static bool IsPositiveInfinity(System.Numerics.Decimal128 value) => IeeeDecimal.IsInfinity(Spec, BitsOf(value)) && !IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsNegativeInfinity(System.Numerics.Decimal128 value) => IeeeDecimal.IsInfinity(Spec, BitsOf(value)) && IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsFinite(System.Numerics.Decimal128 value) => !IeeeDecimal.IsNaN(Spec, BitsOf(value)) && !IeeeDecimal.IsInfinity(Spec, BitsOf(value));
        public static bool IsNegative(System.Numerics.Decimal128 value) => IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsPositive(System.Numerics.Decimal128 value) => !IeeeDecimal.IsNegative(Spec, BitsOf(value));
        public static bool IsRealNumber(System.Numerics.Decimal128 value) => !IeeeDecimal.IsNaN(Spec, BitsOf(value));

        // The adjusted exponent (the exponent of the first digit) of a finite non-zero value, or int.MinValue.
        private static int Adjusted(System.Numerics.Decimal128 value, out string digits, out int exponent)
        {
            IeeeDecimal.Decode(Spec, BitsOf(value), out _, out int kind, out UInt128 sig, out exponent);
            digits = IeeeDecimal.DigitsOf(sig);
            return kind != IeeeDecimal.Finite || sig == 0 ? int.MinValue : digits.Length - 1 + exponent;
        }

        public static bool IsNormal(System.Numerics.Decimal128 value) { int adjusted = Adjusted(value, out _, out _); return adjusted != int.MinValue && adjusted >= Spec.EMin + Spec.Digits - 1; }
        public static bool IsSubnormal(System.Numerics.Decimal128 value) { int adjusted = Adjusted(value, out _, out _); return adjusted != int.MinValue && adjusted < Spec.EMin + Spec.Digits - 1; }

        public static bool IsInteger(System.Numerics.Decimal128 value)
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
        private static bool IsIntegerWithLastDigit(System.Numerics.Decimal128 value, int parity)
        {
            if (!IsInteger(value)) return false;
            Adjusted(value, out string digits, out int exponent);
            if (digits.Length == 0 || exponent > 0) return parity == 0;
            int at = digits.Length + exponent - 1;
            if (at < 0) return parity == 0;
            return ((digits[at] - '0') & 1) == parity;
        }

        public static bool IsEvenInteger(System.Numerics.Decimal128 value) => IsIntegerWithLastDigit(value, 0);
        public static bool IsOddInteger(System.Numerics.Decimal128 value) => IsIntegerWithLastDigit(value, 1);

        // ---- comparison and identity ----
        public static bool operator ==(Decimal128Impl left, Decimal128Impl right) => IeeeDecimal.NumericEquals(Spec, left.Bits, right.Bits);
        public static bool operator !=(Decimal128Impl left, Decimal128Impl right) => !(left == right);
        public static bool operator <(Decimal128Impl left, Decimal128Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) < 0;
        public static bool operator >(Decimal128Impl left, Decimal128Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) > 0;
        public static bool operator <=(Decimal128Impl left, Decimal128Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) <= 0;
        public static bool operator >=(Decimal128Impl left, Decimal128Impl right) => !IeeeDecimal.IsNaN(Spec, left.Bits) && !IeeeDecimal.IsNaN(Spec, right.Bits) && IeeeDecimal.Compare(Spec, left.Bits, right.Bits) >= 0;

        public int CompareTo(object obj)
        {
            if (obj is null) return 1;
            if (obj is System.Numerics.Decimal128 other) return CompareTo(other);
            throw new ArgumentException("Object must be of type Decimal128.");
        }

        public int CompareTo(System.Numerics.Decimal128 other)
        {
            UInt128 o = BitsOf(other);
            bool nan = IeeeDecimal.IsNaN(Spec, Bits), otherNaN = IeeeDecimal.IsNaN(Spec, o);
            if (nan || otherNaN) return nan ? (otherNaN ? 0 : -1) : 1;
            return IeeeDecimal.Compare(Spec, Bits, o);
        }

        public override bool Equals(object obj) => obj is System.Numerics.Decimal128 other && Equals(other);

        public bool Equals(System.Numerics.Decimal128 other) =>
            IeeeDecimal.NumericEquals(Spec, Bits, BitsOf(other)) || (IeeeDecimal.IsNaN(Spec, Bits) && IeeeDecimal.IsNaN(Spec, BitsOf(other)));

        public override int GetHashCode() => IeeeDecimal.HashCode(Spec, Bits);

        public static System.Numerics.Decimal128 Abs(System.Numerics.Decimal128 value) => Wrap(BitsOf(value) & ~(((UInt128)1) << (Spec.Bits - 1)));
        public static System.Numerics.Decimal128 CopySign(System.Numerics.Decimal128 value, System.Numerics.Decimal128 sign) =>
            Wrap((BitsOf(value) & ~(((UInt128)1) << (Spec.Bits - 1))) | (BitsOf(sign) & (((UInt128)1) << (Spec.Bits - 1))));
        public static Decimal128Impl operator -(Decimal128Impl value) => Make(value.Bits ^ (((UInt128)1) << (Spec.Bits - 1)));
        public static Decimal128Impl operator +(Decimal128Impl value) => value;

        public static System.Numerics.Decimal128 Max(System.Numerics.Decimal128 x, System.Numerics.Decimal128 y)
        {
            if (IsNaN(x)) return x;
            if (IsNaN(y)) return y;
            int c = IeeeDecimal.Compare(Spec, BitsOf(x), BitsOf(y));
            return c > 0 || (c == 0 && IsPositive(x)) ? x : y;
        }

        public static System.Numerics.Decimal128 Min(System.Numerics.Decimal128 x, System.Numerics.Decimal128 y)
        {
            if (IsNaN(x)) return x;
            if (IsNaN(y)) return y;
            int c = IeeeDecimal.Compare(Spec, BitsOf(x), BitsOf(y));
            return c < 0 || (c == 0 && IsNegative(x)) ? x : y;
        }

        public static System.Numerics.Decimal128 Clamp(System.Numerics.Decimal128 value, System.Numerics.Decimal128 min, System.Numerics.Decimal128 max)
        {
            if (IeeeDecimal.Compare(Spec, BitsOf(min), BitsOf(max)) > 0) throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            return IeeeDecimal.Compare(Spec, BitsOf(value), BitsOf(min)) < 0 ? min : IeeeDecimal.Compare(Spec, BitsOf(value), BitsOf(max)) > 0 ? max : value;
        }

        public static int Sign(System.Numerics.Decimal128 value)
        {
            if (IsNaN(value)) throw new ArithmeticException("Function does not accept floating point Not-a-Number values.");
            return IeeeDecimal.Compare(Spec, BitsOf(value), IeeeDecimal.Encode(Spec, false, 0, 0));
        }

        // ---- rounding to a power of ten ----
        public static System.Numerics.Decimal128 Truncate(System.Numerics.Decimal128 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 2));
        public static System.Numerics.Decimal128 Floor(System.Numerics.Decimal128 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 3));
        public static System.Numerics.Decimal128 Ceiling(System.Numerics.Decimal128 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 4));
        public static System.Numerics.Decimal128 Round(System.Numerics.Decimal128 value) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, 0));
        public static System.Numerics.Decimal128 Round(System.Numerics.Decimal128 value, MidpointRounding mode) => Wrap(IeeeDecimal.RoundToExponent(Spec, BitsOf(value), 0, ModeOf(mode)));
        public static System.Numerics.Decimal128 Round(System.Numerics.Decimal128 value, int digits) => Round(value, digits, MidpointRounding.ToEven);
        public static System.Numerics.Decimal128 Round(System.Numerics.Decimal128 value, int digits, MidpointRounding mode)
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
        public static Decimal128Impl operator +(Decimal128Impl left, Decimal128Impl right) => throw Unsupported("operator +");
        public static Decimal128Impl operator -(Decimal128Impl left, Decimal128Impl right) => throw Unsupported("operator -");
        public static Decimal128Impl operator *(Decimal128Impl left, Decimal128Impl right) => throw Unsupported("operator *");
        public static Decimal128Impl operator /(Decimal128Impl left, Decimal128Impl right) => throw Unsupported("operator /");
        public static Decimal128Impl operator %(Decimal128Impl left, Decimal128Impl right) => throw Unsupported("operator %");
        public static Decimal128Impl operator ++(Decimal128Impl value) => throw Unsupported("operator ++");
        public static Decimal128Impl operator --(Decimal128Impl value) => throw Unsupported("operator --");
        public static System.Numerics.Decimal128 Sqrt(System.Numerics.Decimal128 value) => throw Unsupported("Sqrt");
        public static System.Numerics.Decimal128 Pow(System.Numerics.Decimal128 x, System.Numerics.Decimal128 y) => throw Unsupported("Pow");
        public static System.Numerics.Decimal128 Exp(System.Numerics.Decimal128 x) => throw Unsupported("Exp");
        public static System.Numerics.Decimal128 Log(System.Numerics.Decimal128 x) => throw Unsupported("Log");
        public static System.Numerics.Decimal128 Log10(System.Numerics.Decimal128 x) => throw Unsupported("Log10");

        // ---- the encoding ----
        public static UInt128 EncodeBinary(System.Numerics.Decimal128 value) => BitsOf(value);
        public static System.Numerics.Decimal128 DecodeBinary(UInt128 value) => Wrap(value);
        public static UInt128 EncodeDecimal(System.Numerics.Decimal128 value) => throw Unsupported("EncodeDecimal (the DPD encoding)");
        public static System.Numerics.Decimal128 DecodeDecimal(UInt128 value) => throw Unsupported("DecodeDecimal (the DPD encoding)");

        // ---- conversions ----
        private static Decimal128Impl FromSigned(Int128 value) => Make(IeeeDecimal.FromInt128(Spec, value));
        private static Decimal128Impl FromUnsigned(UInt128 value) => Make(IeeeDecimal.FromUInt128(Spec, false, value));
        private static Decimal128Impl FromDoubleValue(double value) => Make(IeeeDecimal.FromDouble(Spec, value));

        public static implicit operator Decimal128Impl(byte value) => FromUnsigned(value);
        public static implicit operator Decimal128Impl(sbyte value) => FromSigned(value);
        public static implicit operator Decimal128Impl(char value) => FromUnsigned(value);
        public static implicit operator Decimal128Impl(short value) => FromSigned(value);
        public static implicit operator Decimal128Impl(ushort value) => FromUnsigned(value);
        public static implicit operator Decimal128Impl(int value) => FromSigned(value);
        public static implicit operator Decimal128Impl(uint value) => FromUnsigned(value);
        public static implicit operator Decimal128Impl(long value) => FromSigned(value);
        public static implicit operator Decimal128Impl(ulong value) => FromUnsigned(value);
        public static implicit operator Decimal128Impl(nint value) => FromSigned((long)value);
        public static implicit operator Decimal128Impl(nuint value) => FromUnsigned((ulong)value);
        public static explicit operator Decimal128Impl(Int128 value) => FromSigned(value);
        public static explicit operator Decimal128Impl(UInt128 value) => FromUnsigned(value);
        public static explicit operator Decimal128Impl(float value) => FromDoubleValue(value);
        public static explicit operator Decimal128Impl(double value) => FromDoubleValue(value);
        public static implicit operator Decimal128Impl(decimal value) => Make(IeeeDecimal.FromDecimal(Spec, value));

        public static explicit operator double(Decimal128Impl value) => IeeeDecimal.ToDouble(Spec, value.Bits);
        public static explicit operator float(Decimal128Impl value) => IeeeDecimal.ToSingle(Spec, value.Bits);
        public static explicit operator decimal(Decimal128Impl value) => IeeeDecimal.ToDecimal(Spec, value.Bits);
        public static explicit operator sbyte(Decimal128Impl value) => (sbyte)IeeeDecimal.ToSigned(Spec, value.Bits, sbyte.MinValue, sbyte.MaxValue, false);
        public static explicit operator short(Decimal128Impl value) => (short)IeeeDecimal.ToSigned(Spec, value.Bits, short.MinValue, short.MaxValue, false);
        public static explicit operator int(Decimal128Impl value) => (int)IeeeDecimal.ToSigned(Spec, value.Bits, int.MinValue, int.MaxValue, false);
        public static explicit operator long(Decimal128Impl value) => (long)IeeeDecimal.ToSigned(Spec, value.Bits, long.MinValue, long.MaxValue, false);
        public static explicit operator nint(Decimal128Impl value) => (nint)IeeeDecimal.ToSigned(Spec, value.Bits, nint.MinValue, nint.MaxValue, false);
        public static explicit operator Int128(Decimal128Impl value) => IeeeDecimal.ToSigned(Spec, value.Bits, Int128.MinValue, Int128.MaxValue, false);
        public static explicit operator byte(Decimal128Impl value) => (byte)IeeeDecimal.ToUnsigned(Spec, value.Bits, byte.MaxValue, false);
        public static explicit operator ushort(Decimal128Impl value) => (ushort)IeeeDecimal.ToUnsigned(Spec, value.Bits, ushort.MaxValue, false);
        public static explicit operator char(Decimal128Impl value) => (char)IeeeDecimal.ToUnsigned(Spec, value.Bits, char.MaxValue, false);
        public static explicit operator uint(Decimal128Impl value) => (uint)IeeeDecimal.ToUnsigned(Spec, value.Bits, uint.MaxValue, false);
        public static explicit operator ulong(Decimal128Impl value) => (ulong)IeeeDecimal.ToUnsigned(Spec, value.Bits, ulong.MaxValue, false);
        public static explicit operator nuint(Decimal128Impl value) => (nuint)IeeeDecimal.ToUnsigned(Spec, value.Bits, nuint.MaxValue, false);
        public static explicit operator UInt128(Decimal128Impl value) => IeeeDecimal.ToUnsigned(Spec, value.Bits, UInt128.MaxValue, false);
        public static explicit operator checked sbyte(Decimal128Impl value) => (sbyte)IeeeDecimal.ToSigned(Spec, value.Bits, sbyte.MinValue, sbyte.MaxValue, true);
        public static explicit operator checked short(Decimal128Impl value) => (short)IeeeDecimal.ToSigned(Spec, value.Bits, short.MinValue, short.MaxValue, true);
        public static explicit operator checked int(Decimal128Impl value) => (int)IeeeDecimal.ToSigned(Spec, value.Bits, int.MinValue, int.MaxValue, true);
        public static explicit operator checked long(Decimal128Impl value) => (long)IeeeDecimal.ToSigned(Spec, value.Bits, long.MinValue, long.MaxValue, true);
        public static explicit operator checked nint(Decimal128Impl value) => (nint)IeeeDecimal.ToSigned(Spec, value.Bits, nint.MinValue, nint.MaxValue, true);
        public static explicit operator checked Int128(Decimal128Impl value) => IeeeDecimal.ToSigned(Spec, value.Bits, Int128.MinValue, Int128.MaxValue, true);
        public static explicit operator checked byte(Decimal128Impl value) => (byte)IeeeDecimal.ToUnsigned(Spec, value.Bits, byte.MaxValue, true);
        public static explicit operator checked ushort(Decimal128Impl value) => (ushort)IeeeDecimal.ToUnsigned(Spec, value.Bits, ushort.MaxValue, true);
        public static explicit operator checked char(Decimal128Impl value) => (char)IeeeDecimal.ToUnsigned(Spec, value.Bits, char.MaxValue, true);
        public static explicit operator checked uint(Decimal128Impl value) => (uint)IeeeDecimal.ToUnsigned(Spec, value.Bits, uint.MaxValue, true);
        public static explicit operator checked ulong(Decimal128Impl value) => (ulong)IeeeDecimal.ToUnsigned(Spec, value.Bits, ulong.MaxValue, true);
        public static explicit operator checked nuint(Decimal128Impl value) => (nuint)IeeeDecimal.ToUnsigned(Spec, value.Bits, nuint.MaxValue, true);
        public static explicit operator checked UInt128(Decimal128Impl value) => IeeeDecimal.ToUnsigned(Spec, value.Bits, UInt128.MaxValue, true);


        // ---- text ----
        private const NumberStyles DefaultStyle = NumberStyles.Float | NumberStyles.AllowThousands;

        private static string Utf8(ReadOnlySpan<byte> utf8Text) => Encoding.UTF8.GetString(utf8Text);

        private static System.Numerics.Decimal128 ParseCore(string s, NumberStyles style, IFormatProvider provider)
        {
            IeeeDecimal.TryParse(Spec, s, style, provider, out UInt128 bits, true);
            return Wrap(bits);
        }

        private static bool TryParseCore(string s, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal128 result)
        {
            bool ok = IeeeDecimal.TryParse(Spec, s, style, provider, out UInt128 bits, false);
            result = Wrap(bits);
            return ok;
        }

        public static System.Numerics.Decimal128 Parse(string s) => ParseCore(s, DefaultStyle, null);
        public static System.Numerics.Decimal128 Parse(string s, IFormatProvider provider) => ParseCore(s, DefaultStyle, provider);
        public static System.Numerics.Decimal128 Parse(string s, NumberStyles style) => ParseCore(s, style, null);
        public static System.Numerics.Decimal128 Parse(string s, NumberStyles style, IFormatProvider provider) => ParseCore(s, style, provider);
        public static System.Numerics.Decimal128 Parse(ReadOnlySpan<char> s, IFormatProvider provider) => ParseCore(s.ToString(), DefaultStyle, provider);
        public static System.Numerics.Decimal128 Parse(ReadOnlySpan<char> s, NumberStyles style = DefaultStyle, IFormatProvider provider = null) => ParseCore(s.ToString(), style, provider);
        public static System.Numerics.Decimal128 Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = DefaultStyle, IFormatProvider provider = null) => ParseCore(Utf8(utf8Text), style, provider);
        public static System.Numerics.Decimal128 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider) => ParseCore(Utf8(utf8Text), DefaultStyle, provider);

        public static bool TryParse(string s, out System.Numerics.Decimal128 result) => TryParseCore(s, DefaultStyle, null, out result);
        public static bool TryParse(string s, IFormatProvider provider, out System.Numerics.Decimal128 result) => TryParseCore(s, DefaultStyle, provider, out result);
        public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal128 result) => TryParseCore(s, style, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, out System.Numerics.Decimal128 result) => TryParseCore(s.ToString(), DefaultStyle, null, out result);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out System.Numerics.Decimal128 result) => TryParseCore(s.ToString(), DefaultStyle, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal128 result) => TryParseCore(s.ToString(), style, provider, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out System.Numerics.Decimal128 result) => TryParseCore(Utf8(utf8Text), DefaultStyle, null, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider, out System.Numerics.Decimal128 result) => TryParseCore(Utf8(utf8Text), DefaultStyle, provider, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider provider, out System.Numerics.Decimal128 result) => TryParseCore(Utf8(utf8Text), style, provider, out result);

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
