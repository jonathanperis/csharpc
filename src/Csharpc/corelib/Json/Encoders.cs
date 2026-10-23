// System.Text.Encodings.Web: TextEncoder / JavaScriptEncoder (the inbox "optimized" encoders),
// TextEncoderSettings and UnicodeRange. Utf8JsonWriter escapes through these.
//
// Ported from dotnet/runtime src/libraries/System.Text.Encodings.Web (release/11.0).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Unicode.UnicodeRange")]
    public sealed class UnicodeRangeImpl
    {
        public UnicodeRangeImpl(int firstCodePoint, int length)
        {
            if (firstCodePoint < 0 || firstCodePoint > 0xFFFF) throw new ArgumentOutOfRangeException("firstCodePoint");
            if (length < 0 || ((long)firstCodePoint + (long)length > 0x10000)) throw new ArgumentOutOfRangeException("length");
            FirstCodePoint = firstCodePoint;
            Length = length;
        }

        public int FirstCodePoint { get; }

        public int Length { get; }

        public static UnicodeRangeImpl Create(char firstCharacter, char lastCharacter)
        {
            if (lastCharacter < firstCharacter) throw new ArgumentOutOfRangeException("lastCharacter");
            return new UnicodeRangeImpl(firstCharacter, 1 + (int)(lastCharacter - firstCharacter));
        }
    }

    /// <summary>64K-bit set of allowed BMP code points (AllowedBmpCodePointsBitmap).</summary>
    internal sealed class AllowedBmpBitmap
    {
        internal readonly uint[] Bits = new uint[2048];

        public void AllowChar(int c) => Bits[c >> 5] |= 1u << (c & 31);
        public void ForbidChar(int c) => Bits[c >> 5] &= ~(1u << (c & 31));
        public bool IsAllowed(int c) => (Bits[c >> 5] & (1u << (c & 31))) != 0;

        public AllowedBmpBitmap Clone()
        {
            AllowedBmpBitmap copy = new AllowedBmpBitmap();
            Array.Copy(Bits, copy.Bits, Bits.Length);
            return copy;
        }

        public void ForbidHtmlCharacters()
        {
            ForbidChar('<');
            ForbidChar('>');
            ForbidChar('&');
            ForbidChar('\'');
            ForbidChar('"');
            ForbidChar('+');
        }

        public void ForbidUndefinedCharacters()
        {
            ReadOnlySpan<byte> hex = UnicodeDefinedChars.BitmapHex;
            for (int i = 0; i < 2048; i++)
            {
                uint word = 0;
                for (int b = 0; b < 4; b++)
                {
                    int value = (Hex(hex[(i * 4 + b) * 2]) << 4) | Hex(hex[(i * 4 + b) * 2 + 1]);
                    word |= (uint)value << (8 * b);
                }
                Bits[i] &= word;
            }
        }

        private static int Hex(byte c) => c <= '9' ? c - '0' : c - 'A' + 10;
    }

    [Shadows("System.Text.Encodings.Web.TextEncoderSettings")]
    public class TextEncoderSettingsImpl
    {
        internal AllowedBmpBitmap _bitmap = new AllowedBmpBitmap();

        public TextEncoderSettingsImpl() { }

        public TextEncoderSettingsImpl(TextEncoderSettingsImpl other)
        {
            if (other == null) throw new ArgumentNullException("other");
            _bitmap = other._bitmap.Clone();
        }

        public TextEncoderSettingsImpl(params UnicodeRangeImpl[] allowedRanges)
        {
            if (allowedRanges == null) throw new ArgumentNullException("allowedRanges");
            AllowRanges(allowedRanges);
        }

        public virtual void AllowCharacter(char character) => _bitmap.AllowChar(character);

        public virtual void AllowCharacters(params char[] characters)
        {
            if (characters == null) throw new ArgumentNullException("characters");
            for (int i = 0; i < characters.Length; i++) _bitmap.AllowChar(characters[i]);
        }

        public virtual void AllowCodePoints(IEnumerable<int> codePoints)
        {
            if (codePoints == null) throw new ArgumentNullException("codePoints");
            foreach (int codePoint in codePoints)
                if ((uint)codePoint <= 0xFFFF) _bitmap.AllowChar(codePoint);
        }

        public virtual void AllowRange(UnicodeRangeImpl range)
        {
            if (range == null) throw new ArgumentNullException("range");
            int first = range.FirstCodePoint;
            for (int i = 0; i < range.Length; i++) _bitmap.AllowChar(first + i);
        }

        public virtual void AllowRanges(params UnicodeRangeImpl[] ranges)
        {
            if (ranges == null) throw new ArgumentNullException("ranges");
            for (int i = 0; i < ranges.Length; i++) AllowRange(ranges[i]);
        }

        public virtual void Clear() => _bitmap = new AllowedBmpBitmap();

        public virtual void ForbidCharacter(char character) => _bitmap.ForbidChar(character);

        public virtual void ForbidCharacters(params char[] characters)
        {
            if (characters == null) throw new ArgumentNullException("characters");
            for (int i = 0; i < characters.Length; i++) _bitmap.ForbidChar(characters[i]);
        }

        public virtual void ForbidRange(UnicodeRangeImpl range)
        {
            if (range == null) throw new ArgumentNullException("range");
            int first = range.FirstCodePoint;
            for (int i = 0; i < range.Length; i++) _bitmap.ForbidChar(first + i);
        }

        public virtual void ForbidRanges(params UnicodeRangeImpl[] ranges)
        {
            if (ranges == null) throw new ArgumentNullException("ranges");
            for (int i = 0; i < ranges.Length; i++) ForbidRange(ranges[i]);
        }

        public virtual IEnumerable<int> GetAllowedCodePoints()
        {
            List<int> result = new List<int>();
            for (int i = 0; i <= 0xFFFF; i++) if (_bitmap.IsAllowed(i)) result.Add(i);
            return result;
        }
    }

    [Shadows("System.Text.Encodings.Web.TextEncoder")]
    public abstract class TextEncoderImpl
    {
        // The inbox escaping state (OptimizedInboxTextEncoder): allowed BMP code points, and whether
        // '"' may use the short "\"" form (JavaScriptEncoder.UnsafeRelaxedJsonEscaping).
        internal AllowedBmpBitmap _allowed;
        internal bool _minimalEscaping;

        protected TextEncoderImpl() { }

        public virtual int MaxOutputCharactersPerInputCharacter => 6;

        internal bool IsScalarAllowed(int scalar) => scalar <= 0xFFFF && _allowed.IsAllowed(scalar);

        public virtual bool WillEncode(int unicodeScalar)
        {
            if ((uint)unicodeScalar > 0x10FFFF) return true;
            return !IsScalarAllowed(unicodeScalar);
        }

        /// <summary>Appends the escaped form of a scalar that is not allowed.</summary>
        internal void AppendEscaped(StringBuilder sb, int scalar)
        {
            switch (scalar)
            {
                case '\b': sb.Append("\\b"); return;
                case '\t': sb.Append("\\t"); return;
                case '\n': sb.Append("\\n"); return;
                case '\f': sb.Append("\\f"); return;
                case '\r': sb.Append("\\r"); return;
                case '\\': sb.Append("\\\\"); return;
                case '"':
                    if (_minimalEscaping) { sb.Append("\\\""); return; }
                    break;
            }
            if (scalar < 0x10000) AppendHex(sb, scalar);
            else
            {
                int v = scalar - 0x10000;
                AppendHex(sb, 0xD800 + (v >> 10));
                AppendHex(sb, 0xDC00 + (v & 0x3FF));
            }
        }

        internal static void AppendHex(StringBuilder sb, int value)
        {
            sb.Append('\\').Append('u');
            const string digits = "0123456789ABCDEF";
            sb.Append(digits[(value >> 12) & 0xF]).Append(digits[(value >> 8) & 0xF]).Append(digits[(value >> 4) & 0xF]).Append(digits[value & 0xF]);
        }

        public virtual string Encode(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            int first = FindFirstToEncode(value, 0);
            if (first < 0) return value;
            StringBuilder sb = new StringBuilder(value.Length + 16);
            sb.Append(value, 0, first);
            EncodeFrom(sb, value, first);
            return sb.ToString();
        }

        /// <summary>Index of the first char that is escaped, or -1.</summary>
        internal int FindFirstToEncode(string value, int start)
        {
            for (int i = start; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 0xD800 && c <= 0xDFFF) return i; // supplementary or invalid: always encoded
                if (!_allowed.IsAllowed(c)) return i;
            }
            return -1;
        }

        /// <summary>Encodes value[start..] (OptimizedInboxTextEncoder.Encode, isFinalBlock: true).</summary>
        internal void EncodeFrom(StringBuilder sb, string value, int start)
        {
            for (int i = start; i < value.Length; i++)
            {
                int c = value[i];
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (c <= 0xDBFF && i + 1 < value.Length && value[i + 1] >= 0xDC00 && value[i + 1] <= 0xDFFF)
                    {
                        int scalar = ((c - 0xD800) << 10) + (value[i + 1] - 0xDC00) + 0x10000;
                        AppendEscaped(sb, scalar); // supplementary scalars are never allowed
                        i++;
                    }
                    else AppendEscaped(sb, 0xFFFD); // invalid UTF-16: replacement char, always escaped
                    continue;
                }
                if (_allowed.IsAllowed(c)) sb.Append((char)c);
                else AppendEscaped(sb, c);
            }
        }
    }

    [Shadows("System.Text.Encodings.Web.JavaScriptEncoder")]
    public abstract class JavaScriptEncoderImpl : TextEncoderImpl
    {
        private static JavaScriptEncoderImpl s_default;
        private static JavaScriptEncoderImpl s_relaxed;

        public static JavaScriptEncoderImpl Default => s_default ??= new DefaultJavaScriptEncoder(new TextEncoderSettingsImpl(UnicodeRangesImpl.BasicLatin), false);

        public static JavaScriptEncoderImpl UnsafeRelaxedJsonEscaping => s_relaxed ??= new DefaultJavaScriptEncoder(new TextEncoderSettingsImpl(UnicodeRangesImpl.All), true);

        public static JavaScriptEncoderImpl Create(TextEncoderSettingsImpl settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            return new DefaultJavaScriptEncoder(settings, false);
        }

        public static JavaScriptEncoderImpl Create(params UnicodeRangeImpl[] allowedRanges) =>
            new DefaultJavaScriptEncoder(new TextEncoderSettingsImpl(allowedRanges), false);

        internal static bool IsDefault(JavaScriptEncoderImpl encoder) => encoder == null || (object)encoder == (object)s_default;
    }

    internal sealed class DefaultJavaScriptEncoder : JavaScriptEncoderImpl
    {
        internal DefaultJavaScriptEncoder(TextEncoderSettingsImpl settings, bool allowMinimalJsonEscaping)
        {
            _allowed = settings._bitmap.Clone();
            _allowed.ForbidUndefinedCharacters();
            _minimalEscaping = allowMinimalJsonEscaping;
            if (allowMinimalJsonEscaping)
            {
                _allowed.ForbidChar('"');
                _allowed.ForbidChar('\\');
            }
            else
            {
                _allowed.ForbidHtmlCharacters();
                _allowed.ForbidChar('\\');
                _allowed.ForbidChar('`');
            }
        }
    }
}
