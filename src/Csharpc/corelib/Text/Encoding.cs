// System.Text.Encoding: UTF-8, UTF-16 (LE/BE), ASCII and Latin1, written in C#.
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Encoding")]
    public abstract unsafe class EncodingImpl
    {
        private static Encoding s_utf8, s_utf8NoBom, s_ascii, s_unicode, s_bigEndianUnicode, s_latin1;

        public static Encoding UTF8 => s_utf8 ??= new UTF8Encoding(true);
        public static Encoding Default => s_utf8NoBom ??= new UTF8Encoding(false);
        public static Encoding ASCII => s_ascii ??= new ASCIIEncoding();
        public static Encoding Unicode => s_unicode ??= new UnicodeEncoding(false, true);
        public static Encoding BigEndianUnicode => s_bigEndianUnicode ??= new UnicodeEncoding(true, true);
        public static Encoding Latin1 => s_latin1 ??= (Encoding)(object)new Latin1EncodingImpl();

        public static Encoding GetEncoding(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "utf-8": case "utf8": return UTF8;
                case "us-ascii": case "ascii": return ASCII;
                case "utf-16": case "unicode": case "utf-16le": return Unicode;
                case "utf-16be": case "unicodefffe": return BigEndianUnicode;
                case "iso-8859-1": case "latin1": return Latin1;
            }
            throw new ArgumentException("'" + name + "' is not a supported encoding name. (Parameter 'name')");
        }

        public static Encoding GetEncoding(int codepage)
        {
            switch (codepage)
            {
                case 65001: return UTF8;
                case 20127: return ASCII;
                case 1200: return Unicode;
                case 1201: return BigEndianUnicode;
                case 28591: return Latin1;
            }
            throw new NotSupportedException("No data is available for encoding " + codepage + ".");
        }

        // ---- encoding-specific core (per subclass) ----
        internal abstract int ByteCount(char* chars, int count);
        internal abstract int EncodeChars(char* chars, int count, byte* bytes);
        internal abstract int CharCount(byte* bytes, int count);
        internal abstract int DecodeBytes(byte* bytes, int count, char* chars);
        /// <summary>Trailing bytes that start an incomplete sequence (incremental decoding).</summary>
        internal virtual int IncompleteTail(byte* bytes, int count) => 0;
        internal virtual byte[] PreambleBytes => new byte[0];

        public abstract string WebName { get; }
        public virtual string EncodingName => WebName;
        public virtual string BodyName => WebName;
        public abstract int CodePage { get; }
        public virtual bool IsSingleByte => false;

        public virtual byte[] GetPreamble() => PreambleBytes;
        public virtual Encoder GetEncoder() => (Encoder)(object)new EncodingEncoder(this);
        public virtual ReadOnlySpan<byte> Preamble => PreambleBytes;

        public virtual int GetMaxByteCount(int charCount) => (charCount + 1) * 3;
        public virtual int GetMaxCharCount(int byteCount) => byteCount + 1;

        // ---- public API (shared) ----
        public virtual int GetByteCount(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            fixed (char* c = s) return ByteCount(c, s.Length);
        }

        public virtual int GetByteCount(char[] chars)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            fixed (char* c = chars) return ByteCount(c, chars.Length);
        }

        public virtual byte[] GetBytes(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            fixed (char* c = s)
            {
                byte[] result = new byte[ByteCount(c, s.Length)];
                fixed (byte* b = result) EncodeChars(c, s.Length, b);
                return result;
            }
        }

        public virtual byte[] GetBytes(char[] chars)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            return GetBytes(chars, 0, chars.Length);
        }

        public virtual byte[] GetBytes(char[] chars, int index, int count)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            fixed (char* c = chars)
            {
                byte[] result = new byte[ByteCount(c + index, count)];
                fixed (byte* b = result) EncodeChars(c + index, count, b);
                return result;
            }
        }

        public byte[] GetBytes(string s, int index, int count)
        {
            if (s == null) throw new ArgumentNullException("s");
            fixed (char* c = s)
            {
                byte[] result = new byte[ByteCount(c + index, count)];
                fixed (byte* b = result) EncodeChars(c + index, count, b);
                return result;
            }
        }

        public virtual int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            fixed (char* c = s) fixed (byte* b = bytes) return EncodeChars(c + charIndex, charCount, b + byteIndex);
        }

        public virtual int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            fixed (char* c = chars) fixed (byte* b = bytes) return EncodeChars(c + charIndex, charCount, b + byteIndex);
        }

        public virtual int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (char* c = chars) fixed (byte* b = bytes)
            {
                // .NET's fallback buffer check: the whole output must fit.
                if (ByteCount(c, chars.Length) > bytes.Length) throw TooSmall("bytes", "byte");
                return EncodeChars(c, chars.Length, b);
            }
        }

        public virtual int GetByteCount(ReadOnlySpan<char> chars)
        {
            fixed (char* c = chars) return ByteCount(c, chars.Length);
        }

        public virtual int GetCharCount(ReadOnlySpan<byte> bytes)
        {
            fixed (byte* b = bytes) return CharCount(b, bytes.Length);
        }

        public virtual int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
        {
            fixed (byte* b = bytes) fixed (char* c = chars)
            {
                if (CharCount(b, bytes.Length) > chars.Length) throw TooSmall("chars", "char");
                return DecodeBytes(b, bytes.Length, c);
            }
        }

        public virtual bool TryGetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten)
        {
            fixed (char* c = chars) fixed (byte* b = bytes)
            {
                if (ByteCount(c, chars.Length) > bytes.Length) { bytesWritten = 0; return false; }
                bytesWritten = EncodeChars(c, chars.Length, b);
                return true;
            }
        }

        public virtual bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten)
        {
            fixed (byte* b = bytes) fixed (char* c = chars)
            {
                if (CharCount(b, bytes.Length) > chars.Length) { charsWritten = 0; return false; }
                charsWritten = DecodeBytes(b, bytes.Length, c);
                return true;
            }
        }

        private ArgumentException TooSmall(string parameter, string unit) => new ArgumentException(
            "The output " + unit + " buffer is too small to contain the encoded data, encoding codepage '" + CodePage + "' and fallback '"
            + (unit == "byte" ? "System.Text.EncoderReplacementFallback" : "System.Text.DecoderReplacementFallback") + "'.", parameter);

        public virtual int GetCharCount(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            fixed (byte* b = bytes) return CharCount(b, bytes.Length);
        }

        public virtual char[] GetChars(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            fixed (byte* b = bytes)
            {
                char[] result = new char[CharCount(b, bytes.Length)];
                fixed (char* c = result) DecodeBytes(b, bytes.Length, c);
                return result;
            }
        }

        public virtual string GetString(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            return GetString(bytes, 0, bytes.Length);
        }

        public virtual string GetString(byte[] bytes, int index, int count)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (index < 0 || count < 0 || index > bytes.Length - count)
                throw new ArgumentOutOfRangeException("index", "Index and count must refer to a location within the buffer.");
            fixed (byte* b = bytes) return Decode(b + index, count);
        }

        public string GetString(ReadOnlySpan<byte> bytes)
        {
            fixed (byte* b = bytes) return Decode(b, bytes.Length);
        }

        public unsafe string GetString(byte* bytes, int byteCount) => Decode(bytes, byteCount);

        internal string Decode(byte* b, int count)
        {
            if (count == 0) return "";
            int chars = CharCount(b, count);
            char[] buffer = new char[chars];
            fixed (char* c = buffer) DecodeBytes(b, count, c);
            return new string(buffer);
        }

        public override string ToString() => GetType().FullName;
    }

    [Shadows("System.Text.UTF8Encoding")]
    public unsafe class UTF8EncodingImpl : EncodingImpl
    {
        private readonly bool _bom;

        public UTF8EncodingImpl() { }
        public UTF8EncodingImpl(bool encoderShouldEmitUTF8Identifier) { _bom = encoderShouldEmitUTF8Identifier; }
        public UTF8EncodingImpl(bool encoderShouldEmitUTF8Identifier, bool throwOnInvalidBytes) { _bom = encoderShouldEmitUTF8Identifier; }

        public override string WebName => "utf-8";
        public override string EncodingName => "Unicode (UTF-8)";
        public override int CodePage => 65001;
        internal override byte[] PreambleBytes
        {
            get
            {
                if (!_bom) return new byte[0];
                byte[] p = new byte[3]; p[0] = 0xEF; p[1] = 0xBB; p[2] = 0xBF;
                return p;
            }
        }

        internal override int ByteCount(char* chars, int count)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                if (c < 0x80) n += 1;
                else if (c < 0x800) n += 2;
                else if (c >= 0xD800 && c <= 0xDBFF && i + 1 < count && chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF) { n += 4; i++; }
                else n += 3;
            }
            return n;
        }

        internal override int EncodeChars(char* chars, int count, byte* dest)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                uint cp = chars[i];
                if (cp < 0x80) { dest[n++] = (byte)cp; continue; }
                if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < count && chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF)
                {
                    cp = 0x10000 + ((cp - 0xD800) << 10) + ((uint)chars[i + 1] - 0xDC00);
                    i++;
                }
                else if (cp >= 0xD800 && cp <= 0xDFFF) cp = 0xFFFD;
                if (cp < 0x800) { dest[n++] = (byte)(0xC0 | (cp >> 6)); dest[n++] = (byte)(0x80 | (cp & 0x3F)); }
                else if (cp < 0x10000)
                {
                    dest[n++] = (byte)(0xE0 | (cp >> 12)); dest[n++] = (byte)(0x80 | ((cp >> 6) & 0x3F)); dest[n++] = (byte)(0x80 | (cp & 0x3F));
                }
                else
                {
                    dest[n++] = (byte)(0xF0 | (cp >> 18)); dest[n++] = (byte)(0x80 | ((cp >> 12) & 0x3F));
                    dest[n++] = (byte)(0x80 | ((cp >> 6) & 0x3F)); dest[n++] = (byte)(0x80 | (cp & 0x3F));
                }
            }
            return n;
        }

        internal override int CharCount(byte* bytes, int count)
        {
            // Exact count = decode into scratch; sizes are bounded by count chars + surrogates ≤ count.
            int ascii = 0;
            while (ascii < count && bytes[ascii] < 0x80) ascii++;
            if (ascii == count) return count;
            char* scratch = (char*)Libc.Alloc((nuint)((count + 1) * 2));
            int n = Utf8.DecodeInto(bytes, count, scratch);
            Libc.free(scratch);
            return n;
        }

        internal override int DecodeBytes(byte* bytes, int count, char* chars)
        {
            int i = 0;
            while (i < count && bytes[i] < 0x80) { chars[i] = (char)bytes[i]; i++; }
            if (i == count) return count;
            return i + Utf8.DecodeInto(bytes + i, count - i, chars + i);
        }

        internal override int IncompleteTail(byte* bytes, int count)
        {
            // Look back at most 3 bytes for a lead byte whose sequence runs past the end.
            for (int back = 1; back <= 3 && back <= count; back++)
            {
                byte b = bytes[count - back];
                if ((b & 0xC0) == 0x80) continue; // continuation byte
                int length = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 1;
                return length > back ? back : 0;
            }
            return 0;
        }

        public override bool Equals(object value) => value is UTF8EncodingImpl other && other._bom == _bom;
        public override int GetHashCode() => 65001;
    }

    [Shadows("System.Text.UnicodeEncoding")]
    public unsafe class UnicodeEncodingImpl : EncodingImpl
    {
        private readonly bool _bigEndian, _bom;

        public UnicodeEncodingImpl() { _bom = true; }
        public UnicodeEncodingImpl(bool bigEndian, bool byteOrderMark) { _bigEndian = bigEndian; _bom = byteOrderMark; }
        public UnicodeEncodingImpl(bool bigEndian, bool byteOrderMark, bool throwOnInvalidBytes) { _bigEndian = bigEndian; _bom = byteOrderMark; }

        public override string WebName => _bigEndian ? "utf-16BE" : "utf-16";
        public override string EncodingName => _bigEndian ? "Unicode (Big-Endian)" : "Unicode";
        public override int CodePage => _bigEndian ? 1201 : 1200;
        public override int GetMaxByteCount(int charCount) => (charCount + 1) * 2;
        internal override byte[] PreambleBytes
        {
            get
            {
                if (!_bom) return new byte[0];
                byte[] p = new byte[2];
                p[0] = _bigEndian ? (byte)0xFE : (byte)0xFF;
                p[1] = _bigEndian ? (byte)0xFF : (byte)0xFE;
                return p;
            }
        }

        internal override int ByteCount(char* chars, int count) => count * 2;

        internal override int EncodeChars(char* chars, int count, byte* bytes)
        {
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                if (_bigEndian) { bytes[2 * i] = (byte)(c >> 8); bytes[2 * i + 1] = (byte)c; }
                else { bytes[2 * i] = (byte)c; bytes[2 * i + 1] = (byte)(c >> 8); }
            }
            return count * 2;
        }

        internal override int CharCount(byte* bytes, int count) => (count + 1) / 2;

        internal override int DecodeBytes(byte* bytes, int count, char* chars)
        {
            int n = count / 2;
            for (int i = 0; i < n; i++)
                chars[i] = _bigEndian ? (char)((bytes[2 * i] << 8) | bytes[2 * i + 1]) : (char)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
            if ((count & 1) != 0) { chars[n] = '�'; n++; }
            return n;
        }

        internal override int IncompleteTail(byte* bytes, int count) => count & 1;
    }

    [Shadows("System.Text.ASCIIEncoding")]
    public unsafe class ASCIIEncodingImpl : EncodingImpl
    {
        public ASCIIEncodingImpl() { }
        public override string WebName => "us-ascii";
        public override string EncodingName => "US-ASCII";
        public override int CodePage => 20127;
        public override bool IsSingleByte => true;
        // Replacement is per UTF-16 code unit: a surrogate pair becomes "??", as in .NET.
        internal override int ByteCount(char* chars, int count) => count;
        internal override int EncodeChars(char* chars, int count, byte* bytes)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                bytes[n++] = c < 0x80 ? (byte)c : (byte)'?';
            }
            return n;
        }
        internal override int CharCount(byte* bytes, int count) => count;
        internal override int DecodeBytes(byte* bytes, int count, char* chars)
        {
            for (int i = 0; i < count; i++) chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : '?';
            return count;
        }
    }

    /// <summary>Encoding.Latin1 (an internal class in the BCL).</summary>
    internal sealed unsafe class Latin1EncodingImpl : EncodingImpl
    {
        public override string WebName => "iso-8859-1";
        public override string EncodingName => "Western European (ISO)";
        public override int CodePage => 28591;
        public override bool IsSingleByte => true;
        // Replacement is per UTF-16 code unit: a surrogate pair becomes "??", as in .NET.
        internal override int ByteCount(char* chars, int count) => count;
        internal override int EncodeChars(char* chars, int count, byte* bytes)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                bytes[n++] = c <= 0xFF ? (byte)c : (byte)'?';
            }
            return n;
        }
        internal override int CharCount(byte* bytes, int count) => count;
        internal override int DecodeBytes(byte* bytes, int count, char* chars)
        {
            for (int i = 0; i < count; i++) chars[i] = (char)bytes[i];
            return count;
        }
    }
}
