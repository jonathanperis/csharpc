// System.Text.Encodings.Web: TextEncoder / JavaScriptEncoder (the inbox "optimized" encoders),
// TextEncoderSettings and UnicodeRange. Utf8JsonWriter escapes through these.
//
// Ported from dotnet/runtime src/libraries/System.Text.Encodings.Web (release/11.0).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
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
        // UrlEncoder: escapes are the scalar's UTF-8 bytes as %XX (DefaultUrlEncoder.EscaperImplementation).
        internal bool _percentEncoding;
        // HtmlEncoder: the named entities for < > & ", else &#xHEX; (DefaultHtmlEncoder.EscaperImplementation).
        internal bool _htmlEncoding;

        protected TextEncoderImpl() { }

        public virtual int MaxOutputCharactersPerInputCharacter => 6;

        internal bool IsScalarAllowed(int scalar) => scalar <= 0xFFFF && _allowed.IsAllowed(scalar);

        public virtual bool WillEncode(int unicodeScalar)
        {
            // The inbox encoders build a Rune from it: a value that is no Unicode scalar throws, as in .NET.
            if (!Rune.IsValid(unicodeScalar)) throw new ArgumentOutOfRangeException("value");
            return !IsScalarAllowed(unicodeScalar);
        }

        /// <summary>Appends the escaped form of a scalar that is not allowed.</summary>
        internal void AppendEscaped(StringBuilder sb, int scalar)
        {
            if (_percentEncoding)
            {
                AppendPercentEncoded(sb, scalar);
                return;
            }
            if (_htmlEncoding)
            {
                switch (scalar)
                {
                    case '<': sb.Append("&lt;"); return;
                    case '>': sb.Append("&gt;"); return;
                    case '&': sb.Append("&amp;"); return;
                    case '"': sb.Append("&quot;"); return;
                }
                sb.Append("&#x").Append(scalar.ToString("X")).Append(';');
                return;
            }
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

        private static void AppendPercentEncoded(StringBuilder sb, int scalar)
        {
            const string digits = "0123456789ABCDEF";
            Span<byte> utf8 = stackalloc byte[4];
            int n = new Rune(scalar).EncodeToUtf8(utf8);
            for (int i = 0; i < n; i++) sb.Append('%').Append(digits[utf8[i] >> 4]).Append(digits[utf8[i] & 0xF]);
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
            if (_allowed == null) return EncodeThroughScalars(value);
            int first = FindFirstToEncode(value, 0);
            if (first < 0) return value;
            StringBuilder sb = new StringBuilder(value.Length + 16);
            sb.Append(value, 0, first);
            EncodeFrom(sb, value, first);
            return sb.ToString();
        }

        // ---- The rest of TextEncoder's surface (dotnet/runtime TextEncoder.cs): the TextWriter, span and UTF-8
        // members. A derived encoder (Microsoft.AspNetCore.Razor's NullHtmlEncoder) is served through its
        // WillEncode / TryEncodeUnicodeScalar / FindFirstCharacterToEncode, scalar by scalar, as .NET's base does;
        // the inbox encoders answer from their allowed-character bitmap. ------------------------------------

        public virtual unsafe int FindFirstCharacterToEncode(char* text, int textLength)
        {
            for (int i = 0; i < textLength; i++)
            {
                char c = text[i];
                if ((c >= 0xD800 && c <= 0xDFFF) || _allowed == null || !_allowed.IsAllowed(c)) return i;
            }
            return -1;
        }

        public virtual unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
        {
            string text;
            if (!WillEncode(unicodeScalar)) text = char.ConvertFromUtf32(unicodeScalar);
            else
            {
                var sb = new StringBuilder(12);
                AppendEscaped(sb, unicodeScalar);
                text = sb.ToString();
            }
            if (text.Length > bufferLength)
            {
                numberOfCharactersWritten = 0;
                return false;
            }
            for (int i = 0; i < text.Length; i++) buffer[i] = text[i];
            numberOfCharactersWritten = text.Length;
            return true;
        }

        public void Encode(TextWriter output, string value) => Encode(output, value, 0, value?.Length ?? 0);

        public virtual void Encode(TextWriter output, string value, int startIndex, int characterCount)
        {
            if (output == null) throw new ArgumentNullException("output");
            if (value == null) throw new ArgumentNullException("value");
            ValidateRanges(startIndex, characterCount, value.Length);
            EncodeToWriter(output, value.AsSpan(startIndex, characterCount));
        }

        public virtual void Encode(TextWriter output, char[] value, int startIndex, int characterCount)
        {
            if (output == null) throw new ArgumentNullException("output");
            if (value == null) throw new ArgumentNullException("value");
            ValidateRanges(startIndex, characterCount, value.Length);
            EncodeToWriter(output, new ReadOnlySpan<char>(value, startIndex, characterCount));
        }

        private static void ValidateRanges(int startIndex, int characterCount, int actualInputLength)
        {
            if (startIndex < 0 || startIndex > actualInputLength) throw new ArgumentOutOfRangeException("startIndex");
            if (characterCount < 0 || characterCount > actualInputLength - startIndex) throw new ArgumentOutOfRangeException("characterCount");
        }

        private unsafe int FirstToEncode(ReadOnlySpan<char> text)
        {
            fixed (char* p = text) return FindFirstCharacterToEncode(p, text.Length);
        }

        private void EncodeToWriter(TextWriter output, ReadOnlySpan<char> value)
        {
            int first = FirstToEncode(value);
            if (first < 0)
            {
                output.Write(value);
                return;
            }
            output.Write(value.Slice(0, first));
            char[] buffer = new char[Math.Max(16, 2 * MaxOutputCharactersPerInputCharacter)];
            for (int i = first; i < value.Length;)
            {
                int scalar = NextScalar(value, i, out int length);
                i += length;
                output.Write(new ReadOnlySpan<char>(buffer, 0, EncodeScalar(scalar, buffer)));
            }
        }

        // The scalar at value[i] (U+FFFD for an unpaired surrogate) and the chars it takes.
        private static int NextScalar(ReadOnlySpan<char> value, int i, out int length)
        {
            char c = value[i];
            length = 1;
            if (c < 0xD800 || c > 0xDFFF) return c;
            if (c <= 0xDBFF && i + 1 < value.Length && value[i + 1] >= 0xDC00 && value[i + 1] <= 0xDFFF)
            {
                length = 2;
                return ((c - 0xD800) << 10) + (value[i + 1] - 0xDC00) + 0x10000;
            }
            return 0xFFFD;
        }

        // A scalar as the encoder writes it: itself, or its escape (an encoder that can't escape it into a buffer
        // that large has no valid answer, as .NET reports).
        private unsafe int EncodeScalar(int scalar, char[] buffer)
        {
            if (!WillEncode(scalar))
            {
                if (scalar < 0x10000) { buffer[0] = (char)scalar; return 1; }
                buffer[0] = (char)(0xD800 + ((scalar - 0x10000) >> 10));
                buffer[1] = (char)(0xDC00 + ((scalar - 0x10000) & 0x3FF));
                return 2;
            }
            fixed (char* p = buffer)
                if (TryEncodeUnicodeScalar(scalar, p, buffer.Length, out int written)) return written;
            throw new ArgumentException("Argument encoder does not implement MaxOutputCharsPerInputChar correctly.");
        }

        private string EncodeThroughScalars(string value)
        {
            int first = FirstToEncode(value);
            if (first < 0) return value;
            var writer = new StringWriter();
            EncodeToWriter(writer, value);
            return writer.ToString();
        }

        public virtual OperationStatus Encode(ReadOnlySpan<char> source, Span<char> destination, out int charsConsumed, out int charsWritten, bool isFinalBlock = true)
        {
            char[] buffer = new char[Math.Max(16, 2 * MaxOutputCharactersPerInputCharacter)];
            int read = 0, written = 0;
            while (read < source.Length)
            {
                char c = source[read];
                if (c >= 0xD800 && c <= 0xDBFF && read + 1 == source.Length && !isFinalBlock)
                {
                    charsConsumed = read;
                    charsWritten = written;
                    return OperationStatus.NeedMoreData;
                }
                int scalar = NextScalar(source, read, out int length);
                int n = EncodeScalar(scalar, buffer);
                if (n > destination.Length - written)
                {
                    charsConsumed = read;
                    charsWritten = written;
                    return OperationStatus.DestinationTooSmall;
                }
                new ReadOnlySpan<char>(buffer, 0, n).CopyTo(destination.Slice(written));
                written += n;
                read += length;
            }
            charsConsumed = read;
            charsWritten = written;
            return OperationStatus.Done;
        }

        public virtual OperationStatus EncodeUtf8(ReadOnlySpan<byte> utf8Source, Span<byte> utf8Destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
        {
            if (_allowed != null) return EncodeUtf8Inbox(utf8Source, utf8Destination, out bytesConsumed, out bytesWritten, isFinalBlock);
            char[] buffer = new char[Math.Max(16, 2 * MaxOutputCharactersPerInputCharacter)];
            Span<byte> encoded = stackalloc byte[4];
            int read = 0, written = 0;
            while (read < utf8Source.Length)
            {
                byte b = utf8Source[read];
                // ASCII the encoder lets through: copied as is.
                if (b < 0x80 && !WillEncode(b))
                {
                    if (written == utf8Destination.Length)
                    {
                        bytesConsumed = read;
                        bytesWritten = written;
                        return OperationStatus.DestinationTooSmall;
                    }
                    utf8Destination[written++] = b;
                    read++;
                    continue;
                }
                OperationStatus status = Rune.DecodeFromUtf8(utf8Source.Slice(read), out Rune rune, out int length);
                if (status == OperationStatus.NeedMoreData && !isFinalBlock)
                {
                    bytesConsumed = read;
                    bytesWritten = written;
                    return OperationStatus.NeedMoreData;
                }
                int scalar = status == OperationStatus.Done ? rune.Value : 0xFFFD;
                int needed;
                if (!WillEncode(scalar))
                {
                    if (status == OperationStatus.Done)
                    {
                        needed = length;
                        if (needed > utf8Destination.Length - written)
                        {
                            bytesConsumed = read;
                            bytesWritten = written;
                            return OperationStatus.DestinationTooSmall;
                        }
                        utf8Source.Slice(read, length).CopyTo(utf8Destination.Slice(written));
                    }
                    else
                    {
                        needed = new Rune(0xFFFD).EncodeToUtf8(encoded);
                        if (needed > utf8Destination.Length - written)
                        {
                            bytesConsumed = read;
                            bytesWritten = written;
                            return OperationStatus.DestinationTooSmall;
                        }
                        encoded.Slice(0, needed).CopyTo(utf8Destination.Slice(written));
                    }
                }
                else
                {
                    int chars = EncodeScalar(scalar, buffer);
                    needed = Encoding.UTF8.GetByteCount(new ReadOnlySpan<char>(buffer, 0, chars));
                    if (needed > utf8Destination.Length - written)
                    {
                        bytesConsumed = read;
                        bytesWritten = written;
                        return OperationStatus.DestinationTooSmall;
                    }
                    Encoding.UTF8.GetBytes(new ReadOnlySpan<char>(buffer, 0, chars), utf8Destination.Slice(written));
                }
                written += needed;
                read += length;
            }
            bytesConsumed = read;
            bytesWritten = written;
            return OperationStatus.Done;
        }

        public virtual int FindFirstCharacterToEncodeUtf8(ReadOnlySpan<byte> utf8Text)
        {
            if (_allowed != null)
            {
                int run = AllowedAsciiRun(utf8Text);
                return run == utf8Text.Length ? -1 : FirstToEncodeUtf8Inbox(utf8Text, run);
            }
            int i = 0;
            while (i < utf8Text.Length)
            {
                if (utf8Text[i] < 0x80)
                {
                    if (WillEncode(utf8Text[i])) return i;
                    i++;
                    continue;
                }
                if (Rune.DecodeFromUtf8(utf8Text.Slice(i), out Rune rune, out int length) != OperationStatus.Done || WillEncode(rune.Value)) return i;
                i += length;
            }
            return -1;
        }

        // ---- The inbox encoders' UTF-8 paths (OptimizedInboxTextEncoder): a run of allowed ASCII is found with one
        // bitmap test per byte and copied as a block; only escapes and non-ASCII scalars go one at a time, written
        // straight as UTF-8 (escapes are ASCII). Output and the progress reported on a full destination are the
        // generic path's above (which derived encoders take). RazorSlices writes every HTML-encoded value this way. --

        /// <summary>The length of the leading run of ASCII bytes the encoder lets through.</summary>
        private int AllowedAsciiRun(ReadOnlySpan<byte> text)
        {
            uint[] bits = _allowed.Bits;
            ulong low = bits[0] | ((ulong)bits[1] << 32), high = bits[2] | ((ulong)bits[3] << 32);
            int i = 0;
            while (i < text.Length)
            {
                uint b = text[i];
                ulong word = b < 64 ? low : high;
                if (b >= 0x80 || ((word >> (int)(b & 63)) & 1) == 0) break;
                i++;
            }
            return i;
        }

        /// <summary>FindFirstCharacterToEncodeUtf8 from <paramref name="i"/>, a byte that isn't allowed ASCII.</summary>
        private int FirstToEncodeUtf8Inbox(ReadOnlySpan<byte> text, int i)
        {
            while (i < text.Length)
            {
                if (text[i] < 0x80) return i;
                if (Rune.DecodeFromUtf8(text.Slice(i), out Rune rune, out int length) != OperationStatus.Done || !IsScalarAllowed(rune.Value)) return i;
                i += length;
                i += AllowedAsciiRun(text.Slice(i));
            }
            return -1;
        }

        private OperationStatus EncodeUtf8Inbox(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock)
        {
            Span<byte> escape = stackalloc byte[32];
            int read = 0, written = 0;
            OperationStatus result = OperationStatus.Done;
            while (read < source.Length)
            {
                int run = AllowedAsciiRun(source.Slice(read));
                if (run > 0)
                {
                    int room = destination.Length - written;
                    if (run > room)
                    {
                        source.Slice(read, room).CopyTo(destination.Slice(written));
                        read += room;
                        written += room;
                        result = OperationStatus.DestinationTooSmall;
                        break;
                    }
                    source.Slice(read, run).CopyTo(destination.Slice(written));
                    read += run;
                    written += run;
                    if (read == source.Length) break;
                }
                int length;
                scoped ReadOnlySpan<byte> output;
                byte b = source[read];
                if (b < 0x80)
                {
                    length = 1;
                    output = escape.Slice(0, EscapeUtf8(b, escape));
                }
                else
                {
                    OperationStatus status = Rune.DecodeFromUtf8(source.Slice(read), out Rune rune, out length);
                    if (status == OperationStatus.NeedMoreData && !isFinalBlock)
                    {
                        result = OperationStatus.NeedMoreData;
                        break;
                    }
                    // Invalid UTF-8 is U+FFFD, always escaped (even by an encoder that allows U+FFFD itself).
                    if (status == OperationStatus.Done && IsScalarAllowed(rune.Value)) output = source.Slice(read, length);
                    else output = escape.Slice(0, EscapeUtf8(status == OperationStatus.Done ? rune.Value : 0xFFFD, escape));
                }
                if (output.Length > destination.Length - written)
                {
                    result = OperationStatus.DestinationTooSmall;
                    break;
                }
                output.CopyTo(destination.Slice(written));
                written += output.Length;
                read += length;
            }
            bytesConsumed = read;
            bytesWritten = written;
            return result;
        }

        /// <summary>A disallowed scalar's escape as UTF-8 (ASCII): AppendEscaped's text.</summary>
        private int EscapeUtf8(int scalar, Span<byte> destination)
        {
            const string digits = "0123456789ABCDEF";
            int n = 0;
            if (_percentEncoding)
            {
                Span<byte> utf8 = stackalloc byte[4];
                int count = new Rune(scalar).EncodeToUtf8(utf8);
                for (int i = 0; i < count; i++)
                {
                    destination[n++] = (byte)'%';
                    destination[n++] = (byte)digits[utf8[i] >> 4];
                    destination[n++] = (byte)digits[utf8[i] & 0xF];
                }
                return n;
            }
            if (_htmlEncoding)
            {
                ReadOnlySpan<byte> named = scalar switch { '<' => "&lt;"u8, '>' => "&gt;"u8, '&' => "&amp;"u8, '"' => "&quot;"u8, _ => default };
                if (!named.IsEmpty)
                {
                    named.CopyTo(destination);
                    return named.Length;
                }
                destination[n++] = (byte)'&';
                destination[n++] = (byte)'#';
                destination[n++] = (byte)'x';
                int shift = 20;
                while (shift > 0 && (scalar >> shift) == 0) shift -= 4; // scalar.ToString("X"): no leading zeros
                for (; shift >= 0; shift -= 4) destination[n++] = (byte)digits[(scalar >> shift) & 0xF];
                destination[n++] = (byte)';';
                return n;
            }
            byte simple = scalar switch { '\b' => (byte)'b', '\t' => (byte)'t', '\n' => (byte)'n', '\f' => (byte)'f', '\r' => (byte)'r', '\\' => (byte)'\\', '"' when _minimalEscaping => (byte)'"', _ => 0 };
            if (simple != 0)
            {
                destination[0] = (byte)'\\';
                destination[1] = simple;
                return 2;
            }
            if (scalar < 0x10000) return EscapeUtf16Unit(scalar, destination);
            int v = scalar - 0x10000;
            n = EscapeUtf16Unit(0xD800 + (v >> 10), destination);
            return n + EscapeUtf16Unit(0xDC00 + (v & 0x3FF), destination.Slice(n));
        }

        private static int EscapeUtf16Unit(int unit, Span<byte> destination)
        {
            const string digits = "0123456789ABCDEF";
            destination[0] = (byte)'\\';
            destination[1] = (byte)'u';
            destination[2] = (byte)digits[(unit >> 12) & 0xF];
            destination[3] = (byte)digits[(unit >> 8) & 0xF];
            destination[4] = (byte)digits[(unit >> 4) & 0xF];
            destination[5] = (byte)digits[unit & 0xF];
            return 6;
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

namespace CsharpcCoreLib
{
    using System;

    [Shadows("System.Text.Encodings.Web.HtmlEncoder")]
    public abstract class HtmlEncoderImpl : TextEncoderImpl
    {
        private static HtmlEncoderImpl s_default;

        public static HtmlEncoderImpl Default => s_default ??= new DefaultHtmlEncoder(new TextEncoderSettingsImpl(UnicodeRangesImpl.BasicLatin));

        public static HtmlEncoderImpl Create(TextEncoderSettingsImpl settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            return new DefaultHtmlEncoder(settings);
        }

        public static HtmlEncoderImpl Create(params UnicodeRangeImpl[] allowedRanges) => new DefaultHtmlEncoder(new TextEncoderSettingsImpl(allowedRanges));
    }

    internal sealed class DefaultHtmlEncoder : HtmlEncoderImpl
    {
        internal DefaultHtmlEncoder(TextEncoderSettingsImpl settings)
        {
            _allowed = settings._bitmap.Clone();
            _allowed.ForbidUndefinedCharacters();
            _allowed.ForbidHtmlCharacters();
            _htmlEncoding = true;
        }

        public override int MaxOutputCharactersPerInputCharacter => 10;
    }
}
