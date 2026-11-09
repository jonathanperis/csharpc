// System.Numerics.BFloat16 (the 16-bit "brain" float: float's sign, exponent and top seven mantissa bits) as a 16-bit value,
// after dotnet/runtime's BFloat16.cs (release/11.0; see THIRD-PARTY-NOTICES.md), in the shape of the Half shadow (Half.cs):
// conversions from doubles round to nearest even from the exact value, arithmetic is done in float and rounded back as .NET
// does, comparisons treat NaN as unordered and the zeros as equal, and text is the shortest digits that read back as the same
// BFloat16 in the general format (the exact value's digits when a precision is given). The members generic code and
// serializers use are here; the rest of the surface (hyperbolic and inverse trigonometric functions, the *P1/*M1 variants,
// estimates, parsing of values within a double's precision of a rounding midpoint) is an explicit "not supported" stop.
#nullable disable
using System;
using System.Globalization;
using System.Text;
using NumberFormatInfo = CsharpcCoreLib.Globalization.NumberFormatInfo;

namespace CsharpcCoreLib
{
    [Shadows("System.Numerics.BFloat16")]
    public readonly unsafe struct BFloat16Impl : IComparable, IComparable<System.Numerics.BFloat16>, IEquatable<System.Numerics.BFloat16>, IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        internal const ushort SignMask = 0x8000;
        internal const ushort PositiveInfinityBits = 0x7F80;
        internal const ushort NegativeInfinityBits = 0xFF80;
        internal const ushort QuietNaNBits = 0xFFC0; // .NET's default NaN has the sign bit set, like double.NaN

        private readonly ushort _value;

        internal BFloat16Impl(ushort bits) => _value = bits;

        private static System.Numerics.BFloat16 Wrap(ushort bits) { var h = new BFloat16Impl(bits); return *(System.Numerics.BFloat16*)&h; }
        private static BFloat16Impl Unwrap(System.Numerics.BFloat16 h) => *(BFloat16Impl*)&h;

        public static System.Numerics.BFloat16 Epsilon => Wrap(0x0001);
        public static System.Numerics.BFloat16 MaxValue => Wrap(0x7F7F);
        public static System.Numerics.BFloat16 MinValue => Wrap(0xFF7F);
        public static System.Numerics.BFloat16 NaN => Wrap(QuietNaNBits);
        public static System.Numerics.BFloat16 NegativeInfinity => Wrap(NegativeInfinityBits);
        public static System.Numerics.BFloat16 PositiveInfinity => Wrap(PositiveInfinityBits);
        public static System.Numerics.BFloat16 NegativeZero => Wrap(SignMask);
        public static System.Numerics.BFloat16 Zero => Wrap(0);
        public static System.Numerics.BFloat16 One => Wrap(0x3F80);
        public static System.Numerics.BFloat16 NegativeOne => Wrap(0xBF80);
        public static System.Numerics.BFloat16 AdditiveIdentity => Wrap(0);
        public static System.Numerics.BFloat16 MultiplicativeIdentity => Wrap(0x3F80);
        public static System.Numerics.BFloat16 E => Wrap(0x402E);
        public static System.Numerics.BFloat16 Pi => Wrap(0x4049);
        public static System.Numerics.BFloat16 Tau => Wrap(0x40C9);

        // ---- classification ----
        private static bool NaNBits(ushort v) => (v & 0x7FFF) > PositiveInfinityBits;
        public static bool IsNaN(System.Numerics.BFloat16 value) => NaNBits(Unwrap(value)._value);
        public static bool IsInfinity(System.Numerics.BFloat16 value) => (Unwrap(value)._value & 0x7FFF) == PositiveInfinityBits;
        public static bool IsPositiveInfinity(System.Numerics.BFloat16 value) => Unwrap(value)._value == PositiveInfinityBits;
        public static bool IsNegativeInfinity(System.Numerics.BFloat16 value) => Unwrap(value)._value == NegativeInfinityBits;
        public static bool IsFinite(System.Numerics.BFloat16 value) => (Unwrap(value)._value & 0x7FFF) < PositiveInfinityBits;
        public static bool IsNegative(System.Numerics.BFloat16 value) => (Unwrap(value)._value & SignMask) != 0;
        public static bool IsPositive(System.Numerics.BFloat16 value) => (Unwrap(value)._value & SignMask) == 0;
        public static bool IsZero(System.Numerics.BFloat16 value) => (Unwrap(value)._value & 0x7FFF) == 0;
        public static bool IsNormal(System.Numerics.BFloat16 value) { int e = (Unwrap(value)._value & 0x7F80) >> 7; return e != 0 && e != 0xFF; }
        public static bool IsSubnormal(System.Numerics.BFloat16 value) { ushort v = Unwrap(value)._value; return (v & 0x7F80) == 0 && (v & 0x007F) != 0; }
        public static bool IsRealNumber(System.Numerics.BFloat16 value) => !IsNaN(value);
        public static bool IsInteger(System.Numerics.BFloat16 value) => IsFinite(value) && value == Truncate(value);
        public static bool IsEvenInteger(System.Numerics.BFloat16 value) => IsInteger(value) && Math.Abs((double)value % 2) == 0;
        public static bool IsOddInteger(System.Numerics.BFloat16 value) => IsInteger(value) && Math.Abs((double)value % 2) == 1;
        public static bool IsPow2(System.Numerics.BFloat16 value)
        {
            ushort v = Unwrap(value)._value;
            if (v == 0 || v >= SignMask) return false;
            int exp = (v >> 7) & 0xFF, sig = v & 0x7F;
            return exp == 0xFF ? false : exp == 0 ? (sig & (sig - 1)) == 0 : sig == 0;
        }

        // ---- conversions ----
        private static float ToFloat(ushort bits) => BitConverter.Int32BitsToSingle(bits << 16);
        private static double ToDouble(ushort bits) => ToFloat(bits);

        private static ulong ShiftRoundEven(ulong m, int shift)
        {
            if (shift >= 64) return 0;
            ulong q = m >> shift;
            ulong rem = m & ((1UL << shift) - 1);
            ulong half = 1UL << (shift - 1);
            return rem > half || (rem == half && (q & 1) == 1) ? q + 1 : q;
        }

        internal static ushort FromDouble(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            ushort sign = (ushort)((bits >> 48) & SignMask);
            int exp = (int)((bits >> 52) & 0x7FF);
            ulong mant = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (exp == 0x7FF)
            {
                if (mant == 0) return (ushort)(sign | PositiveInfinityBits);
                return (ushort)(sign | 0x7FC0 | (int)((mant >> 45) & 0x7F)); // NaN: quiet, keeping the top payload bits
            }
            if (exp == 0) return sign; // zero or a double subnormal: far below BFloat16's smallest
            int e = exp - 1023;
            ulong m = (1UL << 52) | mant;
            if (e >= -126)
            {
                ulong q = ShiftRoundEven(m, 45); // 8-bit significand, 256 after a carry
                int result = ((e + 126) << 7) + (int)q;
                return result >= PositiveInfinityBits ? (ushort)(sign | PositiveInfinityBits) : (ushort)(sign | result);
            }
            return (ushort)(sign | (int)ShiftRoundEven(m, -81 - e));
        }

        private static BFloat16Impl FromFloat(float value) => new BFloat16Impl(FromDouble(value));

        public static explicit operator BFloat16Impl(double value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(float value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(Half value) => new BFloat16Impl(FromDouble((double)(float)value));
        public static explicit operator BFloat16Impl(int value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(long value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(uint value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(ulong value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(short value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(ushort value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(char value) => new BFloat16Impl(FromDouble(value));
        public static explicit operator BFloat16Impl(nint value) => new BFloat16Impl(FromDouble((long)value));
        public static explicit operator BFloat16Impl(nuint value) => new BFloat16Impl(FromDouble((ulong)value));
        public static explicit operator BFloat16Impl(decimal value) => new BFloat16Impl(FromDouble((double)value));
        public static explicit operator BFloat16Impl(Int128 value) => new BFloat16Impl(FromDouble((double)value));
        public static explicit operator BFloat16Impl(UInt128 value) => new BFloat16Impl(FromDouble((double)value));
        public static implicit operator BFloat16Impl(byte value) => new BFloat16Impl(FromDouble(value));
        public static implicit operator BFloat16Impl(sbyte value) => new BFloat16Impl(FromDouble(value));

        public static explicit operator double(BFloat16Impl value) => ToDouble(value._value);
        public static explicit operator float(BFloat16Impl value) => ToFloat(value._value);
        public static explicit operator Half(BFloat16Impl value) => (Half)ToFloat(value._value);
        public static explicit operator decimal(BFloat16Impl value) => (decimal)ToDouble(value._value);
        public static explicit operator int(BFloat16Impl value) => (int)ToFloat(value._value);
        public static explicit operator long(BFloat16Impl value) => (long)ToFloat(value._value);
        public static explicit operator uint(BFloat16Impl value) => (uint)ToFloat(value._value);
        public static explicit operator ulong(BFloat16Impl value) => (ulong)ToFloat(value._value);
        public static explicit operator short(BFloat16Impl value) => (short)ToFloat(value._value);
        public static explicit operator ushort(BFloat16Impl value) => (ushort)ToFloat(value._value);
        public static explicit operator byte(BFloat16Impl value) => (byte)ToFloat(value._value);
        public static explicit operator sbyte(BFloat16Impl value) => (sbyte)ToFloat(value._value);
        public static explicit operator char(BFloat16Impl value) => (char)ToFloat(value._value);
        public static explicit operator nint(BFloat16Impl value) => (nint)ToFloat(value._value);
        public static explicit operator nuint(BFloat16Impl value) => (nuint)ToFloat(value._value);
        public static explicit operator Int128(BFloat16Impl value) => (Int128)ToFloat(value._value);
        public static explicit operator UInt128(BFloat16Impl value) => (UInt128)ToFloat(value._value);
        public static explicit operator checked int(BFloat16Impl value) => checked((int)ToFloat(value._value));
        public static explicit operator checked long(BFloat16Impl value) => checked((long)ToFloat(value._value));
        public static explicit operator checked uint(BFloat16Impl value) => checked((uint)ToFloat(value._value));
        public static explicit operator checked ulong(BFloat16Impl value) => checked((ulong)ToFloat(value._value));
        public static explicit operator checked short(BFloat16Impl value) => checked((short)ToFloat(value._value));
        public static explicit operator checked ushort(BFloat16Impl value) => checked((ushort)ToFloat(value._value));
        public static explicit operator checked byte(BFloat16Impl value) => checked((byte)ToFloat(value._value));
        public static explicit operator checked sbyte(BFloat16Impl value) => checked((sbyte)ToFloat(value._value));
        public static explicit operator checked char(BFloat16Impl value) => checked((char)ToFloat(value._value));
        public static explicit operator checked nint(BFloat16Impl value) => checked((nint)ToFloat(value._value));
        public static explicit operator checked nuint(BFloat16Impl value) => checked((nuint)ToFloat(value._value));

        // ---- operators ----
        public static BFloat16Impl operator +(BFloat16Impl left, BFloat16Impl right) => FromFloat(ToFloat(left._value) + ToFloat(right._value));
        public static BFloat16Impl operator -(BFloat16Impl left, BFloat16Impl right) => FromFloat(ToFloat(left._value) - ToFloat(right._value));
        public static BFloat16Impl operator *(BFloat16Impl left, BFloat16Impl right) => FromFloat(ToFloat(left._value) * ToFloat(right._value));
        public static BFloat16Impl operator /(BFloat16Impl left, BFloat16Impl right) => FromFloat(ToFloat(left._value) / ToFloat(right._value));
        public static BFloat16Impl operator %(BFloat16Impl left, BFloat16Impl right) => FromFloat(ToFloat(left._value) % ToFloat(right._value));
        public static BFloat16Impl operator -(BFloat16Impl value) => new BFloat16Impl((ushort)(value._value ^ SignMask));
        public static BFloat16Impl operator +(BFloat16Impl value) => value;
        public static BFloat16Impl operator ++(BFloat16Impl value) => value + new BFloat16Impl(0x3F80);
        public static BFloat16Impl operator --(BFloat16Impl value) => value - new BFloat16Impl(0x3F80);

        public static bool operator ==(BFloat16Impl left, BFloat16Impl right) =>
            (left._value == right._value && !NaNBits(left._value)) || ((left._value | right._value) & 0x7FFF) == 0;
        public static bool operator !=(BFloat16Impl left, BFloat16Impl right) => !(left == right);
        public static bool operator <(BFloat16Impl left, BFloat16Impl right) => ToFloat(left._value) < ToFloat(right._value);
        public static bool operator >(BFloat16Impl left, BFloat16Impl right) => ToFloat(left._value) > ToFloat(right._value);
        public static bool operator <=(BFloat16Impl left, BFloat16Impl right) => ToFloat(left._value) <= ToFloat(right._value);
        public static bool operator >=(BFloat16Impl left, BFloat16Impl right) => ToFloat(left._value) >= ToFloat(right._value);

        // ---- math (float's, rounded back, as .NET) ----
        public static System.Numerics.BFloat16 Abs(System.Numerics.BFloat16 value) => Wrap((ushort)(Unwrap(value)._value & 0x7FFF));
        public static System.Numerics.BFloat16 CopySign(System.Numerics.BFloat16 value, System.Numerics.BFloat16 sign) => Wrap((ushort)((Unwrap(value)._value & 0x7FFF) | (Unwrap(sign)._value & SignMask)));
        public static System.Numerics.BFloat16 Max(System.Numerics.BFloat16 x, System.Numerics.BFloat16 y) => IsNaN(x) ? x : IsNaN(y) ? y : (x > y || (x == y && IsPositive(x))) ? x : y;
        public static System.Numerics.BFloat16 Min(System.Numerics.BFloat16 x, System.Numerics.BFloat16 y) => IsNaN(x) ? x : IsNaN(y) ? y : (x < y || (x == y && IsNegative(x))) ? x : y;
        public static System.Numerics.BFloat16 MaxMagnitude(System.Numerics.BFloat16 x, System.Numerics.BFloat16 y)
        {
            System.Numerics.BFloat16 ax = Abs(x), ay = Abs(y);
            return (ax > ay || IsNaN(ax)) ? x : (ax == ay) ? (IsNegative(x) ? y : x) : y;
        }
        public static System.Numerics.BFloat16 MinMagnitude(System.Numerics.BFloat16 x, System.Numerics.BFloat16 y)
        {
            System.Numerics.BFloat16 ax = Abs(x), ay = Abs(y);
            return (ax < ay || IsNaN(ax)) ? x : (ax == ay) ? (IsNegative(x) ? x : y) : y;
        }
        public static System.Numerics.BFloat16 Clamp(System.Numerics.BFloat16 value, System.Numerics.BFloat16 min, System.Numerics.BFloat16 max)
        {
            if (min > max) throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            return value < min ? min : value > max ? max : value;
        }
        public static int Sign(System.Numerics.BFloat16 value)
        {
            if (IsNaN(value)) throw new ArithmeticException("Function does not accept floating point Not-a-Number values.");
            return value < Wrap(0) ? -1 : value > Wrap(0) ? 1 : 0;
        }
        public static System.Numerics.BFloat16 Truncate(System.Numerics.BFloat16 value) => (System.Numerics.BFloat16)MathF.Truncate((float)value);
        public static System.Numerics.BFloat16 Floor(System.Numerics.BFloat16 value) => (System.Numerics.BFloat16)MathF.Floor((float)value);
        public static System.Numerics.BFloat16 Ceiling(System.Numerics.BFloat16 value) => (System.Numerics.BFloat16)MathF.Ceiling((float)value);
        public static System.Numerics.BFloat16 Round(System.Numerics.BFloat16 value) => (System.Numerics.BFloat16)MathF.Round((float)value);
        public static System.Numerics.BFloat16 Round(System.Numerics.BFloat16 value, MidpointRounding mode) => (System.Numerics.BFloat16)MathF.Round((float)value, mode);
        public static System.Numerics.BFloat16 Round(System.Numerics.BFloat16 value, int digits) => (System.Numerics.BFloat16)MathF.Round((float)value, digits);
        public static System.Numerics.BFloat16 Round(System.Numerics.BFloat16 value, int digits, MidpointRounding mode) => (System.Numerics.BFloat16)MathF.Round((float)value, digits, mode);
        public static System.Numerics.BFloat16 Sqrt(System.Numerics.BFloat16 value) => (System.Numerics.BFloat16)MathF.Sqrt((float)value);
        public static System.Numerics.BFloat16 Cbrt(System.Numerics.BFloat16 value) => (System.Numerics.BFloat16)MathF.Cbrt((float)value);
        public static System.Numerics.BFloat16 Pow(System.Numerics.BFloat16 x, System.Numerics.BFloat16 y) => (System.Numerics.BFloat16)MathF.Pow((float)x, (float)y);
        public static System.Numerics.BFloat16 Exp(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Exp((float)x);
        public static System.Numerics.BFloat16 Log(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Log((float)x);
        public static System.Numerics.BFloat16 Log2(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Log2((float)x);
        public static System.Numerics.BFloat16 Log10(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Log10((float)x);
        public static System.Numerics.BFloat16 Sin(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Sin((float)x);
        public static System.Numerics.BFloat16 Cos(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Cos((float)x);
        public static System.Numerics.BFloat16 Tan(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Tan((float)x);
        public static System.Numerics.BFloat16 Atan(System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Atan((float)x);
        public static System.Numerics.BFloat16 Atan2(System.Numerics.BFloat16 y, System.Numerics.BFloat16 x) => (System.Numerics.BFloat16)MathF.Atan2((float)y, (float)x);

        // ---- comparison and identity ----
        public int CompareTo(object obj)
        {
            if (obj is null) return 1;
            if (obj is System.Numerics.BFloat16 other) return CompareTo(other);
            throw new ArgumentException("Object must be of type BFloat16.");
        }

        public int CompareTo(System.Numerics.BFloat16 other)
        {
            BFloat16Impl o = Unwrap(other);
            if (this < o) return -1;
            if (this > o) return 1;
            if (this == o) return 0;
            if (NaNBits(_value)) return NaNBits(o._value) ? 0 : -1;
            return 1;
        }

        public override bool Equals(object obj) => obj is System.Numerics.BFloat16 other && Equals(other);

        public bool Equals(System.Numerics.BFloat16 other)
        {
            BFloat16Impl o = Unwrap(other);
            return _value == o._value || (NaNBits(_value) && NaNBits(o._value)) || this == o;
        }

        // .NET hashes the value as the float it is: NaNs and both zeros hash alike.
        public override int GetHashCode()
        {
            uint bits = _value;
            if (NaNBits(_value) || (_value & 0x7FFF) == 0) bits &= PositiveInfinityBits;
            return (int)(bits << 16);
        }

        // ---- parsing ----
        // Through double: one rounding to double then one to BFloat16. That differs from a single rounding only when the
        // double lands exactly on a midpoint between two BFloat16s from text with more digits than a double holds,
        // which is refused rather than guessed.
        private static bool IsHalfwayBetweenValues(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exp = (int)((bits >> 52) & 0x7FF);
            if (exp == 0 || exp == 0x7FF) return false;
            int e = exp - 1023;
            ulong m = ((ulong)bits & 0xFFFFFFFFFFFFFUL) | (1UL << 52);
            int shift = e >= -126 ? 45 : -81 - e;
            if (shift >= 64 || shift < 1) return false;
            return (m & ((1UL << shift) - 1)) == 1UL << (shift - 1);
        }

        private const NumberStyles DefaultStyle = NumberStyles.Float | NumberStyles.AllowThousands;

        private static bool TryParseCore(string text, NumberStyles style, IFormatProvider provider, out System.Numerics.BFloat16 result, bool throwOnFailure)
        {
            NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
            if (text == null || !Number.TryParseDoubleCore(text, style, NumberFormatInfo.GetInstance(provider), out double d))
            {
                if (throwOnFailure)
                {
                    if (text == null) throw new ArgumentNullException("s");
                    double.Parse(text, style, provider); // .NET's FormatException for the same text
                }
                result = default;
                return false;
            }
            if (IsHalfwayBetweenValues(d) && text.Length > 17)
                throw new NotSupportedException("BFloat16.Parse of '" + text + "': the text is within a double's precision of a rounding midpoint, which csharpc does not resolve yet.");
            result = Wrap(FromDouble(d));
            return true;
        }

        private static string Utf8(ReadOnlySpan<byte> utf8Text) => Encoding.UTF8.GetString(utf8Text);

        public static System.Numerics.BFloat16 Parse(string s) => Parse(s, DefaultStyle, null);
        public static System.Numerics.BFloat16 Parse(string s, IFormatProvider provider) => Parse(s, DefaultStyle, provider);
        public static System.Numerics.BFloat16 Parse(string s, NumberStyles style) => Parse(s, style, null);
        public static System.Numerics.BFloat16 Parse(string s, NumberStyles style, IFormatProvider provider) { TryParseCore(s, style, provider, out var r, true); return r; }
        public static System.Numerics.BFloat16 Parse(ReadOnlySpan<char> s, IFormatProvider provider) => Parse(s, DefaultStyle, provider);
        public static System.Numerics.BFloat16 Parse(ReadOnlySpan<char> s, NumberStyles style = DefaultStyle, IFormatProvider provider = null) { TryParseCore(s.ToString(), style, provider, out var r, true); return r; }
        public static System.Numerics.BFloat16 Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = DefaultStyle, IFormatProvider provider = null) { TryParseCore(Utf8(utf8Text), style, provider, out var r, true); return r; }
        public static System.Numerics.BFloat16 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider) => Parse(utf8Text, DefaultStyle, provider);

        public static bool TryParse(string s, out System.Numerics.BFloat16 result) => TryParseCore(s, DefaultStyle, null, out result, false);
        public static bool TryParse(string s, IFormatProvider provider, out System.Numerics.BFloat16 result) => TryParseCore(s, DefaultStyle, provider, out result, false);
        public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out System.Numerics.BFloat16 result) => TryParseCore(s, style, provider, out result, false);
        public static bool TryParse(ReadOnlySpan<char> s, out System.Numerics.BFloat16 result) => TryParseCore(s.ToString(), DefaultStyle, null, out result, false);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out System.Numerics.BFloat16 result) => TryParseCore(s.ToString(), DefaultStyle, provider, out result, false);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider provider, out System.Numerics.BFloat16 result) => TryParseCore(s.ToString(), style, provider, out result, false);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out System.Numerics.BFloat16 result) => TryParseCore(Utf8(utf8Text), DefaultStyle, null, out result, false);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider, out System.Numerics.BFloat16 result) => TryParseCore(Utf8(utf8Text), DefaultStyle, provider, out result, false);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider provider, out System.Numerics.BFloat16 result) => TryParseCore(Utf8(utf8Text), style, provider, out result, false);

        // ---- text ----
        public override string ToString() => ToString(null, null);
        public string ToString(string format) => ToString(format, null);
        public string ToString(IFormatProvider provider) => ToString(null, provider);

        public string ToString(string format, IFormatProvider provider)
        {
            double exact = ToDouble(_value);
            if (!double.IsFinite(exact) || (_value & 0x7FFF) == 0) return exact.ToString(format, provider);
            bool shortest = string.IsNullOrEmpty(format) || ((format[0] | 0x20) is 'g' or 'r' && (format.Length == 1 || (format.Length == 2 && format[1] == '0' && (format[0] | 0x20) == 'g')));
            if (!shortest) return exact.ToString(format, provider);
            // Shortest digits that read back as this BFloat16 (Dragon4's shortest mode): for the smallest digit count n at which
            // the n-digit decimal below or above the exact value converts back to these bits, the one that does (the nearer when
            // both do; an exact tie goes to the even last digit). Rounding to nearest alone would miss the powers of two, whose
            // interval is narrower below.
            Span<byte> storage = stackalloc byte[800];
            var g = new DigitBuffer(storage);
            Number.ExactDigitsOf(Math.Abs(exact), ref g);
            ushort magnitude = (ushort)(_value & 0x7FFF);
            var allChars = new char[g.N];
            for (int i = 0; i < g.N; i++) allChars[i] = (char)g.D[i];
            string all = new string(allChars);
            int scale = g.Exp10;
            string digits = all;
            // .NET's Dragon4 prints the smallest subnormal (9.18E-41) as 1E-40, not as the nearer 1-digit 9E-41 (every other finite
            // value agrees with the nearest-shortest choice below; sample 1290 compares the text of all of them).
            if (magnitude == 1) { digits = "1"; scale = -39; all = ""; }
            for (int n = 1; n <= all.Length; n++)
            {
                long lower = long.Parse(all.Substring(0, n), CultureInfo.InvariantCulture);
                double lowerValue = double.Parse(lower.ToString(CultureInfo.InvariantCulture) + "E" + (scale - n).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double upperValue = double.Parse((lower + 1).ToString(CultureInfo.InvariantCulture) + "E" + (scale - n).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                bool lowOk = FromDouble(lowerValue) == magnitude, highOk = FromDouble(upperValue) == magnitude;
                if (!lowOk && !highOk) continue;
                bool up;
                if (lowOk && highOk)
                {
                    // the nearer of the two: the dropped digits against exactly one half (5000...0); an exact tie goes to the even last digit
                    int tail = 0;
                    for (int k = n; k < all.Length && tail == 0; k++)
                        tail = k == n ? all[k] - '5' : all[k] - '0';
                    if (n == all.Length) up = false; // `lower` is the exact value
                    else if (all[n] != '5') up = all[n] > '5';
                    else up = tail > 0 || lower % 2 == 1;
                }
                else up = highOk;
                long chosen = up ? lower + 1 : lower;
                digits = chosen.ToString(CultureInfo.InvariantCulture);
                if (digits.Length > n) scale++; // 999 + 1
                break;
            }
            digits = digits.TrimEnd('0');
            bool lowerCase = !string.IsNullOrEmpty(format) && (format[0] == 'g' || format[0] == 'r');
            return Layout(exact < 0, digits, scale, lowerCase ? 'e' : 'E', NumberFormatInfo.GetInstance(provider));
        }

        // .NET's general layout of round-trip digits (value = 0.digits * 10^scale): scientific when the decimal point is more
        // than max(digits, 4) places to the right of the first digit, or more than three zeros to its left.
        private static string Layout(bool negative, string digits, int scale, char expChar, NumberFormatInfo nfi)
        {
            var sb = new StringBuilder();
            if (negative) sb.Append(nfi.NegativeSign);
            int maxDigits = Math.Max(digits.Length, 4);
            if (scale > maxDigits || scale < -3)
            {
                sb.Append(digits[0]);
                if (digits.Length > 1) sb.Append(nfi.NumberDecimalSeparator).Append(digits, 1, digits.Length - 1);
                int e = scale - 1;
                sb.Append(expChar).Append(e < 0 ? nfi.NegativeSign : nfi.PositiveSign);
                sb.Append(Math.Abs(e).ToString("00", CultureInfo.InvariantCulture));
            }
            else if (scale <= 0) sb.Append('0').Append(nfi.NumberDecimalSeparator).Append('0', -scale).Append(digits);
            else if (scale >= digits.Length) sb.Append(digits).Append('0', scale - digits.Length);
            else sb.Append(digits, 0, scale).Append(nfi.NumberDecimalSeparator).Append(digits, scale, digits.Length - scale);
            return sb.ToString();
        }

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(ToString(format.Length == 0 ? null : format.ToString(), provider), utf8Destination, out bytesWritten);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null)
        {
            string text = ToString(format.Length == 0 ? null : format.ToString(), provider);
            if (text.Length > destination.Length) { charsWritten = 0; return false; }
            text.AsSpan().CopyTo(destination);
            charsWritten = text.Length;
            return true;
        }
    }
}
