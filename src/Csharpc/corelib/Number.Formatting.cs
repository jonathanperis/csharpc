// Number formatting in C# (formerly src/runtime/format.c and format_real in csrt.c): .NET's
// standard (C D E F G N P R X B) and custom ("#,##0.00", "0.0E+00", sections, quotes, %, ‰) numeric
// format strings for the invariant culture, composite formatting, and shortest round-trip
// double/float output. The structure follows .NET's Number.Formatting.cs: a value becomes a
// DigitBuffer (digits d1d2... and Exp10 with value = 0.d1d2... * 10^Exp10), which the formatters
// round and lay out.
//
//   * double/float digits are exact (BigNum expansion of the binary value) and rounded to nearest,
//     ties to even, like .NET Core 3.0+ (0.125.ToString("F2") == "0.12"); the shortest round-trip
//     digits ("R", "G", ToString()) are CoreLib's own Grisu3/Dragon4, from the framework's IL.
//   * custom formats see doubles rounded to 15 significant digits (floats: 7), then round half up.
//   * integers and decimals round half up, and never print a negative zero.
#nullable disable
using System;
using System.Runtime.CompilerServices;
using NumberFormatInfo = System.Globalization.NumberFormatInfo;

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

    }

    /// <summary>A growable char buffer that starts on the stack.</summary>
    internal ref struct CharBuilder
    {
        private Span<char> _chars;
        private int _pos;

        public CharBuilder(Span<char> initial) { _chars = initial; _pos = 0; }

        public int Length => _pos;

        public char LastChar => _chars[_pos - 1];


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



        /// <summary>The space after the text so far (string.Format's ISpanFormattable items are formatted into it).</summary>
        public Span<char> Remaining => _chars.Slice(_pos);

        public void Advance(int count) { _pos += count; }

        public void EnsureCapacity(int capacity)
        {
            if ((uint)capacity > (uint)_chars.Length) Grow(capacity - _pos);
        }

        // Grows as .NET's ValueStringBuilder does (twice the size, or what is needed if more, rented from ArrayPool<char>.Shared, whose
        // arrays up to 2^30 chars are powers of two of at least 16): string.Format offers an ISpanFormattable item the space left in the
        // buffer, and formats it with IFormattable when that is too small, so the capacity is observable.
        private void Grow(int extra)
        {
            int size = (int)Math.Max((uint)(_pos + extra), Math.Min((uint)_chars.Length * 2, (uint)Array.MaxLength));
            if (size <= 16) size = 16;
            else if (size <= 1 << 30) size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)size);
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
        internal static string DoubleToString(double value) => UpstreamNumber.FormatFloat(null, value, null, NumberFormatInfo.CurrentInfo);


        [RuntimeExport("csrt_string_from_f32", "csrt_Boxed_Single_MT")]
        internal static string SingleToString(float value) => UpstreamNumber.FormatFloat(null, value, null, NumberFormatInfo.CurrentInfo);




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
                    case '#': FormatFixed(ref sb, ref g, decimals, NumberFormatInfoInternals.CurrencyGroupSizes(nfi), nfi.CurrencyDecimalSeparator, nfi.CurrencyGroupSeparator); break;
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
                    case '#': FormatFixed(ref sb, ref g, decimals, NumberFormatInfoInternals.NumberGroupSizes(nfi), nfi.NumberDecimalSeparator, nfi.NumberGroupSeparator); break;
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
                    case '#': FormatFixed(ref sb, ref g, decimals, NumberFormatInfoInternals.PercentGroupSizes(nfi), nfi.PercentDecimalSeparator, nfi.PercentGroupSeparator); break;
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


        // ==========================================================================================
        // Entry points (CoreLibRuntime redirects the former csrt_format_* calls here). The provider-less
        // forms use the current culture; *With forms take the IFormatProvider of ToString(format, provider).
        // ==========================================================================================

        internal static string FormatDouble(double value, string format, bool single) => FormatDoubleWith(value, format, single, null);

        internal static string FormatDoubleWith(double value, string format, bool single, IFormatProvider provider) =>
            single ? UpstreamNumber.FormatFloat(null, (float)value, format, NumberFormatInfo.GetInstance(provider))
                : UpstreamNumber.FormatFloat(null, value, format, NumberFormatInfo.GetInstance(provider));



        internal static string FormatInt64(long value, string format, int bits) => FormatInt64With(value, format, bits, null);

        // The width chooses the primitive's own formatter (short's hex mask is 0xFFFF: (short)-1 is "FFFF" in "X").
        internal static string FormatInt64With(long value, string format, int bits, IFormatProvider provider) => bits switch
        {
            64 => UpstreamNumber.FormatInt64(null, value, format, provider),
            16 => UpstreamNumber.FormatInt32(null, (int)value, 0xFFFF, format, provider),
            8 => UpstreamNumber.FormatInt32(null, (int)value, 0xFF, format, provider),
            _ => UpstreamNumber.FormatInt32(null, (int)value, ~0, format, provider),
        };

        internal static string FormatUInt64(ulong value, string format, int bits) => FormatUInt64With(value, format, bits, null);

        internal static string FormatUInt64With(ulong value, string format, int bits, IFormatProvider provider) =>
            bits == 64 ? UpstreamNumber.FormatUInt64(null, value, format, provider) : UpstreamNumber.FormatUInt32(null, (uint)value, format, provider);


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


        internal static string FormatDecimal(decimal value, string format) => FormatDecimalWith(value, format, null);

        /// <summary>decimal.ToString(format, provider): exact digits (trailing zeros kept for "G"), half-up rounding.</summary>
        internal static string FormatDecimalWith(decimal value, string format, IFormatProvider provider) =>
            UpstreamNumber.FormatDecimal(null, value, format, NumberFormatInfo.GetInstance(provider));



        internal static string DecimalToString(decimal value) => UpstreamNumber.FormatDecimal(null, value, null, NumberFormatInfo.CurrentInfo);

        /// <summary>Boxed decimal's ToString (the runtime passes the payload's address).</summary>
        [RuntimeExport("csrt_decimal_to_string", "csrt_Boxed_Decimal_MT")]
        internal static string DecimalToStringAt(ref decimal value) => DecimalToString(value);

        // ==========================================================================================
        // Custom format strings: .NET's NumberToStringFormat (Number.Formatting.Common.cs, see above)
        // ==========================================================================================




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

        internal static string FormatObjectWith(object value, string format, IFormatProvider provider) => FormatObjectWith(value, format, provider, spanFormattableFirst: true);

        /// <summary>
        /// spanFormattableFirst: ISpanFormattable.TryFormat (into a buffer grown until it fits) before IFormattable, as the interpolation
        /// handlers format a value; string.Format's own rule is AppendCompositeItem's.
        /// </summary>
        internal static string FormatObjectWith(object value, string format, IFormatProvider provider, bool spanFormattableFirst)
        {
            if (value == null) return "";
            if (format != null && format.Length == 0) format = null;
            // Boxed numbers format through their IFormattable thunks below (FormatBoxed*), so only the formatters of types the program
            // boxes are linked: a type switch here linked the double, float and decimal formatters into every program that formats a
            // message (#426: CoreLib's SR.Format reaches this through string.Format).
            // DateTime / TimeSpan through the runtime (csrt_format_boxed_ticks), so the date formatter is only
            // compiled into programs that box one.
            IntPtr ticksText = FormatBoxedTicks(CoreIntrinsics.ObjectToPointer(value), CoreIntrinsics.ObjectToPointer(format), CoreIntrinsics.ObjectToPointer(provider));
            if (ticksText != IntPtr.Zero) return (string)CoreIntrinsics.PointerToObject(ticksText);
            // char is IFormattable too (ignoring format and provider); its interface thunk lands here.
            if (value is char ch) return ch.ToString();
            // .NET's interpolation handlers prefer ISpanFormattable.TryFormat over IFormattable.ToString, and a type may answer the two
            // differently. (A boxed built-in value's TryFormat thunk is its IFormattable text: BoxedSpanFormattable.TryFormatOf.)
            if (spanFormattableFirst && value is ISpanFormattable spanFormattable) return FormatSpanFormattable(spanFormattable, format, provider);
            if (value is IFormattable formattable) return formattable.ToString(format, provider) ?? "";
            return value.ToString() ?? "";
        }

        /// <summary>
        /// `{value}` of a reference type or an IFormattable struct in an interpolated string (FunctionCodeGen.InterpolationHole), as
        /// DefaultInterpolatedStringHandler.AppendFormatted&lt;T&gt; appends it: ISpanFormattable.TryFormat into a buffer grown until it fits, else
        /// IFormattable.ToString(null, null), else ToString(); null is "". (A boxed BigInteger is not IFormattable here; its ToString() is the same
        /// text as ToString(null, null).)
        /// </summary>
        internal static string FormatInterpolationHole(object value)
        {
            if (value is IFormattable formattable)
            {
                if (value is ISpanFormattable spanFormattable) return FormatSpanFormattable(spanFormattable, null, null);
                return formattable.ToString(null, null) ?? "";
            }
            return value?.ToString() ?? "";
        }

        /// <summary>
        /// ISpanFormattable.TryFormat as string.Format and the interpolation handlers call it (ValueStringBuilder.AppendFormatHelper): into the
        /// space at hand, and again into a larger buffer while it answers false.
        /// </summary>
        internal static string FormatSpanFormattable(ISpanFormattable value, string format, IFormatProvider provider)
        {
            Span<char> initial = stackalloc char[256];
            if (value.TryFormat(initial, out int written, format, provider)) return initial.Slice(0, written).ToString();
            for (int size = 1024; ; size = size < int.MaxValue / 2 ? size * 2 : Array.MaxLength)
            {
                char[] buffer = new char[size];
                if (value.TryFormat(buffer, out written, format, provider)) return new string(buffer, 0, written);
                if (size == Array.MaxLength) throw new OutOfMemoryException();
            }
        }

        // .NET's ThrowHelper.ThrowFormatInvalidString(offset, reason).
        internal static void ThrowInvalidComposite(int offset, string reason) =>
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
            if (format == null) throw new ArgumentNullException("format");
            ICustomFormatter customFormatter = provider == null ? null : provider.GetFormat(typeof(ICustomFormatter)) as ICustomFormatter;
            const int IndexLimit = 1000000, WidthLimit = 1000000;  // exclusive limits, as in .NET
            int count = args == null ? 0 : args.Length;
            // .NET's string.Format: a 256-char stack buffer, grown at once to the format's length plus 8 per argument.
            Span<char> chars = stackalloc char[256];
            var sb = new CharBuilder(chars);
            sb.EnsureCapacity(format.Length + count * 8);
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
                AppendCompositeItem(ref sb, customFormatter, provider, spec, args[index], width, leftJustify);
            }
        }

        /// <summary>
        /// One item of string.Format, as .NET's ValueStringBuilder.AppendFormatHelper formats it: the provider's ICustomFormatter first; then, when
        /// nothing goes before it (no right alignment), an ISpanFormattable into the space left in the buffer, once; and otherwise, or when that
        /// space is too small, IFormattable.ToString(format, provider) or ToString(). No type test on the built-in numbers here: testing a boxed
        /// type refers to its method table, which compiles that type's interface thunks (and so its formatter) into the program.
        /// </summary>
        internal static void AppendCompositeItem(ref CharBuilder sb, ICustomFormatter customFormatter, IFormatProvider provider, string spec, object arg, int width, bool leftJustify,
            bool lean = false)
        {
            string piece = null;
            if (customFormatter != null) piece = customFormatter.Format(spec, arg, provider);
            if (piece == null)
            {
                if ((leftJustify || width == 0) && arg is ISpanFormattable spanFormattable
                    && spanFormattable.TryFormat(sb.Remaining, out int charsWritten, spec, provider))
                {
                    if ((uint)charsWritten > (uint)sb.Remaining.Length) throw new FormatException("Input string was not in a correct format.");
                    sb.Advance(charsWritten);
                    if (leftJustify && width > charsWritten) sb.Append(' ', width - charsWritten);
                    return;
                }
                // lean (CoreLib's messages, UpstreamSupport.FormatItems): IFormattable or ToString() only, without FormatObjectWith's special
                // cases (BigInteger's box, the runtime's date formatting), which would link their formatters into every program that can throw
                // a KeyNotFoundException.
                piece = lean ? (arg is IFormattable formattable ? formattable.ToString(spec, provider) : arg?.ToString()) ?? ""
                    : FormatObjectWith(arg, spec, provider, spanFormattableFirst: false);
            }
            int len = piece.Length;
            if (!leftJustify && width > len) sb.Append(' ', width - len);
            sb.Append(piece);
            if (leftJustify && width > len) sb.Append(' ', width - len);
        }

        /// <summary>Console.Write(double/float) (formerly csrt_console_write_f64/f32).</summary>
        internal static void ConsoleWriteDouble(double value) => Console.Write(DoubleToString(value));
        internal static void ConsoleWriteSingle(float value) => Console.Write(SingleToString(value));
    }
}
