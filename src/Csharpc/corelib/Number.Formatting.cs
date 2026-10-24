// Number formatting in C# (formerly src/runtime/format.c and format_real in csrt.c): .NET's
// standard (C D E F G N P R X B) and custom ("#,##0.00", "0.0E+00", sections, quotes, %, ‰) numeric
// format strings for the invariant culture, composite formatting, and shortest round-trip
// double/float output. The structure follows .NET's Number.Formatting.cs: a value becomes a
// DigitBuffer (digits d1d2... and Exp10 with value = 0.d1d2... * 10^Exp10), which the formatters
// round and lay out.
//
//   * double/float digits are exact (BigNum expansion of the binary value) and rounded to nearest,
//     ties to even, like .NET Core 3.0+ (0.125.ToString("F2") == "0.12"); the shortest round-trip
//     digits ("R", "G", ToString()) come from Steele & White / Burger & Dybvig free-format generation.
//   * custom formats see doubles rounded to 15 significant digits (floats: 7), then round half up.
//   * integers and decimals round half up, and never print a negative zero.
#nullable disable
using System;
using NumberFormatInfo = CsharpcCoreLib.Globalization.NumberFormatInfo;

namespace CsharpcCoreLib
{
    /// <summary>Decimal digits of a number: value = 0.D[0]D[1]...D[N-1] * 10^Exp10 (no leading zeros).</summary>
    internal ref struct DigitBuffer
    {
        public Span<byte> D;
        public int N;
        public int Exp10;
        public bool Neg;
        public bool IsFloat;

        public DigitBuffer(Span<byte> storage) { D = storage; N = 0; Exp10 = 0; Neg = false; IsFloat = false; }

        public bool HasDigit(int i) => i < N;
    }

    /// <summary>A growable char buffer that starts on the stack.</summary>
    internal ref struct CharBuilder
    {
        private Span<char> _chars;
        private int _pos;

        public CharBuilder(Span<char> initial) { _chars = initial; _pos = 0; }

        public int Length => _pos;

        public char LastChar => _chars[_pos - 1];

        public void Truncate(int length) { _pos = length; }

        public void Append(char c)
        {
            if (_pos == _chars.Length) Grow(1);
            _chars[_pos++] = c;
        }

        public void Append(string s)
        {
            if (s == null) return;
            if (_pos + s.Length > _chars.Length) Grow(s.Length);
            for (int i = 0; i < s.Length; i++) _chars[_pos + i] = s[i];
            _pos += s.Length;
        }

        public void Append(char c, int count)
        {
            if (count <= 0) return;
            if (_pos + count > _chars.Length) Grow(count);
            for (int i = 0; i < count; i++) _chars[_pos + i] = c;
            _pos += count;
        }

        public void Insert(int index, string s)
        {
            for (int i = s.Length - 1; i >= 0; i--) Insert(index, s[i]);
        }

        public void Insert(int index, char c)
        {
            if (_pos == _chars.Length) Grow(1);
            for (int i = _pos; i > index; i--) _chars[i] = _chars[i - 1];
            _chars[index] = c;
            _pos++;
        }

        private void Grow(int extra)
        {
            int size = _chars.Length * 2;
            if (size < _pos + extra) size = _pos + extra;
            if (size < 64) size = 64;
            char[] array = new char[size];
            for (int i = 0; i < _pos; i++) array[i] = _chars[i];
            _chars = array;
        }

        public string Build()
        {
            if (_pos == 0) return "";
            string s = CoreIntrinsics.FastAllocateString(_pos);
            Span<char> dst = CoreIntrinsics.StringChars(s);
            for (int i = 0; i < _pos; i++) dst[i] = _chars[i];
            return s;
        }
    }

    internal static partial class Number
    {
        private const int DoublePrecisionCustomFormat = 15;
        private const int SinglePrecisionCustomFormat = 7;
        private const int DigitStorage = 800;  // a double's exact expansion has at most 767 significant digits

        private static void ThrowBadFormat() => throw new FormatException("Format specifier was invalid.");

        // ==========================================================================================
        // Digit generation
        // ==========================================================================================

        /// <summary>Splits a double into f * 2^e. Returns false for NaN/infinity.</summary>
        private static void Decompose(double value, out ulong f, out int e, out bool lowerBoundaryCloser)
        {
            long bits = CoreIntrinsics.DoubleToBits(value);
            int biased = (int)((bits >> 52) & 0x7FF);
            ulong fraction = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (biased == 0) { f = fraction; e = -1074; }
            else { f = fraction | (1UL << 52); e = biased - 1075; }
            lowerBoundaryCloser = fraction == 0 && biased > 1;
        }

        private static void Decompose(float value, out ulong f, out int e, out bool lowerBoundaryCloser)
        {
            int bits = CoreIntrinsics.SingleToBits(value);
            int biased = (bits >> 23) & 0xFF;
            uint fraction = (uint)bits & 0x7FFFFF;
            if (biased == 0) { f = fraction; e = -149; }
            else { f = fraction | (1U << 23); e = biased - 150; }
            lowerBoundaryCloser = fraction == 0 && biased > 1;
        }

        /// <summary>Exact decimal digits of a finite, nonzero double (decimal conversion).</summary>
        internal static void ExactDigitsOf(double value, ref DigitBuffer g)
        {
            Decompose(value, out ulong f, out int e, out bool _);
            ExactDigits(f, e, ref g);
        }

        /// <summary>Exact decimal digits of f * 2^e (f != 0).</summary>
        private static void ExactDigits(ulong f, int e, ref DigitBuffer g)
        {
            while ((f & 1) == 0) { f >>= 1; e++; }
            Span<uint> storage = stackalloc uint[90];
            var n = new BigNum(storage);
            n.SetUInt64(f);
            int exp10 = 0;  // value = n * 10^exp10
            if (e > 0) n.ShiftLeft(e);
            else if (e < 0) { n.MulPow5(-e); exp10 = e; }  // f * 2^e = f * 5^-e * 10^e

            Span<uint> chunks = stackalloc uint[90];  // base 10^9, least significant first
            int count = 0;
            while (!n.IsZero) chunks[count++] = n.DivRemSmall(1000000000);
            int len = 0;
            uint top = chunks[count - 1];
            len = WriteChunk(g.D, 0, top, false);
            for (int i = count - 2; i >= 0; i--) len = WriteChunk(g.D, len, chunks[i], true);
            g.Exp10 = len + exp10;
            while (len > 0 && g.D[len - 1] == (byte)'0') len--;
            g.N = len;
        }

        private static int WriteChunk(Span<byte> dst, int at, uint chunk, bool pad)
        {
            int digits = pad ? 9 : CountDigits(chunk);
            for (int i = digits - 1; i >= 0; i--) { dst[at + i] = (byte)('0' + (int)(chunk % 10)); chunk /= 10; }
            return at + digits;
        }

        private static void IntegerDigits(ulong magnitude, ref DigitBuffer g)
        {
            if (magnitude == 0) { g.N = 0; g.Exp10 = 0; return; }
            int digits = CountDigits(magnitude);
            for (int i = digits - 1; i >= 0; i--) { g.D[i] = (byte)('0' + (int)(magnitude % 10)); magnitude /= 10; }
            g.N = digits;
            g.Exp10 = digits;
            while (g.N > 0 && g.D[g.N - 1] == (byte)'0') g.N--;
        }

        /// <summary>
        /// Shortest digits that round-trip to f * 2^e (Steele &amp; White / Burger &amp; Dybvig free-format
        /// algorithm on exact big integers); among equally short candidates, the closest.
        /// </summary>
        private static void ShortestDigits(ulong f, int e, bool lowerBoundaryCloser, ref DigitBuffer g)
        {
            Span<uint> rs = stackalloc uint[48];
            Span<uint> ss = stackalloc uint[48];
            Span<uint> ps = stackalloc uint[48];
            Span<uint> ms = stackalloc uint[48];
            var r = new BigNum(rs);
            var s = new BigNum(ss);
            var mPlus = new BigNum(ps);
            var mMinus = new BigNum(ms);
            bool even = (f & 1) == 0;
            // value = r / s; the rounding interval is (r - mMinus, r + mPlus) / s.
            r.SetUInt64(f);
            if (e >= 0)
            {
                mPlus.SetUInt64(1); mPlus.ShiftLeft(e);
                mMinus.SetUInt64(1); mMinus.ShiftLeft(e);
                if (!lowerBoundaryCloser) { r.ShiftLeft(e + 1); s.SetUInt64(2); }
                else { r.ShiftLeft(e + 2); s.SetUInt64(4); mPlus.ShiftLeft(1); }
            }
            else
            {
                mPlus.SetUInt64(1);
                mMinus.SetUInt64(1);
                s.SetUInt64(1);
                if (!lowerBoundaryCloser) { r.ShiftLeft(1); s.ShiftLeft(1 - e); }
                else { r.ShiftLeft(2); s.ShiftLeft(2 - e); mPlus.SetUInt64(2); }
            }

            // k = ceil(log10(value)), estimated from the bit length (exact or one too small).
            int bitLength = 0;
            for (ulong t = f; t != 0; t >>= 1) bitLength++;
            int k = (int)Math.Ceiling((e + bitLength - 1) * 0.30102999566398114 - 1e-10);
            if (k >= 0) s.MulPow10(k);
            else
            {
                r.MulPow10(-k);
                mPlus.MulPow10(-k);
                mMinus.MulPow10(-k);
            }
            int high = BigNum.CompareSum(in r, in mPlus, in s);
            if (even ? high >= 0 : high > 0) { s.MulSmall(10); k++; }

            int n = 0;
            while (true)
            {
                r.MulSmall(10);
                mPlus.MulSmall(10);
                mMinus.MulSmall(10);
                uint digit = r.DivRemDigit(in s);
                int low = BigNum.Compare(in r, in mMinus);
                high = BigNum.CompareSum(in r, in mPlus, in s);
                bool tc1 = even ? low <= 0 : low < 0;
                bool tc2 = even ? high >= 0 : high > 0;
                if (!tc1 && !tc2) { g.D[n++] = (byte)('0' + digit); continue; }
                if (tc1 && tc2)
                {
                    // Both neighbours round-trip: take the closer one (ties to even).
                    int half = BigNum.CompareSum(in r, in r, in s);
                    if (half > 0 || (half == 0 && (digit & 1) == 1)) digit++;
                }
                else if (tc2) digit++;
                g.D[n++] = (byte)('0' + digit);
                break;
            }
            g.N = n;
            g.Exp10 = k;
            while (g.N > 0 && g.D[g.N - 1] == (byte)'0') g.N--;
        }

        /// <summary>
        /// .NET's RoundNumber: keep `pos` digits. Ties go to even when the digits are an exact binary
        /// value (tiesToEven), otherwise half up. A zero result has Exp10 0 and, unless it's a
        /// floating-point value, no sign.
        /// </summary>
        private static void Round(ref DigitBuffer g, int pos, bool tiesToEven)
        {
            int i = pos < 0 ? 0 : pos < g.N ? pos : g.N;
            bool up = false;
            if (pos >= 0 && pos < g.N)
            {
                byte d = g.D[pos];
                up = !tiesToEven
                    ? d >= (byte)'5'
                    : d > (byte)'5' || (d == (byte)'5' && (pos + 1 < g.N || (pos > 0 && ((g.D[pos - 1] - '0') & 1) == 1)));
            }
            if (up)
            {
                while (i > 0 && g.D[i - 1] == (byte)'9') i--;
                if (i > 0) g.D[i - 1]++;
                else { g.Exp10++; g.D[0] = (byte)'1'; i = 1; }
            }
            else
            {
                while (i > 0 && g.D[i - 1] == (byte)'0') i--;
            }
            if (i == 0)
            {
                if (!g.IsFloat) g.Neg = false;
                g.Exp10 = 0;
            }
            g.N = i;
        }

        // ==========================================================================================
        // Shortest round-trip: double.ToString(), float.ToString(), "R", "G"
        // ==========================================================================================

        [RuntimeExport("csrt_string_from_f64", "csrt_Boxed_Double_MT")]
        internal static string DoubleToString(double value) => DoubleToString(value, NumberFormatInfo.CurrentInfo);

        internal static string DoubleToString(double value, NumberFormatInfo nfi)
        {
            if (value != value) return nfi.NaNSymbol;
            double magnitude = value < 0 ? -value : value;
            if (magnitude < 9007199254740992.0)
            {
                long integer = (long)magnitude;
                if (integer == magnitude && magnitude != 0)
                    return value < 0 ? NegativeInt64ToDecStr(-integer, nfi.NegativeSign) : UInt64ToDecStr((ulong)integer);
            }
            Span<byte> storage = stackalloc byte[32];
            var g = new DigitBuffer(storage);
            Span<char> chars = stackalloc char[32];
            var sb = new CharBuilder(chars);
            if (!ShortestDouble(value, ref g, ref sb, nfi)) FormatGeneral(ref sb, ref g, 17, 'E', false, nfi);
            return sb.Build();
        }

        [RuntimeExport("csrt_string_from_f32", "csrt_Boxed_Single_MT")]
        internal static string SingleToString(float value) => SingleToString(value, NumberFormatInfo.CurrentInfo);

        internal static string SingleToString(float value, NumberFormatInfo nfi)
        {
            if (value != value) return nfi.NaNSymbol;
            float magnitude = value < 0 ? -value : value;
            if (magnitude < 16777216f)
            {
                int integer = (int)magnitude;
                if (integer == magnitude && magnitude != 0)
                    return value < 0 ? NegativeInt64ToDecStr(-integer, nfi.NegativeSign) : UInt64ToDecStr((ulong)integer);
            }
            Span<byte> storage = stackalloc byte[16];
            var g = new DigitBuffer(storage);
            Span<char> chars = stackalloc char[32];
            var sb = new CharBuilder(chars);
            if (!ShortestSingle(value, ref g, ref sb, nfi)) FormatGeneral(ref sb, ref g, 9, 'E', false, nfi);
            return sb.Build();
        }

        /// <summary>Shortest digits of a double; writes the special values (and the sign) and returns true if done.</summary>
        private static bool ShortestDouble(double value, ref DigitBuffer g, ref CharBuilder sb, NumberFormatInfo nfi)
        {
            g.IsFloat = true;
            g.Neg = CoreIntrinsics.DoubleToBits(value) < 0;
            if (value != value) { sb.Append(nfi.NaNSymbol); return true; }
            if (value == double.PositiveInfinity) { sb.Append(nfi.PositiveInfinitySymbol); return true; }
            if (value == double.NegativeInfinity) { sb.Append(nfi.NegativeInfinitySymbol); return true; }
            if (g.Neg) sb.Append(nfi.NegativeSign);
            if (value == 0) { g.N = 0; g.Exp10 = 0; return false; }
            Decompose(value, out ulong f, out int e, out bool closer);
            ShortestDigits(f, e, closer, ref g);
            return false;
        }

        private static bool ShortestSingle(float value, ref DigitBuffer g, ref CharBuilder sb, NumberFormatInfo nfi)
        {
            g.IsFloat = true;
            g.Neg = CoreIntrinsics.SingleToBits(value) < 0;
            if (value != value) { sb.Append(nfi.NaNSymbol); return true; }
            if (value == float.PositiveInfinity) { sb.Append(nfi.PositiveInfinitySymbol); return true; }
            if (value == float.NegativeInfinity) { sb.Append(nfi.NegativeInfinitySymbol); return true; }
            if (g.Neg) sb.Append(nfi.NegativeSign);
            if (value == 0) { g.N = 0; g.Exp10 = 0; return false; }
            Decompose(value, out ulong f, out int e, out bool closer);
            ShortestDigits(f, e, closer, ref g);
            return false;
        }

        // ==========================================================================================
        // Layout: .NET's NumberToString and its helpers (src/libraries/Common/src/System/
        // Number.Formatting.Common.cs, release/11.0, commit bc123d0bd173; MIT, see THIRD-PARTY-NOTICES.md),
        // over csharpc's DigitBuffer and with the culture's NumberFormatInfo. The sign is written by the
        // caller unless noted.
        // ==========================================================================================

        private static string CurrencyFormat(bool isNegative, int index)
        {
            if (isNegative)
            {
                switch (index)
                {
                    case 0: return "($#)";
                    case 1: return "-$#";
                    case 2: return "$-#";
                    case 3: return "$#-";
                    case 4: return "(#$)";
                    case 5: return "-#$";
                    case 6: return "#-$";
                    case 7: return "#$-";
                    case 8: return "-# $";
                    case 9: return "-$ #";
                    case 10: return "# $-";
                    case 11: return "$ #-";
                    case 12: return "$ -#";
                    case 13: return "#- $";
                    case 14: return "($ #)";
                    case 15: return "(# $)";
                    default: return "$- #";
                }
            }
            switch (index)
            {
                case 0: return "$#";
                case 1: return "#$";
                case 2: return "$ #";
                default: return "# $";
            }
        }

        private static string PercentFormat(bool isNegative, int index)
        {
            if (isNegative)
            {
                switch (index)
                {
                    case 0: return "-# %";
                    case 1: return "-#%";
                    case 2: return "-%#";
                    case 3: return "%-#";
                    case 4: return "%#-";
                    case 5: return "#-%";
                    case 6: return "#%-";
                    case 7: return "-% #";
                    case 8: return "# %-";
                    case 9: return "% #-";
                    case 10: return "% -#";
                    default: return "#- %";
                }
            }
            switch (index)
            {
                case 0: return "# %";
                case 1: return "#%";
                case 2: return "%#";
                default: return "% #";
            }
        }

        private static string NumberNegativeFormat(int index)
        {
            switch (index)
            {
                case 0: return "(#)";
                case 1: return "-#";
                case 2: return "- #";
                case 3: return "#-";
                default: return "# -";
            }
        }

        /// <summary>"G": fixed unless the exponent is >= maxDigits or below -4 (then d.dddE+xx).</summary>
        private static void FormatGeneral(ref CharBuilder sb, ref DigitBuffer g, int maxDigits, char expChar, bool suppressScientific, NumberFormatInfo nfi)
        {
            int digPos = g.Exp10;
            bool scientific = false;
            if (!suppressScientific && (digPos > maxDigits || digPos < -3)) { digPos = 1; scientific = true; }
            int di = 0;
            if (digPos > 0)
            {
                do { sb.Append(di < g.N ? (char)g.D[di++] : '0'); } while (--digPos > 0);
            }
            else sb.Append('0');
            if (di < g.N || digPos < 0)
            {
                sb.Append(nfi.NumberDecimalSeparator);
                while (digPos < 0) { sb.Append('0'); digPos++; }
                while (di < g.N) sb.Append((char)g.D[di++]);
            }
            if (scientific) FormatExponent(ref sb, g.Exp10 - 1, expChar, 2, true, nfi);
        }

        private static void FormatExponent(ref CharBuilder sb, int value, char expChar, int minDigits, bool positiveSign, NumberFormatInfo nfi)
        {
            sb.Append(expChar);
            if (value < 0) { sb.Append(nfi.NegativeSign); value = -value; }
            else if (positiveSign) sb.Append(nfi.PositiveSign);
            int digits = CountDigits((ulong)value);
            sb.Append('0', minDigits - digits);
            Span<char> tmp = stackalloc char[10];
            WriteDigits(tmp.Slice(0, digits), (ulong)value);
            for (int i = 0; i < digits; i++) sb.Append(tmp[i]);
        }

        private static void FormatScientific(ref CharBuilder sb, ref DigitBuffer g, int digits, char expChar, NumberFormatInfo nfi)
        {
            int di = 0;
            sb.Append(g.N > 0 ? (char)g.D[di++] : '0');
            if (digits != 1) sb.Append(nfi.NumberDecimalSeparator);
            while (--digits > 0) sb.Append(di < g.N ? (char)g.D[di++] : '0');
            FormatExponent(ref sb, g.N == 0 ? 0 : g.Exp10 - 1, expChar, 3, true, nfi);
        }

        /// <summary>.NET's FormatFixed: integer digits (grouped by groupDigits with sGroup, if given), then decimals.</summary>
        private static void FormatFixed(ref CharBuilder sb, ref DigitBuffer g, int decimals, int[] groupDigits, string sDecimal, string sGroup)
        {
            int digPos = g.Exp10;
            int di = 0;
            if (digPos > 0)
            {
                if (groupDigits != null)
                {
                    // Written right to left into a scratch buffer; group sizes repeat the last entry and a
                    // 0 entry ends grouping (a single 0 entry: no grouping).
                    int digStart = digPos < g.N ? digPos : g.N;
                    int capacity = digPos * (1 + sGroup.Length);
                    Span<char> buffer = capacity <= 256 ? stackalloc char[256] : new char[capacity];
                    int p = buffer.Length;
                    int groupSizeIndex = 0;
                    int groupSize = groupDigits.Length != 0 ? groupDigits[0] : 0;
                    int digitCount = 0;
                    for (int i = digPos - 1; i >= 0; i--)
                    {
                        buffer[--p] = i < digStart ? (char)g.D[i] : '0';
                        if (groupSize > 0)
                        {
                            digitCount++;
                            if (digitCount == groupSize && i != 0)
                            {
                                for (int j = sGroup.Length - 1; j >= 0; j--) buffer[--p] = sGroup[j];
                                if (groupSizeIndex < groupDigits.Length - 1)
                                {
                                    groupSizeIndex++;
                                    groupSize = groupDigits[groupSizeIndex];
                                }
                                digitCount = 0;
                            }
                        }
                    }
                    for (int k = p; k < buffer.Length; k++) sb.Append(buffer[k]);
                    di = digStart;
                }
                else
                {
                    do { sb.Append(di < g.N ? (char)g.D[di++] : '0'); } while (--digPos > 0);
                }
                digPos = 0;
            }
            else sb.Append('0');
            if (decimals > 0)
            {
                sb.Append(sDecimal);
                if (digPos < 0)
                {
                    int zeros = -digPos < decimals ? -digPos : decimals;
                    sb.Append('0', zeros);
                    decimals -= zeros;
                }
                while (decimals > 0) { sb.Append(di < g.N ? (char)g.D[di++] : '0'); decimals--; }
            }
        }

        private static void FormatCurrency(ref CharBuilder sb, ref DigitBuffer g, int decimals, NumberFormatInfo nfi)
        {
            string fmt = CurrencyFormat(g.Neg, g.Neg ? nfi.CurrencyNegativePattern : nfi.CurrencyPositivePattern);
            for (int i = 0; i < fmt.Length; i++)
            {
                switch (fmt[i])
                {
                    case '#': FormatFixed(ref sb, ref g, decimals, nfi._currencyGroupSizes, nfi.CurrencyDecimalSeparator, nfi.CurrencyGroupSeparator); break;
                    case '-': sb.Append(nfi.NegativeSign); break;
                    case '$': sb.Append(nfi.CurrencySymbol); break;
                    default: sb.Append(fmt[i]); break;
                }
            }
        }

        private static void FormatNumber(ref CharBuilder sb, ref DigitBuffer g, int decimals, NumberFormatInfo nfi)
        {
            string fmt = g.Neg ? NumberNegativeFormat(nfi.NumberNegativePattern) : "#";
            for (int i = 0; i < fmt.Length; i++)
            {
                switch (fmt[i])
                {
                    case '#': FormatFixed(ref sb, ref g, decimals, nfi._numberGroupSizes, nfi.NumberDecimalSeparator, nfi.NumberGroupSeparator); break;
                    case '-': sb.Append(nfi.NegativeSign); break;
                    default: sb.Append(fmt[i]); break;
                }
            }
        }

        private static void FormatPercent(ref CharBuilder sb, ref DigitBuffer g, int decimals, NumberFormatInfo nfi)
        {
            string fmt = PercentFormat(g.Neg, g.Neg ? nfi.PercentNegativePattern : nfi.PercentPositivePattern);
            for (int i = 0; i < fmt.Length; i++)
            {
                switch (fmt[i])
                {
                    case '#': FormatFixed(ref sb, ref g, decimals, nfi._percentGroupSizes, nfi.PercentDecimalSeparator, nfi.PercentGroupSeparator); break;
                    case '-': sb.Append(nfi.NegativeSign); break;
                    case '%': sb.Append(nfi.PercentSymbol); break;
                    default: sb.Append(fmt[i]); break;
                }
            }
        }

        /// <summary>Standard numeric format over prepared digits (.NET's NumberToString).</summary>
        private static void FormatStandard(ref CharBuilder sb, ref DigitBuffer g, char fmt, int precision, bool exact, NumberFormatInfo nfi)
        {
            // exact: digits are a double's exact expansion (round ties to even); else round half up.
            switch (fmt)
            {
                case 'C': case 'c':
                    if (precision < 0) precision = nfi.CurrencyDecimalDigits;
                    Round(ref g, g.Exp10 + precision, exact);
                    FormatCurrency(ref sb, ref g, precision, nfi);
                    return;
                case 'F': case 'f':
                    if (precision < 0) precision = nfi.NumberDecimalDigits;
                    Round(ref g, g.Exp10 + precision, exact);
                    if (g.Neg) sb.Append(nfi.NegativeSign);
                    FormatFixed(ref sb, ref g, precision, null, nfi.NumberDecimalSeparator, null);
                    return;
                case 'N': case 'n':
                    if (precision < 0) precision = nfi.NumberDecimalDigits;
                    Round(ref g, g.Exp10 + precision, exact);
                    FormatNumber(ref sb, ref g, precision, nfi);
                    return;
                case 'E': case 'e':
                    if (precision < 0) precision = 6;
                    precision++;
                    Round(ref g, precision, exact);
                    if (g.Neg) sb.Append(nfi.NegativeSign);
                    FormatScientific(ref sb, ref g, precision, fmt, nfi);
                    return;
                case 'G': case 'g':
                    if (precision < 1) precision = g.N;
                    Round(ref g, precision, exact);
                    if (g.Neg) sb.Append(nfi.NegativeSign);
                    FormatGeneral(ref sb, ref g, precision, fmt == 'G' ? 'E' : 'e', false, nfi);
                    return;
                case 'P': case 'p':
                    if (precision < 0) precision = nfi.PercentDecimalDigits;
                    if (g.N > 0) g.Exp10 += 2;
                    Round(ref g, g.Exp10 + precision, exact);
                    FormatPercent(ref sb, ref g, precision, nfi);
                    return;
                default:
                    ThrowBadFormat();
                    return;
            }
        }

        /// <summary>Splits "X8" into ('X', 8); returns '\0' for a custom format. Precision -1 = none.</summary>
        private static char ParseFormatSpecifier(string format, out int precision)
        {
            precision = -1;
            if (format == null || format.Length == 0) return 'G';
            char c = format[0];
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
            {
                if (format.Length == 1) return c;
                int p = 0;
                for (int i = 1; i < format.Length; i++)
                {
                    char d = format[i];
                    if (d < '0' || d > '9') return '\0';
                    p = p * 10 + (d - '0');
                    if (p > 999999999) ThrowBadFormat();
                }
                precision = p;
                return c;
            }
            return '\0';
        }

        // ==========================================================================================
        // Entry points (CoreLibRuntime redirects the former csrt_format_* calls here). The provider-less
        // forms use the current culture; *With forms take the IFormatProvider of ToString(format, provider).
        // ==========================================================================================

        internal static string FormatDouble(double value, string format, bool single) => FormatDoubleWith(value, format, single, null);

        internal static string FormatDoubleWith(double value, string format, bool single, IFormatProvider provider)
        {
            NumberFormatInfo nfi = NumberFormatInfo.GetInstance(provider);
            char fmt = ParseFormatSpecifier(format, out int precision);
            Span<byte> storage = stackalloc byte[DigitStorage];
            var g = new DigitBuffer(storage);
            Span<char> chars = stackalloc char[64];
            var sb = new CharBuilder(chars);
            if ((fmt == 'R' || fmt == 'r' || ((fmt == 'G' || fmt == 'g') && precision <= 0)))
            {
                bool done = single ? ShortestSingle((float)value, ref g, ref sb, nfi) : ShortestDouble(value, ref g, ref sb, nfi);
                if (!done) FormatGeneral(ref sb, ref g, single ? 9 : 17, fmt == 'g' || fmt == 'r' ? 'e' : 'E', false, nfi);
                return sb.Build();
            }
            if (value != value) return nfi.NaNSymbol;
            if (value == double.PositiveInfinity) return nfi.PositiveInfinitySymbol;
            if (value == double.NegativeInfinity) return nfi.NegativeInfinitySymbol;
            g.IsFloat = true;
            g.Neg = CoreIntrinsics.DoubleToBits(value) < 0;
            if (value != 0)
            {
                Decompose(value, out ulong f, out int e, out bool _);
                ExactDigits(f, e, ref g);
            }
            if (fmt == '\0')
            {
                Round(ref g, single ? SinglePrecisionCustomFormat : DoublePrecisionCustomFormat, true);
                FormatCustom(ref sb, ref g, format, nfi);
                return sb.Build();
            }
            if (fmt == 'X' || fmt == 'x')
            {
                FormatHexFloat(ref sb, value, single, precision, fmt == 'X');
                return sb.Build();
            }
            if (fmt == 'D' || fmt == 'd' || fmt == 'B' || fmt == 'b') ThrowBadFormat();
            FormatStandard(ref sb, ref g, fmt, precision, true, nfi);
            return sb.Build();
        }

        /// <summary>"X" on double/float (.NET 11): normalized hexadecimal significand, "0X1.8P+0".</summary>
        private static void FormatHexFloat(ref CharBuilder sb, double value, bool single, int precision, bool upper)
        {
            ulong f;
            int e, fractionBits, hexDigits;
            if (single) { Decompose((float)value, out f, out e, out bool _); fractionBits = 23; hexDigits = 6; }
            else { Decompose(value, out f, out e, out bool _); fractionBits = 52; hexDigits = 13; }
            if (CoreIntrinsics.DoubleToBits(value) < 0) sb.Append('-');
            sb.Append('0');
            sb.Append(upper ? 'X' : 'x');
            int exponent = 0;
            ulong fraction = 0;  // hexDigits * 4 bits after the point
            ulong lead = 0;
            if (f != 0)
            {
                while ((f >> fractionBits) == 0) { f <<= 1; e--; }  // normalize subnormals
                lead = 1;
                exponent = e + fractionBits;
                fraction = (f & ((1UL << fractionBits) - 1)) << (hexDigits * 4 - fractionBits);
            }
            int shown = hexDigits;
            if (precision < 0)
            {
                while (shown > 0 && ((fraction >> ((hexDigits - shown) * 4)) & 0xF) == 0) shown--;
            }
            else if (precision < hexDigits)
            {
                // Round to `precision` hex digits, ties to even; a carry into the leading digit makes it 2.
                int dropBits = (hexDigits - precision) * 4;
                ulong kept = ((lead << (hexDigits * 4)) | fraction) >> dropBits;
                ulong rest = fraction & ((1UL << dropBits) - 1);
                ulong half = 1UL << (dropBits - 1);
                if (rest > half || (rest == half && (kept & 1) == 1)) kept++;
                lead = kept >> (precision * 4);
                fraction = (kept & ((1UL << (precision * 4)) - 1)) << dropBits;
                shown = precision;
            }
            char a = upper ? 'A' : 'a';
            sb.Append((char)('0' + (int)lead));
            int total = precision > hexDigits ? precision : shown;
            if (total > 0) sb.Append('.');
            for (int i = 0; i < total; i++)
            {
                int d = i < hexDigits ? (int)((fraction >> ((hexDigits - 1 - i) * 4)) & 0xF) : 0;
                sb.Append(d < 10 ? (char)('0' + d) : (char)(a + d - 10));
            }
            sb.Append(upper ? 'P' : 'p');
            sb.Append(exponent < 0 ? '-' : '+');
            int magnitude = exponent < 0 ? -exponent : exponent;
            AppendDigits(ref sb, (ulong)magnitude, CountDigits((ulong)magnitude));
        }

        internal static string FormatInt64(long value, string format, int bits) => FormatInt64With(value, format, bits, null);

        internal static string FormatInt64With(long value, string format, int bits, IFormatProvider provider)
        {
            ulong magnitude = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;
            return FormatInteger(magnitude, value < 0, (ulong)value, format, bits, provider);
        }

        internal static string FormatUInt64(ulong value, string format, int bits) => FormatInteger(value, false, value, format, bits, null);

        internal static string FormatUInt64With(ulong value, string format, int bits, IFormatProvider provider) => FormatInteger(value, false, value, format, bits, provider);

        private static string FormatInteger(ulong magnitude, bool negative, ulong raw, string format, int bits, IFormatProvider provider)
        {
            char fmt = ParseFormatSpecifier(format, out int precision);
            if ((fmt == 'G' || fmt == 'g' || fmt == 'R' || fmt == 'r') && precision < 1)
                return negative ? NegativeInt64ToDecStr(-(long)(magnitude - 1) - 1, NumberFormatInfo.GetInstance(provider).NegativeSign) : UInt64ToDecStr(magnitude);
            if (bits < 64) raw &= (1UL << bits) - 1;
            Span<char> chars = stackalloc char[72];
            var sb = new CharBuilder(chars);
            switch (fmt)
            {
                case 'D': case 'd':
                {
                    int digits = CountDigits(magnitude);
                    if (negative) sb.Append(NumberFormatInfo.GetInstance(provider).NegativeSign);
                    sb.Append('0', precision - digits);
                    AppendDigits(ref sb, magnitude, digits);
                    return sb.Build();
                }
                case 'X': case 'x':
                {
                    int digits = 1;
                    for (ulong t = raw >> 4; t != 0; t >>= 4) digits++;
                    sb.Append('0', precision - digits);
                    char a = fmt == 'X' ? 'A' : 'a';
                    for (int i = digits - 1; i >= 0; i--)
                    {
                        int d = (int)((raw >> (i * 4)) & 0xF);
                        sb.Append(d < 10 ? (char)('0' + d) : (char)(a + d - 10));
                    }
                    return sb.Build();
                }
                case 'B': case 'b':
                {
                    int digits = 1;
                    for (ulong t = raw >> 1; t != 0; t >>= 1) digits++;
                    sb.Append('0', precision - digits);
                    for (int i = digits - 1; i >= 0; i--) sb.Append((raw >> i & 1) != 0 ? '1' : '0');
                    return sb.Build();
                }
            }
            NumberFormatInfo nfi = NumberFormatInfo.GetInstance(provider);
            Span<byte> storage = stackalloc byte[24];
            var g = new DigitBuffer(storage);
            g.Neg = negative;
            IntegerDigits(magnitude, ref g);
            if (fmt == '\0') FormatCustom(ref sb, ref g, format, nfi);
            // Integers format "R<n>" as "G<n>" (case preserved: "r5" gives a lowercase exponent).
            else if (fmt == 'R' || fmt == 'r') FormatStandard(ref sb, ref g, (char)(fmt - ('R' - 'G')), precision, false, nfi);
            else FormatStandard(ref sb, ref g, fmt, precision, false, nfi);
            return sb.Build();
        }

        /// <summary>A standard format over an integer given as its decimal digits (BigInteger).</summary>
        internal static string FormatIntegerDigits(string digits, bool negative, char fmt, int precision, NumberFormatInfo nfi)
        {
            byte[] storage = new byte[digits.Length + 2];
            var g = new DigitBuffer(storage);
            g.Neg = negative;
            int n = digits.Length;
            for (int i = 0; i < n; i++) storage[i] = (byte)digits[i];
            g.Exp10 = n;
            while (n > 0 && storage[n - 1] == (byte)'0') n--;
            g.N = n;
            if (n == 0) { g.Exp10 = 0; g.Neg = false; }
            var sb = new CharBuilder(new char[digits.Length * 2 + 32]);
            FormatStandard(ref sb, ref g, fmt, precision, false, nfi);
            return sb.Build();
        }

        private static void AppendDigits(ref CharBuilder sb, ulong value, int digits)
        {
            Span<char> tmp = stackalloc char[20];
            WriteDigits(tmp.Slice(0, digits), value);
            for (int i = 0; i < digits; i++) sb.Append(tmp[i]);
        }

        internal static string FormatDecimal(decimal value, string format) => FormatDecimalWith(value, format, null);

        /// <summary>decimal.ToString(format, provider): exact digits (trailing zeros kept for "G"), half-up rounding.</summary>
        internal static string FormatDecimalWith(decimal value, string format, IFormatProvider provider)
        {
            NumberFormatInfo nfi = NumberFormatInfo.GetInstance(provider);
            int flags = CoreIntrinsics.DecimalFlags(value);
            uint hi = CoreIntrinsics.DecimalHi(value);
            ulong lo = CoreIntrinsics.DecimalLo(value);
            int scale = (flags >> 16) & 0xFF;
            Span<byte> storage = stackalloc byte[32];
            var g = new DigitBuffer(storage);
            g.Neg = flags < 0;
            // 96-bit integer digits via base-10^9 chunks.
            Span<uint> words = stackalloc uint[4];
            var n = new BigNum(words);
            n.W[0] = (uint)lo; n.W[1] = (uint)(lo >> 32); n.W[2] = hi; n.Len = 3;
            n.Normalize();
            int len = 0;
            if (!n.IsZero)
            {
                Span<uint> chunks = stackalloc uint[4];
                int count = 0;
                while (!n.IsZero) chunks[count++] = n.DivRemSmall(1000000000);
                len = WriteChunk(g.D, 0, chunks[count - 1], false);
                for (int i = count - 2; i >= 0; i--) len = WriteChunk(g.D, len, chunks[i], true);
            }
            g.N = len;
            g.Exp10 = len - scale;

            char fmt = ParseFormatSpecifier(format, out int precision);
            Span<char> chars = stackalloc char[64];
            var sb = new CharBuilder(chars);
            if (fmt == '\0')
            {
                TrimTrailingZeros(ref g);
                FormatCustom(ref sb, ref g, format, nfi);
                return sb.Build();
            }
            // "R<n>" is "G<n>" for decimal (case preserved for the exponent character).
            if (fmt == 'R' || fmt == 'r') fmt = (char)(fmt - ('R' - 'G'));
            if ((fmt == 'G' || fmt == 'g') && precision < 0)
            {
                // ECMA: all digits, trailing zeros included, never scientific; no "-0".
                if (g.N > 0 && g.Neg) sb.Append(nfi.NegativeSign);
                FormatGeneral(ref sb, ref g, 0, 'E', true, nfi);
                return sb.Build();
            }
            if (fmt == 'D' || fmt == 'd' || fmt == 'X' || fmt == 'x' || fmt == 'B' || fmt == 'b') ThrowBadFormat();
            // "G0" means "as many digits as the decimal has", trailing zeros counted (1e20m.ToString("G0")
            // stays fixed-point because its 21 digits exceed the exponent) but not printed.
            if ((fmt == 'G' || fmt == 'g') && precision == 0) precision = g.N > 0 ? g.N : 1;
            TrimTrailingZeros(ref g);
            FormatStandard(ref sb, ref g, fmt, precision, false, nfi);
            return sb.Build();
        }

        private static void TrimTrailingZeros(ref DigitBuffer g)
        {
            while (g.N > 0 && g.D[g.N - 1] == (byte)'0') g.N--;
            if (g.N == 0) { g.Exp10 = 0; g.Neg = false; }
        }

        internal static string DecimalToString(decimal value) => FormatDecimalWith(value, null, null);

        /// <summary>Boxed decimal's ToString (the runtime passes the payload's address).</summary>
        [RuntimeExport("csrt_decimal_to_string", "csrt_Boxed_Decimal_MT")]
        internal static string DecimalToStringAt(ref decimal value) => FormatDecimalWith(value, null, null);

        // ==========================================================================================
        // Custom format strings: .NET's NumberToStringFormat (Number.Formatting.Common.cs, see above)
        // ==========================================================================================

        private static int FindSection(string format, int section)
        {
            if (section == 0) return 0;
            int src = 0;
            while (true)
            {
                if (src >= format.Length) return 0;
                char ch = format[src++];
                switch (ch)
                {
                    case '\'':
                    case '"':
                        while (src < format.Length && format[src++] != ch) { }
                        break;
                    case '\\':
                        if (src < format.Length) src++;
                        break;
                    case ';':
                        if (--section != 0) break;
                        if (src < format.Length && format[src] != ';') return src;
                        return 0;
                }
            }
        }

        private static bool HasNegativeSection(string format) => FindSection(format, 1) != 0;

        private static void FormatCustom(ref CharBuilder sb, ref DigitBuffer g, string format, NumberFormatInfo nfi)
        {
            int digitCount, decimalPos, firstDigit, lastDigit;
            bool scientific;
            int thousandPos, thousandCount = 0;
            bool thousandSeps;
            int scaleAdjust;
            int src;
            int section = FindSection(format, g.N == 0 ? 2 : g.Neg ? 1 : 0);
            while (true)
            {
                digitCount = 0; decimalPos = -1; firstDigit = 0x7FFFFFFF; lastDigit = 0; scientific = false;
                thousandPos = -1; thousandSeps = false; scaleAdjust = 0;
                src = section;
                while (src < format.Length)
                {
                    char ch = format[src++];
                    if (ch == ';') break;
                    switch (ch)
                    {
                        case '#': digitCount++; break;
                        case '0':
                            if (firstDigit == 0x7FFFFFFF) firstDigit = digitCount;
                            digitCount++;
                            lastDigit = digitCount;
                            break;
                        case '.':
                            if (decimalPos < 0) decimalPos = digitCount;
                            break;
                        case ',':
                            if (digitCount > 0 && decimalPos < 0)
                            {
                                if (thousandPos >= 0)
                                {
                                    if (thousandPos == digitCount) { thousandCount++; break; }
                                    thousandSeps = true;
                                }
                                thousandPos = digitCount;
                                thousandCount = 1;
                            }
                            break;
                        case '%': scaleAdjust += 2; break;
                        case '\x2030': scaleAdjust += 3; break;
                        case '\'':
                        case '"':
                            while (src < format.Length && format[src++] != ch) { }
                            break;
                        case '\\':
                            if (src < format.Length) src++;
                            break;
                        case 'E':
                        case 'e':
                            if ((src < format.Length && format[src] == '0') ||
                                (src + 1 < format.Length && (format[src] == '+' || format[src] == '-') && format[src + 1] == '0'))
                            {
                                while (++src < format.Length && format[src] == '0') { }
                                scientific = true;
                            }
                            break;
                    }
                }
                if (decimalPos < 0) decimalPos = digitCount;
                if (thousandPos >= 0)
                {
                    if (thousandPos == decimalPos) scaleAdjust -= thousandCount * 3;
                    else thousandSeps = true;
                }
                if (g.N != 0)
                {
                    g.Exp10 += scaleAdjust;
                    int pos = scientific ? digitCount : g.Exp10 + digitCount - decimalPos;
                    Round(ref g, pos, false);
                    if (g.N == 0)
                    {
                        int zero = FindSection(format, 2);
                        if (zero != section) { section = zero; continue; }
                    }
                }
                else
                {
                    if (!g.IsFloat) g.Neg = false;
                    g.Exp10 = 0;
                }
                break;
            }

            firstDigit = firstDigit < decimalPos ? decimalPos - firstDigit : 0;
            lastDigit = lastDigit > decimalPos ? decimalPos - lastDigit : 0;
            int digPos, adjust;
            if (scientific) { digPos = decimalPos; adjust = 0; }
            else
            {
                digPos = g.Exp10 > decimalPos ? g.Exp10 : decimalPos;
                adjust = g.Exp10 - decimalPos;
            }
            src = section;

            // Where the group separators go (digit positions), from the culture's NumberGroupSizes.
            Span<int> thousandsSepPos = stackalloc int[4];
            int thousandsSepCtr = -1;
            if (thousandSeps && nfi.NumberGroupSeparator.Length > 0)
            {
                int[] groupDigits = nfi._numberGroupSizes;
                int groupSizeIndex = 0;
                int groupTotalSizeCount = 0;
                int groupSizeLen = groupDigits.Length;
                if (groupSizeLen != 0) groupTotalSizeCount = groupDigits[groupSizeIndex];
                int groupSize = groupTotalSizeCount;
                int totalDigits = digPos + (adjust < 0 ? adjust : 0);
                int numDigits = firstDigit > totalDigits ? firstDigit : totalDigits;
                while (numDigits > groupTotalSizeCount)
                {
                    if (groupSize == 0) break;
                    ++thousandsSepCtr;
                    if (thousandsSepCtr >= thousandsSepPos.Length)
                    {
                        int[] grown = new int[thousandsSepPos.Length * 2];
                        thousandsSepPos.CopyTo(grown);
                        thousandsSepPos = grown;
                    }
                    thousandsSepPos[thousandsSepCtr] = groupTotalSizeCount;
                    if (groupSizeIndex < groupSizeLen - 1)
                    {
                        groupSizeIndex++;
                        groupSize = groupDigits[groupSizeIndex];
                    }
                    groupTotalSizeCount += groupSize;
                }
            }

            int start = sb.Length;
            // A dedicated negative section emits the sign itself; a negative value that rounded to zero
            // and fell back to the first section gets no extra sign then (.NET 11).
            if (g.Neg && section == 0 && g.Exp10 != 0 && !HasNegativeSection(format)) sb.Append(nfi.NegativeSign);
            bool decimalWritten = false;
            int di = 0;
            while (src < format.Length)
            {
                char ch = format[src++];
                if (ch == ';') break;
                if (adjust > 0 && (ch == '#' || ch == '0' || ch == '.'))
                {
                    while (adjust > 0)
                    {
                        sb.Append(di < g.N ? (char)g.D[di++] : '0');
                        if (thousandSeps && digPos > 1 && thousandsSepCtr >= 0 && digPos == thousandsSepPos[thousandsSepCtr] + 1)
                        {
                            sb.Append(nfi.NumberGroupSeparator);
                            thousandsSepCtr--;
                        }
                        digPos--;
                        adjust--;
                    }
                }
                switch (ch)
                {
                    case '#':
                    case '0':
                    {
                        char c;
                        if (adjust < 0)
                        {
                            adjust++;
                            c = digPos <= firstDigit ? '0' : '\0';
                        }
                        else
                        {
                            c = di < g.N ? (char)g.D[di++] : digPos > lastDigit ? '0' : '\0';
                        }
                        if (c != '\0')
                        {
                            sb.Append(c);
                            if (thousandSeps && digPos > 1 && thousandsSepCtr >= 0 && digPos == thousandsSepPos[thousandsSepCtr] + 1)
                            {
                                sb.Append(nfi.NumberGroupSeparator);
                                thousandsSepCtr--;
                            }
                        }
                        digPos--;
                        break;
                    }
                    case '.':
                        if (digPos != 0 || decimalWritten) break;
                        if (lastDigit < 0 || (decimalPos < digitCount && di < g.N))
                        {
                            sb.Append(nfi.NumberDecimalSeparator);
                            decimalWritten = true;
                        }
                        break;
                    case '\x2030': sb.Append(nfi.PerMilleSymbol); break;
                    case '%': sb.Append(nfi.PercentSymbol); break;
                    case ',': break;
                    case '\'':
                    case '"':
                        while (src < format.Length && format[src] != ch) sb.Append(format[src++]);
                        if (src < format.Length) src++;
                        break;
                    case '\\':
                        if (src < format.Length) sb.Append(format[src++]);
                        break;
                    case 'E':
                    case 'e':
                    {
                        bool positiveSign = false;
                        int i = 0;
                        if (scientific)
                        {
                            if (src < format.Length && format[src] == '0') i++;
                            else if (src + 1 < format.Length && format[src] == '+' && format[src + 1] == '0') positiveSign = true;
                            else if (src + 1 < format.Length && format[src] == '-' && format[src + 1] == '0') { }
                            else { sb.Append(ch); break; }
                            while (++src < format.Length && format[src] == '0') i++;
                            if (i > 10) i = 10;
                            int exp = g.N == 0 ? 0 : g.Exp10 - decimalPos;
                            FormatExponent(ref sb, exp, ch, i, positiveSign, nfi);
                            scientific = false;
                        }
                        else
                        {
                            sb.Append(ch);
                            if (src < format.Length)
                            {
                                if (format[src] == '+' || format[src] == '-') sb.Append(format[src++]);
                                while (src < format.Length && format[src] == '0') sb.Append(format[src++]);
                            }
                        }
                        break;
                    }
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            if (g.Neg && section == 0 && g.Exp10 == 0 && sb.Length > start && !HasNegativeSection(format)) sb.Insert(start, nfi.NegativeSign);
        }

        // ==========================================================================================
        // Composite formatting, alignment, objects
        // ==========================================================================================

        /// <summary>Pads to |width| (positive: right-align, negative: left-align), like {x,width}.</summary>
        internal static string Align(string s, int width)
        {
            int len = s == null ? 0 : s.Length;
            int w = width < 0 ? -width : width;
            if (w <= len) return s ?? "";
            string r = CoreIntrinsics.FastAllocateString(w);
            Span<char> dst = CoreIntrinsics.StringChars(r);
            int pad = w - len;
            int at = width < 0 ? 0 : pad;
            for (int i = 0; i < w; i++) dst[i] = ' ';
            for (int i = 0; i < len; i++) dst[at + i] = s[i];
            return r;
        }

        // IFormattable.ToString(format, provider) of a boxed primitive (ModuleCodeGen.Primitives.cs), one per
        // kind of number: as FormatObjectWith, without reaching the other types' formatters.
        internal static string FormatBoxedSigned(long value, string format, int bits, IFormatProvider provider) =>
            FormatInt64With(value, format != null && format.Length == 0 ? null : format, bits, provider);

        internal static string FormatBoxedUnsigned(ulong value, string format, int bits, IFormatProvider provider) =>
            FormatUInt64With(value, format != null && format.Length == 0 ? null : format, bits, provider);

        internal static string FormatBoxedFloat(double value, string format, bool single, IFormatProvider provider) =>
            FormatDoubleWith(value, format != null && format.Length == 0 ? null : format, single, provider);

        internal static string FormatBoxedDecimal(decimal value, string format, IFormatProvider provider) =>
            FormatDecimalWith(value, format != null && format.Length == 0 ? null : format, provider);

        /// <summary>Formats any object with an optional format string (boxed numbers honour it).</summary>
        internal static string FormatObject(object value, string format) => FormatObjectWith(value, format, null);

        /// <summary>
        /// A composite-format argument: IFormattable values format with (format, provider) — numbers and
        /// dates through the corelib directly — anything else with ToString().
        /// </summary>
        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_format_boxed_ticks")]
        private static extern IntPtr FormatBoxedTicks(IntPtr value, IntPtr format, IntPtr provider);

        internal static string FormatObjectWith(object value, string format, IFormatProvider provider)
        {
            if (value == null) return "";
            if (format != null && format.Length == 0) format = null;
            if (value is int i32) return FormatInt64With(i32, format, 32, provider);
            if (value is long i64) return FormatInt64With(i64, format, 64, provider);
            if (value is double f64) return FormatDoubleWith(f64, format, false, provider);
            if (value is float f32) return FormatDoubleWith(f32, format, true, provider);
            if (value is decimal dec) return FormatDecimalWith(dec, format, provider);
            if (value is uint u32) return FormatUInt64With(u32, format, 32, provider);
            if (value is ulong u64) return FormatUInt64With(u64, format, 64, provider);
            if (value is short i16) return FormatInt64With(i16, format, 16, provider);
            if (value is ushort u16) return FormatUInt64With(u16, format, 16, provider);
            if (value is byte u8) return FormatUInt64With(u8, format, 8, provider);
            if (value is sbyte i8) return FormatInt64With(i8, format, 8, provider);
            // DateTime / TimeSpan through the runtime (csrt_format_boxed_ticks), so the date formatter is only
            // compiled into programs that box one.
            IntPtr ticksText = FormatBoxedTicks(CoreIntrinsics.ObjectToPointer(value), CoreIntrinsics.ObjectToPointer(format), CoreIntrinsics.ObjectToPointer(provider));
            if (ticksText != IntPtr.Zero) return (string)CoreIntrinsics.PointerToObject(ticksText);
            // char is IFormattable too (ignoring format and provider); its interface thunk lands here.
            if (value is char ch) return ch.ToString();
            if (value is IFormattable formattable) return formattable.ToString(format, provider) ?? "";
            return value.ToString() ?? "";
        }

        // .NET's ThrowHelper.ThrowFormatInvalidString(offset, reason).
        private static void ThrowInvalidComposite(int offset, string reason) =>
            throw new FormatException("Input string was not in a correct format. Failure to parse near offset " +
                Int32ToDecStr(offset) + ". " + reason);

        private const string UnclosedFormatItem = "Format item ends prematurely.";
        private const string ExpectedAsciiDigit = "Expected an ASCII digit.";

        // Advances to the next character of a format item; running off the end is an unclosed item.
        private static char MoveNext(string format, ref int pos)
        {
            pos++;
            if (pos >= format.Length) ThrowInvalidComposite(pos, UnclosedFormatItem);
            return format[pos];
        }

        /// <summary>
        /// string.Format(format, args): "{index[,alignment][:format]}" with "{{" / "}}" escapes. Mirrors
        /// .NET's ValueStringBuilder.AppendFormatHelper step by step, so a malformed format reports the
        /// same offset and reason, and argument formatting errors surface in the same order.
        /// </summary>
        internal static string FormatComposite(string format, object[] args) => FormatCompositeWith(null, format, args);

        /// <summary>string.Format(provider, format, args); an ICustomFormatter from the provider formats each argument first.</summary>
        internal static string FormatCompositeWith(IFormatProvider provider, string format, object[] args)
        {
            ICustomFormatter customFormatter = provider == null ? null : provider.GetFormat(typeof(ICustomFormatter)) as ICustomFormatter;
            if (format == null) throw new ArgumentNullException("format");
            const int IndexLimit = 1000000, WidthLimit = 1000000;  // exclusive limits, as in .NET
            int count = args == null ? 0 : args.Length;
            Span<char> chars = stackalloc char[256];
            var sb = new CharBuilder(chars);
            int pos = 0;
            char ch;
            while (true)
            {
                // Literal text up to the next unescaped '{'.
                while (true)
                {
                    if (pos >= format.Length) return sb.Build();
                    char c = format[pos];
                    if (c != '{' && c != '}') { sb.Append(c); pos++; continue; }
                    ch = MoveNext(format, ref pos);
                    if (c == ch) { sb.Append(ch); pos++; continue; }
                    if (c != '{') ThrowInvalidComposite(pos, "Unexpected closing brace without a corresponding opening brace.");
                    break;
                }

                int width = 0;
                bool leftJustify = false;
                string spec = null;
                int index = ch - '0';
                if ((uint)index >= 10u) ThrowInvalidComposite(pos, ExpectedAsciiDigit);
                ch = MoveNext(format, ref pos);
                if (ch != '}')
                {
                    while (ch >= '0' && ch <= '9' && index < IndexLimit)
                    {
                        index = index * 10 + ch - '0';
                        ch = MoveNext(format, ref pos);
                    }
                    while (ch == ' ') ch = MoveNext(format, ref pos);
                    if (ch == ',')
                    {
                        do { ch = MoveNext(format, ref pos); } while (ch == ' ');
                        if (ch == '-')
                        {
                            leftJustify = true;
                            ch = MoveNext(format, ref pos);
                        }
                        width = ch - '0';
                        if ((uint)width >= 10u) ThrowInvalidComposite(pos, ExpectedAsciiDigit);
                        ch = MoveNext(format, ref pos);
                        while (ch >= '0' && ch <= '9' && width < WidthLimit)
                        {
                            width = width * 10 + ch - '0';
                            ch = MoveNext(format, ref pos);
                        }
                        while (ch == ' ') ch = MoveNext(format, ref pos);
                    }
                    if (ch != '}')
                    {
                        if (ch != ':') ThrowInvalidComposite(pos, UnclosedFormatItem);
                        int startingPos = pos;
                        while (true)
                        {
                            ch = MoveNext(format, ref pos);
                            if (ch == '}') break;
                            if (ch == '{') ThrowInvalidComposite(pos, UnclosedFormatItem);
                        }
                        startingPos++;
                        spec = format.Substring(startingPos, pos - startingPos);
                    }
                }
                pos++;
                if (index >= count)
                    throw new FormatException("Index (zero based) must be greater than or equal to zero and less than the size of the argument list.");
                string piece = null;
                if (customFormatter != null) piece = customFormatter.Format(spec, args[index], provider);
                if (piece == null) piece = FormatObjectWith(args[index], spec, provider);
                int len = piece.Length;
                if (!leftJustify && width > len) sb.Append(' ', width - len);
                sb.Append(piece);
                if (leftJustify && width > len) sb.Append(' ', width - len);
            }
        }

        /// <summary>Console.Write(double/float) (formerly csrt_console_write_f64/f32).</summary>
        internal static void ConsoleWriteDouble(double value) => Console.Write(DoubleToString(value));
        internal static void ConsoleWriteSingle(float value) => Console.Write(SingleToString(value));
    }
}
