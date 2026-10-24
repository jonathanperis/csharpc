// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/Guid.cs
// (+ Guid.Unix.cs); see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md):
// UTF-16 paths only (UTF-8 formatting goes through the UTF-16 one), scalar formatting instead of the
// Vector128 one, and the parse result kept in a plain struct instead of being reinterpreted as a Guid.
// Values passed in as the real `Guid` are read through a pointer cast (same layout, no boxing).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Guid")]
    public readonly unsafe struct GuidImpl : ISpanFormattable, IComparable, IComparable<Guid>, IEquatable<Guid>, ISpanParsable<Guid>
    {
        private const byte Variant10xxMask = 0xC0;
        private const byte Variant10xxValue = 0x80;
        private const ushort VersionMask = 0xF000;
        private const ushort Version4Value = 0x4000;
        private const ushort Version7Value = 0x7000;

        public static readonly Guid Empty;

        // Same field order and types as System.Guid: ToByteArray/TryWriteBytes/the byte-span ctor copy this layout.
        private readonly int _a;
        private readonly short _b;
        private readonly short _c;
        private readonly byte _d;
        private readonly byte _e;
        private readonly byte _f;
        private readonly byte _g;
        private readonly byte _h;
        private readonly byte _i;
        private readonly byte _j;
        private readonly byte _k;

        private static GuidImpl Of(Guid g) => *(GuidImpl*)&g;
        private static Guid To(GuidImpl g) => *(Guid*)&g;

        public static Guid AllBitsSet => To(new GuidImpl(uint.MaxValue, ushort.MaxValue, ushort.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue));

        public GuidImpl(byte[] b) : this(new ReadOnlySpan<byte>(b ?? throw new ArgumentNullException("b")))
        {
        }

        public GuidImpl(ReadOnlySpan<byte> b) : this(b, false)
        {
        }

        public GuidImpl(ReadOnlySpan<byte> b, bool bigEndian)
        {
            if (b.Length != 16) ThrowGuidArrayCtorArgumentException();
            if (bigEndian)
            {
                _a = (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
                _b = (short)((b[4] << 8) | b[5]);
                _c = (short)((b[6] << 8) | b[7]);
            }
            else
            {
                _a = b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
                _b = (short)(b[4] | (b[5] << 8));
                _c = (short)(b[6] | (b[7] << 8));
            }
            _d = b[8];
            _e = b[9];
            _f = b[10];
            _g = b[11];
            _h = b[12];
            _i = b[13];
            _j = b[14];
            _k = b[15];
        }

        private static void ThrowGuidArrayCtorArgumentException() =>
            throw new ArgumentException("Byte array for Guid must be exactly 16 bytes long.", "b");

        public GuidImpl(uint a, ushort b, ushort c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
        {
            _a = (int)a;
            _b = (short)b;
            _c = (short)c;
            _d = d;
            _e = e;
            _f = f;
            _g = g;
            _h = h;
            _i = i;
            _j = j;
            _k = k;
        }

        public GuidImpl(int a, short b, short c, byte[] d)
        {
            if (d == null) throw new ArgumentNullException("d");
            if (d.Length != 8) throw new ArgumentException("Byte array for Guid must be exactly 8 bytes long.", "d");
            _a = a;
            _b = b;
            _c = c;
            _d = d[0];
            _e = d[1];
            _f = d[2];
            _g = d[3];
            _h = d[4];
            _i = d[5];
            _j = d[6];
            _k = d[7];
        }

        public GuidImpl(int a, short b, short c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
        {
            _a = a;
            _b = b;
            _c = c;
            _d = d;
            _e = e;
            _f = f;
            _g = g;
            _h = h;
            _i = i;
            _j = j;
            _k = k;
        }

        public GuidImpl(string g)
        {
            if (g == null) throw new ArgumentNullException("g");
            var result = new GuidResult(GuidParseThrowStyle.All);
            TryParseGuid(g, ref result);
            this = result.ToGuid();
        }

        public int Variant => _d >> 4;

        public int Version => (ushort)_c >> 12;

        public static Guid NewGuid()
        {
            // Guid.Unix.cs: cryptographically-secure random bytes, then the version 4 / variant 10xx bits.
            byte* bytes = stackalloc byte[16];
            CoreDebug.RandomBytes(bytes, 16);
            GuidImpl g = *(GuidImpl*)bytes;
            return To(g.WithVersion(Version4Value));
        }

        private GuidImpl WithVersion(ushort version) =>
            new GuidImpl(_a, _b, (short)((_c & ~VersionMask) | version),
                (byte)((_d & ~Variant10xxMask) | Variant10xxValue), _e, _f, _g, _h, _i, _j, _k);

        public static Guid CreateVersion7()
        {
            // DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), without DateTimeOffset.
            long unix_ts_ms = (DateTime.UtcNow.Ticks - 621355968000000000L) / 10000;
            GuidImpl r = Of(NewGuid());
            return To(new GuidImpl((int)(unix_ts_ms >> 16), (short)unix_ts_ms, r._c, r._d, r._e, r._f, r._g, r._h, r._i, r._j, r._k)
                .WithVersion(Version7Value));
        }

        // ---- parsing -------------------------------------------------------------------------------

        private enum GuidParseThrowStyle : byte
        {
            None = 0,
            All = 1,
            AllButOverflow = 2
        }

        private enum ParseFailure
        {
            Format_ExtraJunkAtEnd,
            Format_GuidBraceAfterLastNumber,
            Format_GuidBrace,
            Format_GuidComma,
            Format_GuidDashes,
            Format_GuidEndBrace,
            Format_GuidHexPrefix,
            Format_GuidInvalidChar,
            Format_GuidInvLen,
            Format_GuidUnrecognized,
            Overflow_Byte,
            Overflow_UInt32,
        }

        private struct GuidResult
        {
            internal uint _a;
            internal ushort _b;
            internal ushort _c;
            internal byte _d, _e, _f, _g, _h, _i, _j, _k;
            private readonly GuidParseThrowStyle _throwStyle;

            internal GuidResult(GuidParseThrowStyle canThrow)
            {
                this = default;
                _throwStyle = canThrow;
            }

            internal readonly void SetFailure(ParseFailure failureKind)
            {
                if (_throwStyle == GuidParseThrowStyle.None) return;
                if (failureKind == ParseFailure.Overflow_UInt32 && _throwStyle == GuidParseThrowStyle.All)
                    throw new OverflowException("Value was either too large or too small for a UInt32.");
                string message;
                switch (failureKind)
                {
                    case ParseFailure.Format_ExtraJunkAtEnd: message = "Additional non-parsable characters are at the end of the string."; break;
                    case ParseFailure.Format_GuidBraceAfterLastNumber: message = "Could not find a brace, or the length between the previous token and the brace was zero (i.e., '0x,'etc.)."; break;
                    case ParseFailure.Format_GuidBrace: message = "Expected {0xdddddddd, etc}."; break;
                    case ParseFailure.Format_GuidComma: message = "Could not find a comma, or the length between the previous token and the comma was zero (i.e., '0x,'etc.)."; break;
                    case ParseFailure.Format_GuidDashes: message = "Dashes are in the wrong position for GUID parsing."; break;
                    case ParseFailure.Format_GuidEndBrace: message = "Could not find the ending brace."; break;
                    case ParseFailure.Format_GuidHexPrefix: message = "Expected 0x prefix."; break;
                    case ParseFailure.Format_GuidInvalidChar: message = "Guid string should only contain hexadecimal characters."; break;
                    case ParseFailure.Format_GuidInvLen: message = "Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)."; break;
                    default: message = "Unrecognized Guid format."; break;
                }
                throw new FormatException(message);
            }

            internal readonly GuidImpl ToGuid() => new GuidImpl(_a, _b, _c, _d, _e, _f, _g, _h, _i, _j, _k);

            internal void SetByte(int index, byte value)
            {
                switch (index)
                {
                    case 0: _d = value; break;
                    case 1: _e = value; break;
                    case 2: _f = value; break;
                    case 3: _g = value; break;
                    case 4: _h = value; break;
                    case 5: _i = value; break;
                    case 6: _j = value; break;
                    default: _k = value; break;
                }
            }
        }

        public static Guid Parse(string input)
        {
            if (input == null) throw new ArgumentNullException("input");
            return Parse((ReadOnlySpan<char>)input);
        }

        public static Guid Parse(ReadOnlySpan<char> input)
        {
            var result = new GuidResult(GuidParseThrowStyle.AllButOverflow);
            TryParseGuid(input, ref result);
            return To(result.ToGuid());
        }

        public static bool TryParse(string input, out Guid result)
        {
            if (input == null)
            {
                result = default;
                return false;
            }
            return TryParse((ReadOnlySpan<char>)input, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> input, out Guid result)
        {
            var parseResult = new GuidResult(GuidParseThrowStyle.None);
            if (TryParseGuid(input, ref parseResult))
            {
                result = To(parseResult.ToGuid());
                return true;
            }
            result = default;
            return false;
        }

        public static Guid ParseExact(string input, string format)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (format == null) throw new ArgumentNullException("format");
            return ParseExact((ReadOnlySpan<char>)input, (ReadOnlySpan<char>)format);
        }

        public static Guid ParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format)
        {
            if (format.Length != 1) ThrowBadGuidFormatSpecification();
            input = TrimWhiteSpace(input);
            var result = new GuidResult(GuidParseThrowStyle.AllButOverflow);
            switch ((char)(format[0] | 0x20))
            {
                case 'd': TryParseExactD(input, ref result); break;
                case 'n': TryParseExactN(input, ref result); break;
                case 'b': TryParseExactB(input, ref result); break;
                case 'p': TryParseExactP(input, ref result); break;
                case 'x': TryParseExactX(input, ref result); break;
                default: ThrowBadGuidFormatSpecification(); break;
            }
            return To(result.ToGuid());
        }

        public static bool TryParseExact(string input, string format, out Guid result)
        {
            if (input == null)
            {
                result = default;
                return false;
            }
            return TryParseExact((ReadOnlySpan<char>)input, (ReadOnlySpan<char>)format, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, out Guid result)
        {
            if (format.Length != 1 || input.Length < 32) // Minimal length we can parse ('N' format)
            {
                result = default;
                return false;
            }
            input = TrimWhiteSpace(input);
            var parseResult = new GuidResult(GuidParseThrowStyle.None);
            bool success;
            switch ((char)(format[0] | 0x20))
            {
                case 'd': success = TryParseExactD(input, ref parseResult); break;
                case 'n': success = TryParseExactN(input, ref parseResult); break;
                case 'b': success = TryParseExactB(input, ref parseResult); break;
                case 'p': success = TryParseExactP(input, ref parseResult); break;
                case 'x': success = TryParseExactX(input, ref parseResult); break;
                default: success = false; break;
            }
            result = success ? To(parseResult.ToGuid()) : default;
            return success;
        }

        public static Guid Parse(string s, IFormatProvider provider) => Parse(s);
        public static bool TryParse(string s, IFormatProvider provider, out Guid result) => TryParse(s, out result);
        public static Guid Parse(ReadOnlySpan<char> s, IFormatProvider provider) => Parse(s);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out Guid result) => TryParse(s, out result);

        /// <summary>MemoryExtensions.Trim: char.IsWhiteSpace at both ends (ParseExact).</summary>
        private static ReadOnlySpan<char> TrimWhiteSpace(ReadOnlySpan<char> s)
        {
            int start = 0, end = s.Length - 1;
            while (start <= end && char.IsWhiteSpace(s[start])) start++;
            while (end >= start && char.IsWhiteSpace(s[end])) end--;
            return s.Slice(start, end - start + 1);
        }

        /// <summary>Number.SpanTrim: only ' ' and '\t'..'\r' (Parse / TryParse / the string ctor).</summary>
        private static ReadOnlySpan<char> SpanTrim(ReadOnlySpan<char> s)
        {
            int start = 0, end = s.Length - 1;
            while (start <= end && IsWhite(s[start])) start++;
            while (end >= start && IsWhite(s[end])) end--;
            return s.Slice(start, end - start + 1);
        }

        private static bool IsWhite(char ch) => ch == 0x20 || (uint)(ch - 0x09) <= (0x0D - 0x09);

        private static bool TryParseGuid(ReadOnlySpan<char> guidString, ref GuidResult result)
        {
            guidString = SpanTrim(guidString); // Remove whitespace from beginning and end

            if (guidString.Length < 32) // Minimal length we can parse ('N' format)
            {
                result.SetFailure(ParseFailure.Format_GuidUnrecognized);
                return false;
            }

            switch (guidString[0])
            {
                case '(':
                    return TryParseExactP(guidString, ref result);
                case '{':
                    return guidString[9] == '-' ? TryParseExactB(guidString, ref result) : TryParseExactX(guidString, ref result);
                default:
                    return guidString[8] == '-' ? TryParseExactD(guidString, ref result) : TryParseExactN(guidString, ref result);
            }
        }

        private static bool TryParseExactB(ReadOnlySpan<char> guidString, ref GuidResult result)
        {
            // e.g. "{d85b1407-351d-4694-9392-03acc5870eb1}"
            if (guidString.Length != 38 || guidString[0] != '{' || guidString[37] != '}')
            {
                result.SetFailure(ParseFailure.Format_GuidInvLen);
                return false;
            }
            return TryParseExactD(guidString.Slice(1, 36), ref result);
        }

        private static bool TryParseExactD(ReadOnlySpan<char> guidString, ref GuidResult result)
        {
            // e.g. "d85b1407-351d-4694-9392-03acc5870eb1"
            if (guidString.Length != 36 || guidString[8] != '-' || guidString[13] != '-' || guidString[18] != '-' || guidString[23] != '-')
            {
                result.SetFailure(guidString.Length != 36 ? ParseFailure.Format_GuidInvLen : ParseFailure.Format_GuidDashes);
                return false;
            }

            int invalidIfNegative = 0;
            result._a = (uint)((DecodeByte(guidString[0], guidString[1], ref invalidIfNegative) << 24)
                | (DecodeByte(guidString[2], guidString[3], ref invalidIfNegative) << 16)
                | (DecodeByte(guidString[4], guidString[5], ref invalidIfNegative) << 8)
                | DecodeByte(guidString[6], guidString[7], ref invalidIfNegative));
            result._b = (ushort)((DecodeByte(guidString[9], guidString[10], ref invalidIfNegative) << 8)
                | DecodeByte(guidString[11], guidString[12], ref invalidIfNegative));
            result._c = (ushort)((DecodeByte(guidString[14], guidString[15], ref invalidIfNegative) << 8)
                | DecodeByte(guidString[16], guidString[17], ref invalidIfNegative));
            result._d = DecodeByte(guidString[19], guidString[20], ref invalidIfNegative);
            result._e = DecodeByte(guidString[21], guidString[22], ref invalidIfNegative);
            result._f = DecodeByte(guidString[24], guidString[25], ref invalidIfNegative);
            result._g = DecodeByte(guidString[26], guidString[27], ref invalidIfNegative);
            result._h = DecodeByte(guidString[28], guidString[29], ref invalidIfNegative);
            result._i = DecodeByte(guidString[30], guidString[31], ref invalidIfNegative);
            result._j = DecodeByte(guidString[32], guidString[33], ref invalidIfNegative);
            result._k = DecodeByte(guidString[34], guidString[35], ref invalidIfNegative);

            if (invalidIfNegative >= 0) return true;

            // The 'D' format has some undesirable behavior leftover from its original implementation:
            // - Components may begin with "0x" and/or "+", but the expected length of each component
            //   needs to include those prefixes, e.g. a four digit component could be "1234" or
            //   "0x34" or "+0x4" or "+234", but not "0x1234" nor "+1234" nor "+0x1234".
            // - "0X" is valid instead of "0x"
            if (ContainsCompatChar(guidString) && TryCompatParsing(guidString, ref result))
                return true;

            result.SetFailure(ParseFailure.Format_GuidInvalidChar);
            return false;
        }

        private static bool ContainsCompatChar(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] == 'X' || s[i] == 'x' || s[i] == '+') return true;
            return false;
        }

        private static bool TryCompatParsing(ReadOnlySpan<char> guidString, ref GuidResult result)
        {
            guidString = guidString.Slice(0, 36);
            if (TryParseHex(guidString.Slice(0, 8), out result._a) && // _a
                TryParseHex(guidString.Slice(9, 4), out uint uintTmp)) // _b
            {
                result._b = (ushort)uintTmp;
                if (TryParseHex(guidString.Slice(14, 4), out uintTmp)) // _c
                {
                    result._c = (ushort)uintTmp;
                    if (TryParseHex(guidString.Slice(19, 4), out uintTmp)) // _d, _e
                    {
                        result._d = (byte)(uintTmp >> 8);
                        result._e = (byte)uintTmp;
                        if (TryParseHex(guidString.Slice(24, 4), out uintTmp)) // _f, _g
                        {
                            result._f = (byte)(uintTmp >> 8);
                            result._g = (byte)uintTmp;
                            // Unlike the other components, this one never allowed 0x or +, so we can parse it as straight hex.
                            if (TryParseStrictHex(guidString.Slice(28, 8), out uintTmp)) // _h, _i, _j, _k
                            {
                                result._h = (byte)(uintTmp >> 24);
                                result._i = (byte)(uintTmp >> 16);
                                result._j = (byte)(uintTmp >> 8);
                                result._k = (byte)uintTmp;
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>uint.TryParse(s, NumberStyles.AllowHexSpecifier) for at most 8 digits.</summary>
        private static bool TryParseStrictHex(ReadOnlySpan<char> s, out uint result)
        {
            result = 0;
            if (s.Length == 0) return false;
            uint tmp = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int v = FromChar(s[i]);
                if (v == 0xFF) return false;
                tmp = (tmp << 4) | (uint)v;
            }
            result = tmp;
            return true;
        }

        private static bool TryParseExactN(ReadOnlySpan<char> guidString, ref GuidResult result)
        {
            // e.g. "d85b1407351d4694939203acc5870eb1"
            if (guidString.Length != 32)
            {
                result.SetFailure(ParseFailure.Format_GuidInvLen);
                return false;
            }

            int invalidIfNegative = 0;
            result._a = (uint)((DecodeByte(guidString[0], guidString[1], ref invalidIfNegative) << 24)
                | (DecodeByte(guidString[2], guidString[3], ref invalidIfNegative) << 16)
                | (DecodeByte(guidString[4], guidString[5], ref invalidIfNegative) << 8)
                | DecodeByte(guidString[6], guidString[7], ref invalidIfNegative));
            result._b = (ushort)((DecodeByte(guidString[8], guidString[9], ref invalidIfNegative) << 8)
                | DecodeByte(guidString[10], guidString[11], ref invalidIfNegative));
            result._c = (ushort)((DecodeByte(guidString[12], guidString[13], ref invalidIfNegative) << 8)
                | DecodeByte(guidString[14], guidString[15], ref invalidIfNegative));
            for (int i = 0; i < 8; i++)
                result.SetByte(i, DecodeByte(guidString[16 + 2 * i], guidString[17 + 2 * i], ref invalidIfNegative));

            if (invalidIfNegative >= 0) return true;

            result.SetFailure(ParseFailure.Format_GuidInvalidChar);
            return false;
        }

        private static bool TryParseExactP(ReadOnlySpan<char> guidString, ref GuidResult result)
        {
            // e.g. "(d85b1407-351d-4694-9392-03acc5870eb1)"
            if (guidString.Length != 38 || guidString[0] != '(' || guidString[37] != ')')
            {
                result.SetFailure(ParseFailure.Format_GuidInvLen);
                return false;
            }
            return TryParseExactD(guidString.Slice(1, 36), ref result);
        }

        private static bool TryParseExactX(ReadOnlySpan<char> guidString, ref GuidResult result)
        {
            // e.g. "{0xd85b1407,0x351d,0x4694,{0x93,0x92,0x03,0xac,0xc5,0x87,0x0e,0xb1}}"

            // Compat notes due to the previous implementation's implementation details.
            // - Each component need not be the full expected number of digits.
            // - Each component may contain any number of leading 0s
            // - The "short" components are parsed as 32-bits and only considered to overflow if they'd overflow 32 bits.
            // - The "byte" components are parsed as 32-bits and are considered to overflow if they'd overflow 8 bits,
            //   but for the Guid ctor, whether they overflow 8 bits or 32 bits results in differing exceptions.
            // - Components may begin with "0x", "0x+", even "0x+0x".
            // - "0X" is valid instead of "0x"

            // Eat all of the whitespace.  Unlike the other forms, X allows for any amount of whitespace
            // anywhere, not just at the beginning and end.
            guidString = EatAllWhitespace(guidString);

            // Check for leading '{'
            if (guidString.Length == 0 || guidString[0] != '{')
            {
                result.SetFailure(ParseFailure.Format_GuidBrace);
                return false;
            }

            // Check for '0x'
            if (!IsHexPrefix(guidString, 1))
            {
                result.SetFailure(ParseFailure.Format_GuidHexPrefix);
                return false;
            }

            // Find the end of this hex number (since it is not fixed length)
            int numStart = 3;
            int numLen = guidString.Slice(numStart).IndexOf(',');
            if (numLen <= 0)
            {
                result.SetFailure(ParseFailure.Format_GuidComma);
                return false;
            }

            bool overflow = false;
            if (!TryParseHex(guidString.Slice(numStart, numLen), out result._a, ref overflow) || overflow)
            {
                result.SetFailure(overflow ? ParseFailure.Overflow_UInt32 : ParseFailure.Format_GuidInvalidChar);
                return false;
            }

            // Check for '0x'
            if (!IsHexPrefix(guidString, numStart + numLen + 1))
            {
                result.SetFailure(ParseFailure.Format_GuidHexPrefix);
                return false;
            }
            // +3 to get by ',0x'
            numStart = numStart + numLen + 3;
            numLen = guidString.Slice(numStart).IndexOf(',');
            if (numLen <= 0)
            {
                result.SetFailure(ParseFailure.Format_GuidComma);
                return false;
            }

            // Read in the number
            if (!TryParseHex(guidString.Slice(numStart, numLen), out uint tmp, ref overflow) || overflow)
            {
                result.SetFailure(overflow ? ParseFailure.Overflow_UInt32 : ParseFailure.Format_GuidInvalidChar);
                return false;
            }
            result._b = (ushort)tmp;

            // Check for '0x'
            if (!IsHexPrefix(guidString, numStart + numLen + 1))
            {
                result.SetFailure(ParseFailure.Format_GuidHexPrefix);
                return false;
            }
            // +3 to get by ',0x'
            numStart = numStart + numLen + 3;
            numLen = guidString.Slice(numStart).IndexOf(',');
            if (numLen <= 0)
            {
                result.SetFailure(ParseFailure.Format_GuidComma);
                return false;
            }

            // Read in the number
            if (!TryParseHex(guidString.Slice(numStart, numLen), out tmp, ref overflow) || overflow)
            {
                result.SetFailure(overflow ? ParseFailure.Overflow_UInt32 : ParseFailure.Format_GuidInvalidChar);
                return false;
            }
            result._c = (ushort)tmp;

            // Check for '{'
            if ((uint)guidString.Length <= (uint)(numStart + numLen + 1) || guidString[numStart + numLen + 1] != '{')
            {
                result.SetFailure(ParseFailure.Format_GuidBrace);
                return false;
            }

            // Prepare for loop
            numLen++;
            for (int i = 0; i < 8; i++)
            {
                // Check for '0x'
                if (!IsHexPrefix(guidString, numStart + numLen + 1))
                {
                    result.SetFailure(ParseFailure.Format_GuidHexPrefix);
                    return false;
                }

                // +3 to get by ',0x' or '{0x' for first case
                numStart = numStart + numLen + 3;

                // Calculate number length
                if (i < 7)  // first 7 cases
                {
                    numLen = guidString.Slice(numStart).IndexOf(',');
                    if (numLen <= 0)
                    {
                        result.SetFailure(ParseFailure.Format_GuidComma);
                        return false;
                    }
                }
                else // last case ends with '}', not ','
                {
                    numLen = guidString.Slice(numStart).IndexOf('}');
                    if (numLen <= 0)
                    {
                        result.SetFailure(ParseFailure.Format_GuidBraceAfterLastNumber);
                        return false;
                    }
                }

                // Read in the number
                if (!TryParseHex(guidString.Slice(numStart, numLen), out uint byteVal, ref overflow) || overflow || byteVal > byte.MaxValue)
                {
                    // The previous implementation had some odd inconsistencies, which are carried forward here.
                    // The byte values in the X format are treated as integers with regards to overflow, so
                    // a "byte" value like 0xddd in Guid's ctor results in a FormatException but 0xddddddddd results
                    // in OverflowException.
                    result.SetFailure(
                        overflow ? ParseFailure.Overflow_UInt32 :
                        byteVal > byte.MaxValue ? ParseFailure.Overflow_Byte :
                        ParseFailure.Format_GuidInvalidChar);
                    return false;
                }
                result.SetByte(i, (byte)byteVal);
            }

            // Check for last '}'
            if (numStart + numLen + 1 >= guidString.Length || guidString[numStart + numLen + 1] != '}')
            {
                result.SetFailure(ParseFailure.Format_GuidEndBrace);
                return false;
            }

            // Check if we have extra characters at the end
            if (numStart + numLen + 1 != guidString.Length - 1)
            {
                result.SetFailure(ParseFailure.Format_ExtraJunkAtEnd);
                return false;
            }

            return true;
        }

        /// <summary>HexConverter.FromChar: the digit value, or 0xFF.</summary>
        private static int FromChar(int c)
        {
            if ((uint)(c - '0') <= 9) return c - '0';
            if ((uint)(c - 'a') <= 5) return c - 'a' + 10;
            if ((uint)(c - 'A') <= 5) return c - 'A' + 10;
            return 0xFF;
        }

        private static byte DecodeByte(char ch1, char ch2, ref int invalidIfNegative)
        {
            int upper = FromChar(ch1);
            int lower = FromChar(ch2);
            if (upper == 0xFF || lower == 0xFF)
            {
                invalidIfNegative = -1;
                return 0;
            }
            return (byte)((upper << 4) | lower);
        }

        private static bool TryParseHex(ReadOnlySpan<char> guidString, out uint result)
        {
            bool overflowIgnored = false;
            return TryParseHex(guidString, out result, ref overflowIgnored);
        }

        private static bool TryParseHex(ReadOnlySpan<char> guidString, out uint result, ref bool overflow)
        {
            if (guidString.Length > 0)
            {
                if (guidString[0] == '+')
                    guidString = guidString.Slice(1);

                if (guidString.Length > 1 && guidString[0] == '0' && (guidString[1] | 0x20) == 'x')
                    guidString = guidString.Slice(2);
            }

            // Skip past leading 0s.
            int i = 0;
            for (; i < guidString.Length && guidString[i] == '0'; i++) ;

            int processedDigits = 0;
            uint tmp = 0;
            for (; i < guidString.Length; i++)
            {
                int numValue = FromChar(guidString[i]);
                if (numValue == 0xFF)
                {
                    if (processedDigits > 8) overflow = true;
                    result = 0;
                    return false;
                }
                tmp = unchecked((tmp * 16) + (uint)numValue);
                processedDigits++;
            }

            if (processedDigits > 8) overflow = true;
            result = tmp;
            return true;
        }

        private static ReadOnlySpan<char> EatAllWhitespace(ReadOnlySpan<char> charSpan)
        {
            // Find the first whitespace character. If there is none, just return the input.
            int i = 0;
            while (i < charSpan.Length && !char.IsWhiteSpace(charSpan[i])) i++;
            if (i == charSpan.Length) return charSpan;

            // There was at least one whitespace. Copy over everything prior to it to a new array.
            var chArr = new char[charSpan.Length];
            int newLength = i;
            for (int j = 0; j < i; j++) chArr[j] = charSpan[j];

            // Loop through the remaining chars, copying over non-whitespace.
            for (; i < charSpan.Length; i++)
            {
                char c = charSpan[i];
                if (!char.IsWhiteSpace(c)) chArr[newLength++] = c;
            }
            return new ReadOnlySpan<char>(chArr, 0, newLength);
        }

        private static bool IsHexPrefix(ReadOnlySpan<char> str, int i) =>
            i + 1 < str.Length && str[i] == '0' && (str[i + 1] | 0x20) == 'x';

        // ---- bytes ---------------------------------------------------------------------------------

        private void WriteBytes(Span<byte> g, bool bigEndian)
        {
            if (bigEndian)
            {
                g[0] = (byte)(_a >> 24); g[1] = (byte)(_a >> 16); g[2] = (byte)(_a >> 8); g[3] = (byte)_a;
                g[4] = (byte)(_b >> 8); g[5] = (byte)_b;
                g[6] = (byte)(_c >> 8); g[7] = (byte)_c;
            }
            else
            {
                g[0] = (byte)_a; g[1] = (byte)(_a >> 8); g[2] = (byte)(_a >> 16); g[3] = (byte)(_a >> 24);
                g[4] = (byte)_b; g[5] = (byte)(_b >> 8);
                g[6] = (byte)_c; g[7] = (byte)(_c >> 8);
            }
            g[8] = _d; g[9] = _e; g[10] = _f; g[11] = _g; g[12] = _h; g[13] = _i; g[14] = _j; g[15] = _k;
        }

        public byte[] ToByteArray()
        {
            var g = new byte[16];
            WriteBytes(g, false);
            return g;
        }

        public byte[] ToByteArray(bool bigEndian)
        {
            var g = new byte[16];
            WriteBytes(g, bigEndian);
            return g;
        }

        public bool TryWriteBytes(Span<byte> destination)
        {
            if (destination.Length < 16) return false;
            WriteBytes(destination, false);
            return true;
        }

        public bool TryWriteBytes(Span<byte> destination, bool bigEndian, out int bytesWritten)
        {
            if (destination.Length < 16)
            {
                bytesWritten = 0;
                return false;
            }
            WriteBytes(destination, bigEndian);
            bytesWritten = 16;
            return true;
        }

        // ---- equality and ordering -----------------------------------------------------------------

        // Simply XOR all the bits of the GUID 32 bits at a time (little-endian layout of _b.._k).
        public override int GetHashCode() =>
            _a ^ ((ushort)_b | (_c << 16)) ^ (_d | (_e << 8) | (_f << 16) | (_g << 24)) ^ (_h | (_i << 8) | (_j << 16) | (_k << 24));

        public override bool Equals(object o) => o is Guid g && EqualsCore(this, Of(g));

        public bool Equals(Guid g) => EqualsCore(this, Of(g));

        private static bool EqualsCore(GuidImpl left, GuidImpl right) =>
            left._a == right._a && left._b == right._b && left._c == right._c
            && left._d == right._d && left._e == right._e && left._f == right._f && left._g == right._g
            && left._h == right._h && left._i == right._i && left._j == right._j && left._k == right._k;

        private static int GetResult(uint me, uint them) => me < them ? -1 : 1;

        public int CompareTo(object value)
        {
            if (value == null) return 1;
            if (!(value is Guid other)) throw new ArgumentException("Object must be of type GUID.", "value");
            return CompareTo(other);
        }

        public int CompareTo(Guid value) => CompareCore(this, Of(value));

        // Unsigned field-by-field order (CompareTo and the comparison operators agree).
        private static int CompareCore(GuidImpl left, GuidImpl right)
        {
            if (right._a != left._a) return GetResult((uint)left._a, (uint)right._a);
            if (right._b != left._b) return GetResult((uint)left._b, (uint)right._b);
            if (right._c != left._c) return GetResult((uint)left._c, (uint)right._c);
            if (right._d != left._d) return GetResult(left._d, right._d);
            if (right._e != left._e) return GetResult(left._e, right._e);
            if (right._f != left._f) return GetResult(left._f, right._f);
            if (right._g != left._g) return GetResult(left._g, right._g);
            if (right._h != left._h) return GetResult(left._h, right._h);
            if (right._i != left._i) return GetResult(left._i, right._i);
            if (right._j != left._j) return GetResult(left._j, right._j);
            if (right._k != left._k) return GetResult(left._k, right._k);
            return 0;
        }

        public static bool operator ==(GuidImpl a, GuidImpl b) => EqualsCore(a, b);
        public static bool operator !=(GuidImpl a, GuidImpl b) => !EqualsCore(a, b);
        public static bool operator <(GuidImpl left, GuidImpl right) => CompareCore(left, right) < 0;
        public static bool operator <=(GuidImpl left, GuidImpl right) => CompareCore(left, right) <= 0;
        public static bool operator >(GuidImpl left, GuidImpl right) => CompareCore(left, right) > 0;
        public static bool operator >=(GuidImpl left, GuidImpl right) => CompareCore(left, right) >= 0;

        // ---- formatting ----------------------------------------------------------------------------

        public override string ToString() => ToString("d", null);

        public string ToString(string format) => ToString(format, null);

        // The provider is ignored, as in .NET.
        public string ToString(string format, IFormatProvider provider)
        {
            int guidSize;
            if (string.IsNullOrEmpty(format))
            {
                guidSize = 36;
            }
            else
            {
                // all acceptable format strings are of length 1
                if (format.Length != 1) ThrowBadGuidFormatSpecification();
                switch (format[0] | 0x20)
                {
                    case 'd': guidSize = 36; break;
                    case 'n': guidSize = 32; break;
                    case 'b':
                    case 'p': guidSize = 38; break;
                    case 'x': guidSize = 68; break;
                    default: guidSize = 0; ThrowBadGuidFormatSpecification(); break;
                }
            }
            string guidString = CoreIntrinsics.FastAllocateString(guidSize);
            TryFormatCore(CoreIntrinsics.StringChars(guidString), out _, format);
            return guidString;
        }

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default) =>
            TryFormatCore(destination, out charsWritten, format);

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider) =>
            TryFormatCore(destination, out charsWritten, format);

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default)
        {
            // Every char of a formatted Guid is ASCII: format as UTF-16, then narrow.
            Span<char> chars = stackalloc char[68];
            if (!TryFormatCore(chars, out int written, format) || written > utf8Destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            for (int i = 0; i < written; i++) utf8Destination[i] = (byte)chars[i];
            bytesWritten = written;
            return true;
        }

        private const int TryFormatFlags_UseDashes = unchecked((int)0x80000000);
        private const int TryFormatFlags_CurlyBraces = ('}' << 16) | ('{' << 8);
        private const int TryFormatFlags_Parens = (')' << 16) | ('(' << 8);

        private bool TryFormatCore(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            int flags;
            if (format.Length == 0)
            {
                flags = 36 + TryFormatFlags_UseDashes;
            }
            else
            {
                if (format.Length != 1) ThrowBadGuidFormatSpecification();
                switch (format[0] | 0x20)
                {
                    case 'd': flags = 36 + TryFormatFlags_UseDashes; break;
                    case 'p': flags = 38 + TryFormatFlags_UseDashes + TryFormatFlags_Parens; break;
                    case 'b': flags = 38 + TryFormatFlags_UseDashes + TryFormatFlags_CurlyBraces; break;
                    case 'n': flags = 32; break;
                    case 'x': return TryFormatX(destination, out charsWritten);
                    default: flags = 0; ThrowBadGuidFormatSpecification(); break;
                }
            }
            return TryFormatCore(destination, out charsWritten, flags);
        }

        // flags: lowest byte = required length; second byte = opening brace (or 0); third byte = closing
        // brace (or 0); highest bit = use dashes.
        private bool TryFormatCore(Span<char> destination, out int charsWritten, int flags)
        {
            if ((byte)flags > destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            charsWritten = (byte)flags;
            flags >>= 8;

            int p = 0;
            if ((byte)flags != 0) destination[p++] = (char)(byte)flags;
            flags >>= 8;

            p = HexsToChars(destination, p, _a >> 24, _a >> 16);
            p = HexsToChars(destination, p, _a >> 8, _a);
            if (flags < 0) destination[p++] = '-';
            p = HexsToChars(destination, p, _b >> 8, _b);
            if (flags < 0) destination[p++] = '-';
            p = HexsToChars(destination, p, _c >> 8, _c);
            if (flags < 0) destination[p++] = '-';
            p = HexsToChars(destination, p, _d, _e);
            if (flags < 0) destination[p++] = '-';
            p = HexsToChars(destination, p, _f, _g);
            p = HexsToChars(destination, p, _h, _i);
            p = HexsToChars(destination, p, _j, _k);

            if ((byte)flags != 0) destination[p] = (char)(byte)flags;
            return true;
        }

        private static char ToCharLower(int value)
        {
            value &= 0xF;
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }

        private static int HexsToChars(Span<char> dest, int p, int a, int b)
        {
            dest[p] = ToCharLower(a >> 4);
            dest[p + 1] = ToCharLower(a);
            dest[p + 2] = ToCharLower(b >> 4);
            dest[p + 3] = ToCharLower(b);
            return p + 4;
        }

        private bool TryFormatX(Span<char> dest, out int charsWritten)
        {
            if (dest.Length < 68)
            {
                charsWritten = 0;
                return false;
            }

            // {0xdddddddd,0xdddd,0xdddd,{0xdd,0xdd,0xdd,0xdd,0xdd,0xdd,0xdd,0xdd}}
            dest[0] = '{';
            dest[1] = '0';
            dest[2] = 'x';
            for (int n = 0; n < 8; n++) dest[3 + n] = ToCharLower(_a >> (28 - 4 * n));
            dest[11] = ',';
            dest[12] = '0';
            dest[13] = 'x';
            for (int n = 0; n < 4; n++) dest[14 + n] = ToCharLower(_b >> (12 - 4 * n));
            dest[18] = ',';
            dest[19] = '0';
            dest[20] = 'x';
            for (int n = 0; n < 4; n++) dest[21 + n] = ToCharLower(_c >> (12 - 4 * n));
            dest[25] = ',';
            dest[26] = '{';
            WriteHex(dest, 27, _d, true);
            WriteHex(dest, 32, _e, true);
            WriteHex(dest, 37, _f, true);
            WriteHex(dest, 42, _g, true);
            WriteHex(dest, 47, _h, true);
            WriteHex(dest, 52, _i, true);
            WriteHex(dest, 57, _j, true);
            WriteHex(dest, 62, _k, false);
            dest[66] = '}';
            dest[67] = '}';
            charsWritten = 68;
            return true;
        }

        private static void WriteHex(Span<char> dest, int offset, int val, bool appendComma)
        {
            dest[offset + 0] = '0';
            dest[offset + 1] = 'x';
            dest[offset + 2] = ToCharLower(val >> 4);
            dest[offset + 3] = ToCharLower(val);
            if (appendComma) dest[offset + 4] = ',';
        }

        private static void ThrowBadGuidFormatSpecification() =>
            throw new FormatException("Format string can be only \"D\", \"d\", \"N\", \"n\", \"P\", \"p\", \"B\", \"b\", \"X\" or \"x\".");
    }
}
