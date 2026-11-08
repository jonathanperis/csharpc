// System.Half (IEEE 754 binary16) as a 16-bit value, after dotnet/runtime's Half.cs (release/11.0; see
// THIRD-PARTY-NOTICES.md): conversions round to nearest even from the exact value (doubles directly,
// not through float), arithmetic is done in float and rounded back as .NET does, comparisons treat
// NaN as unordered and the zeros as equal, and text is the shortest digits that read back as the same
// Half in the general format (the exact value's digits when a precision is given), laid out by double's
// formatter. Parsing is not implemented (explicit diagnostic): it needs correct rounding from decimal text.
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.Half")]
    public readonly unsafe struct HalfImpl : IComparable, IComparable<Half>, IEquatable<Half>, IFormattable, ISpanFormattable
    {
        internal const ushort SignMask = 0x8000;
        internal const ushort PositiveInfinityBits = 0x7C00;
        internal const ushort NegativeInfinityBits = 0xFC00;
        internal const ushort QuietNaNBits = 0xFE00;

        private readonly ushort _value;

        internal HalfImpl(ushort bits) => _value = bits;

        private static Half Wrap(ushort bits) { var h = new HalfImpl(bits); return *(Half*)&h; }
        private static HalfImpl Unwrap(Half h) => *(HalfImpl*)&h;
        internal static Half FromBits(ushort bits) => Wrap(bits);
        internal static ushort BitsOf(Half h) => Unwrap(h)._value;

        public static Half Epsilon => Wrap(0x0001);
        public static Half MaxValue => Wrap(0x7BFF);
        public static Half MinValue => Wrap(0xFBFF);
        public static Half NaN => Wrap(QuietNaNBits);
        public static Half NegativeInfinity => Wrap(NegativeInfinityBits);
        public static Half PositiveInfinity => Wrap(PositiveInfinityBits);
        public static Half NegativeZero => Wrap(SignMask);

        // ---- classification ----
        private static bool NaNBits(ushort v) => (v & 0x7FFF) > PositiveInfinityBits;
        public static bool IsNaN(Half value) => NaNBits(Unwrap(value)._value);
        public static bool IsInfinity(Half value) => (Unwrap(value)._value & 0x7FFF) == PositiveInfinityBits;
        public static bool IsPositiveInfinity(Half value) => Unwrap(value)._value == PositiveInfinityBits;
        public static bool IsNegativeInfinity(Half value) => Unwrap(value)._value == NegativeInfinityBits;
        public static bool IsFinite(Half value) => (Unwrap(value)._value & 0x7FFF) < PositiveInfinityBits;
        public static bool IsNegative(Half value) => (Unwrap(value)._value & SignMask) != 0;
        public static bool IsPositive(Half value) => (Unwrap(value)._value & SignMask) == 0;
        public static bool IsNormal(Half value) { int e = (Unwrap(value)._value & 0x7C00) >> 10; return e != 0 && e != 0x1F; }
        public static bool IsSubnormal(Half value) { ushort v = Unwrap(value)._value; return (v & 0x7C00) == 0 && (v & 0x03FF) != 0; }
        public static bool IsRealNumber(Half value) => !IsNaN(value);
        public static bool IsInteger(Half value) => IsFinite(value) && value == Truncate(value);
        public static bool IsEvenInteger(Half value) => IsInteger(value) && Math.Abs((double)value % 2) == 0;
        public static bool IsOddInteger(Half value) => IsInteger(value) && Math.Abs((double)value % 2) == 1;

        // ---- conversions ----
        private static float ToFloat(ushort bits) => (float)ToDouble(bits);

        private static double ToDouble(ushort bits)
        {
            int sign = (bits & SignMask) != 0 ? -1 : 1;
            int exp = (bits >> 10) & 0x1F;
            int mant = bits & 0x3FF;
            if (exp == 0x1F)
            {
                if (mant == 0) return sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
                // NaN keeps its sign and payload (quieted)
                return BitConverter.Int64BitsToDouble((sign < 0 ? long.MinValue : 0) | 0x7FF8000000000000L | ((long)mant << 42));
            }
            if (exp == 0) return sign * (mant * (1.0 / 16777216.0)); // subnormal: mant * 2^-24
            long normal = ((long)(exp - 15 + 1023) << 52) | ((long)mant << 42);
            return sign * BitConverter.Int64BitsToDouble(normal);
        }

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
                // NaN: quiet, keeping the top payload bits (a signaling NaN becomes quiet).
                return (ushort)(sign | 0x7E00 | (int)((mant >> 42) & 0x1FF));
            }
            if (exp == 0) return sign; // zero or a double subnormal: far below Half's smallest
            int e = exp - 1023;
            ulong m = (1UL << 52) | mant;
            if (e >= -14)
            {
                ulong q = ShiftRoundEven(m, 42); // 11-bit significand, 2048 after a carry
                int result = ((e + 14) << 10) + (int)q;
                return result >= PositiveInfinityBits ? (ushort)(sign | PositiveInfinityBits) : (ushort)(sign | result);
            }
            return (ushort)(sign | (int)ShiftRoundEven(m, 28 - e));
        }

        public static explicit operator HalfImpl(double value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(float value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(int value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(long value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(uint value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(ulong value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(short value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(ushort value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(char value) => new HalfImpl(FromDouble(value));
        public static explicit operator HalfImpl(nint value) => new HalfImpl(FromDouble((long)value));
        public static explicit operator HalfImpl(nuint value) => new HalfImpl(FromDouble((ulong)value));
        public static explicit operator HalfImpl(decimal value) => new HalfImpl(FromDouble((double)value));
        public static implicit operator HalfImpl(byte value) => new HalfImpl(FromDouble(value));
        public static implicit operator HalfImpl(sbyte value) => new HalfImpl(FromDouble(value));

        public static explicit operator double(HalfImpl value) => ToDouble(value._value);
        public static explicit operator float(HalfImpl value) => ToFloat(value._value);
        public static explicit operator decimal(HalfImpl value) => (decimal)ToDouble(value._value);
        public static explicit operator int(HalfImpl value) => (int)ToFloat(value._value);
        public static explicit operator long(HalfImpl value) => (long)ToFloat(value._value);
        public static explicit operator uint(HalfImpl value) => (uint)ToFloat(value._value);
        public static explicit operator ulong(HalfImpl value) => (ulong)ToFloat(value._value);
        public static explicit operator short(HalfImpl value) => (short)ToFloat(value._value);
        public static explicit operator ushort(HalfImpl value) => (ushort)ToFloat(value._value);
        public static explicit operator byte(HalfImpl value) => (byte)ToFloat(value._value);
        public static explicit operator sbyte(HalfImpl value) => (sbyte)ToFloat(value._value);
        public static explicit operator char(HalfImpl value) => (char)ToFloat(value._value);
        public static explicit operator nint(HalfImpl value) => (nint)ToFloat(value._value);
        public static explicit operator nuint(HalfImpl value) => (nuint)ToFloat(value._value);
        public static explicit operator checked int(HalfImpl value) => checked((int)ToFloat(value._value));
        public static explicit operator checked long(HalfImpl value) => checked((long)ToFloat(value._value));
        public static explicit operator checked uint(HalfImpl value) => checked((uint)ToFloat(value._value));
        public static explicit operator checked ulong(HalfImpl value) => checked((ulong)ToFloat(value._value));
        public static explicit operator checked short(HalfImpl value) => checked((short)ToFloat(value._value));
        public static explicit operator checked ushort(HalfImpl value) => checked((ushort)ToFloat(value._value));
        public static explicit operator checked byte(HalfImpl value) => checked((byte)ToFloat(value._value));
        public static explicit operator checked sbyte(HalfImpl value) => checked((sbyte)ToFloat(value._value));

        // ---- operators ----
        public static HalfImpl operator +(HalfImpl left, HalfImpl right) => new HalfImpl(FromDouble(ToFloat(left._value) + ToFloat(right._value)));
        public static HalfImpl operator -(HalfImpl left, HalfImpl right) => new HalfImpl(FromDouble(ToFloat(left._value) - ToFloat(right._value)));
        public static HalfImpl operator *(HalfImpl left, HalfImpl right) => new HalfImpl(FromDouble(ToFloat(left._value) * ToFloat(right._value)));
        public static HalfImpl operator /(HalfImpl left, HalfImpl right) => new HalfImpl(FromDouble(ToFloat(left._value) / ToFloat(right._value)));
        public static HalfImpl operator %(HalfImpl left, HalfImpl right) => new HalfImpl(FromDouble(ToFloat(left._value) % ToFloat(right._value)));
        public static HalfImpl operator -(HalfImpl value) => new HalfImpl((ushort)(value._value ^ SignMask));
        public static HalfImpl operator +(HalfImpl value) => value;
        public static HalfImpl operator ++(HalfImpl value) => value + new HalfImpl(FromDouble(1.0));
        public static HalfImpl operator --(HalfImpl value) => value - new HalfImpl(FromDouble(1.0));

        public static bool operator ==(HalfImpl left, HalfImpl right) =>
            (left._value == right._value && !NaNBits(left._value)) || ((left._value | right._value) & 0x7FFF) == 0;
        public static bool operator !=(HalfImpl left, HalfImpl right) => !(left == right);
        public static bool operator <(HalfImpl left, HalfImpl right) => ToFloat(left._value) < ToFloat(right._value);
        public static bool operator >(HalfImpl left, HalfImpl right) => ToFloat(left._value) > ToFloat(right._value);
        public static bool operator <=(HalfImpl left, HalfImpl right) => ToFloat(left._value) <= ToFloat(right._value);
        public static bool operator >=(HalfImpl left, HalfImpl right) => ToFloat(left._value) >= ToFloat(right._value);

        // ---- math ----
        public static Half Abs(Half value) => Wrap((ushort)(Unwrap(value)._value & 0x7FFF));
        public static Half CopySign(Half value, Half sign) => Wrap((ushort)((Unwrap(value)._value & 0x7FFF) | (Unwrap(sign)._value & SignMask)));
        public static Half Max(Half x, Half y) => IsNaN(x) ? x : IsNaN(y) ? y : (x > y || (x == y && IsPositive(x))) ? x : y;
        public static Half Min(Half x, Half y) => IsNaN(x) ? x : IsNaN(y) ? y : (x < y || (x == y && IsNegative(x))) ? x : y;
        public static Half Clamp(Half value, Half min, Half max)
        {
            if (min > max) throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            return value < min ? min : value > max ? max : value;
        }
        public static int Sign(Half value)
        {
            if (IsNaN(value)) throw new ArithmeticException("Function does not accept floating point Not-a-Number values.");
            return value < Wrap(0) ? -1 : value > Wrap(0) ? 1 : 0;
        }
        public static Half Truncate(Half value) => (Half)Math.Truncate((double)value);
        public static Half Floor(Half value) => (Half)Math.Floor((double)value);
        public static Half Ceiling(Half value) => (Half)Math.Ceiling((double)value);
        public static Half Round(Half value) => (Half)Math.Round((double)value);
        public static Half Sqrt(Half value) => (Half)MathF.Sqrt((float)value);
        public static Half Pow(Half x, Half y) => (Half)MathF.Pow((float)x, (float)y);

        // ---- comparison and identity ----
        public int CompareTo(object obj)
        {
            if (obj is null) return 1;
            if (obj is Half other) return CompareTo(other);
            throw new ArgumentException("Object must be of type Half.");
        }

        public int CompareTo(Half other)
        {
            HalfImpl o = Unwrap(other);
            if (this < o) return -1;
            if (this > o) return 1;
            if (this == o) return 0;
            if (NaNBits(_value)) return NaNBits(o._value) ? 0 : -1;
            return 1;
        }

        public override bool Equals(object obj) => obj is Half other && Equals(other);

        public bool Equals(Half other)
        {
            HalfImpl o = Unwrap(other);
            return _value == o._value || (NaNBits(_value) && NaNBits(o._value)) || this == o;
        }

        public override int GetHashCode()
        {
            uint bits = _value;
            // all NaNs and both zeros hash alike
            if (NaNBits(_value) || (_value & 0x7FFF) == 0) bits &= PositiveInfinityBits;
            return (int)bits;
        }

        // ---- parsing ----
        // Through double: one rounding to double then one to Half. That differs from a single rounding only when the
        // double lands exactly on a midpoint between two Halves from text with more digits than a double holds,
        // which is refused rather than guessed.
        private static bool IsHalfwayBetweenHalves(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exp = (int)((bits >> 52) & 0x7FF);
            if (exp == 0 || exp == 0x7FF) return false;
            int e = exp - 1023;
            ulong m = ((ulong)bits & 0xFFFFFFFFFFFFFUL) | (1UL << 52);
            int shift = e >= -14 ? 42 : 28 - e;
            if (shift >= 64 || shift < 1) return false;
            return (m & ((1UL << shift) - 1)) == 1UL << (shift - 1);
        }

        private static bool TryParseCore(ReadOnlySpan<char> text, NumberStyles style, IFormatProvider provider, out Half result, bool throwOnFailure)
        {
            if (!double.TryParse(text, style, provider, out double d))
            {
                if (throwOnFailure) double.Parse(text, style, provider); // .NET's FormatException for the same text
                result = default;
                return false;
            }
            if (IsHalfwayBetweenHalves(d) && text.Length > 17)
                throw new NotSupportedException("Half.Parse of '" + text.ToString() + "': the text is within a double's precision of a rounding midpoint, which csharpc does not resolve yet.");
            result = Wrap(FromDouble(d));
            return true;
        }

        private const NumberStyles DefaultStyle = NumberStyles.Float | NumberStyles.AllowThousands;

        public static Half Parse(string s) => Parse(s, DefaultStyle, null);
        public static Half Parse(string s, IFormatProvider provider) => Parse(s, DefaultStyle, provider);
        public static Half Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static Half Parse(string s, NumberStyles style, IFormatProvider provider)
        {
            if (s == null) throw new ArgumentNullException("s");
            TryParseCore(s.AsSpan(), style, provider, out Half result, true);
            return result;
        }

        public static Half Parse(ReadOnlySpan<char> s, IFormatProvider provider) => Parse(s, DefaultStyle, provider);

        public static Half Parse(ReadOnlySpan<char> s, NumberStyles style = DefaultStyle, IFormatProvider provider = null)
        {
            TryParseCore(s, style, provider, out Half result, true);
            return result;
        }

        public static bool TryParse(string s, out Half result) => TryParse(s, DefaultStyle, null, out result);
        public static bool TryParse(string s, IFormatProvider provider, out Half result) => TryParse(s, DefaultStyle, provider, out result);

        public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out Half result)
        {
            if (s == null) { result = default; return false; }
            return TryParseCore(s.AsSpan(), style, provider, out result, false);
        }

        public static bool TryParse(ReadOnlySpan<char> s, out Half result) => TryParse(s, DefaultStyle, null, out result);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out Half result) => TryParse(s, DefaultStyle, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider provider, out Half result) =>
            TryParseCore(s, style, provider, out result, false);

        // IUtf8SpanParsable: the text of the bytes, parsed as the chars would be (bytes that aren't valid UTF-8 decode to
        // U+FFFD, which no number syntax accepts).
        public static Half Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = DefaultStyle, IFormatProvider provider = null) =>
            Parse(System.Text.Encoding.UTF8.GetString(utf8Text).AsSpan(), style, provider);
        public static Half Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider) => Parse(utf8Text, DefaultStyle, provider);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider provider, out Half result) =>
            TryParseCore(System.Text.Encoding.UTF8.GetString(utf8Text).AsSpan(), style, provider, out result, false);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider, out Half result) =>
            TryParse(utf8Text, DefaultStyle, provider, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Half result) => TryParse(utf8Text, DefaultStyle, null, out result);

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
            // Shortest digits that read back as this Half (Dragon4's shortest mode): for the smallest digit count n
            // at which the n-digit decimal below or above the exact value converts back to these bits, the one that
            // does (the nearer when both do; an exact tie goes to the even last digit). Rounding to nearest alone
            // would miss the powers of two, whose interval is narrower below.
            string e = Math.Abs(exact).ToString("E25", CultureInfo.InvariantCulture); // d.ddd...E+xxx, the exact digits
            int ePos = e.IndexOf('E');
            string all = e[0] + e.Substring(2, ePos - 2);
            int exp10 = int.Parse(e.Substring(ePos + 1), CultureInfo.InvariantCulture);
            double text = exact;
            for (int digits = 1; digits <= 5; digits++)
            {
                long lower = long.Parse(all.Substring(0, digits), CultureInfo.InvariantCulture);
                double lowerValue = double.Parse(lower.ToString(CultureInfo.InvariantCulture) + "E" + (exp10 - (digits - 1)).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double upperValue = double.Parse((lower + 1).ToString(CultureInfo.InvariantCulture) + "E" + (exp10 - (digits - 1)).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                ushort magnitude = (ushort)(_value & 0x7FFF);
                bool lowOk = FromDouble(lowerValue) == magnitude, highOk = FromDouble(upperValue) == magnitude;
                if (!lowOk && !highOk) continue;
                bool up;
                if (lowOk && highOk)
                {
                    int tail = 0; // the dropped digits against exactly one half: 5000...0
                    for (int k = digits; k < all.Length && tail == 0; k++)
                        tail = k == digits ? all[k] - '5' : all[k] - '0';
                    up = tail > 0 || (tail == 0 && lower % 2 == 1);
                }
                else up = highOk;
                text = Math.Sign(exact) * (up ? upperValue : lowerValue);
                break;
            }
            return text.ToString(format is { Length: > 0 } && (format[0] | 0x20) == 'r' ? "G" : format, provider);
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
