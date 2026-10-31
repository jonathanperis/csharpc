// System.Text.Encoding with UTF-8, UTF-16 (LE/BE), ASCII and Latin1, in C#.
//
// The base class is .NET's (dotnet/runtime release/11.0 Encoding.cs): fallbacks, Clone/IsReadOnly, the
// virtual members implemented over the abstract array overloads, DefaultEncoder/DefaultDecoder — so an
// encoding derived elsewhere (System.Private.Xml's Ucs4Encoding) behaves as on .NET. The built-in
// encodings are one codec each (Codec below: a scalar at a time, with .NET's replacement/exception/custom
// fallbacks and UTF-8's maximal-subpart rule for invalid bytes) behind the argument checks of .NET's
// UTF8Encoding/UnicodeEncoding/ASCIIEncoding/Latin1Encoding, and stateful decoders/encoders (DecoderNLS /
// EncoderNLS: leftover bytes and a trailing high surrogate carry over between calls).
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Encoding")]
    public abstract unsafe class EncodingImpl : ICloneable
    {
        private static Encoding s_utf8, s_utf8NoBom, s_ascii, s_unicode, s_bigEndianUnicode, s_latin1;

        public static Encoding UTF8 => s_utf8 ??= new UTF8Encoding(true);
        public static Encoding Default => s_utf8NoBom ??= new UTF8Encoding(false);
        public static Encoding ASCII => s_ascii ??= new ASCIIEncoding();
        public static Encoding Unicode => s_unicode ??= new UnicodeEncoding(false, true);
        public static Encoding BigEndianUnicode => s_bigEndianUnicode ??= new UnicodeEncoding(true, true);
        public static Encoding Latin1 => s_latin1 ??= (Encoding)(object)new Latin1EncodingImpl();

        // ---- the codec of a built-in encoding (0: none — a derived encoding) ----
        internal const int CodecUtf8 = 1, CodecUtf16LE = 2, CodecUtf16BE = 3, CodecAscii = 4, CodecLatin1 = 5;
        internal int _codec;

        internal int _codePage;
        private bool _isReadOnly = true;
        internal EncoderFallback encoderFallback;
        internal DecoderFallback decoderFallback;

        protected EncodingImpl() : this(0) { }

        protected EncodingImpl(int codePage)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(codePage);
            _codePage = codePage;
            SetDefaultFallbacks();
        }

        protected EncodingImpl(int codePage, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(codePage);
            _codePage = codePage;
            this.encoderFallback = encoderFallback ?? EncoderFallback.ReplacementFallback;
            this.decoderFallback = decoderFallback ?? DecoderFallback.ReplacementFallback;
        }

        internal virtual void SetDefaultFallbacks()
        {
            encoderFallback = EncoderFallback.ReplacementFallback;
            decoderFallback = DecoderFallback.ReplacementFallback;
        }

        // ---- GetEncoding (EncodingTable's names and code pages, for the encodings csharpc has) ----

        private const string NoCodepageData = "No data is available for encoding {0}. For information on defining a custom encoding, see the documentation for the Encoding.RegisterProvider method.";

        // Registered providers come first, as in .NET (CodePagesEncodingProvider, which Roslyn registers).
        public static void RegisterProvider(EncodingProvider provider) => EncodingProviderImpl.AddProvider(provider);

        public static Encoding GetEncoding(int codepage)
        {
            if (EncodingProviderImpl.GetEncodingFromProvider(codepage) is { } provided && provided.CodePage != 65000) return provided;
            switch (codepage)
            {
                case 0: return Default;
                case 1200: return Unicode;
                case 1201: return BigEndianUnicode;
                case 65001: return UTF8;
                case 20127: return ASCII;
                case 28591: return Latin1;
                case 1: case 2: case 3: case 42:
                    throw new ArgumentException(string.Format("{0} is not a supported code page.", codepage), "codepage");
                case 65000:
                    throw new NotSupportedException("Support for UTF-7 is disabled. See https://aka.ms/dotnet-warnings/SYSLIB0001 for more information.");
                case 12000: case 12001:
                    throw new NotSupportedException("not supported by csharpc yet: UTF-32 encodings");
            }
            if (codepage < 0 || codepage > 65535)
                throw new ArgumentOutOfRangeException("codepage", string.Format("Valid values are between {0} and {1}, inclusive.", 0, 65535));
            throw new NotSupportedException(string.Format(NoCodepageData, codepage));
        }

        public static Encoding GetEncoding(int codepage, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
        {
            if (EncodingProviderImpl.GetEncodingFromProvider(codepage, encoderFallback, decoderFallback) is { } provided && provided.CodePage != 65000)
                return provided;
            Encoding fallbackEncoding = (Encoding)GetEncoding(codepage).Clone();
            fallbackEncoding.EncoderFallback = encoderFallback;
            fallbackEncoding.DecoderFallback = decoderFallback;
            return fallbackEncoding;
        }

        public static Encoding GetEncoding(string name)
        {
            if (EncodingProviderImpl.GetEncodingFromProvider(name) is { } provided && provided.CodePage != 65000) return provided;
            return GetEncoding(CodePageFromName(name));
        }

        public static Encoding GetEncoding(string name, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
        {
            if (EncodingProviderImpl.GetEncodingFromProvider(name, encoderFallback, decoderFallback) is { } provided && provided.CodePage != 65000)
                return provided;
            return GetEncoding(CodePageFromName(name), encoderFallback, decoderFallback);
        }

        private static int CodePageFromName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            switch (name.ToLowerInvariant())
            {
                case "ansi_x3.4-1968": case "ansi_x3.4-1986": case "ascii": case "cp367": case "csascii": case "ibm367":
                case "iso-ir-6": case "iso646-us": case "iso_646.irv:1991": case "us": case "us-ascii":
                    return 20127;
                case "cp819": case "csisolatin1": case "ibm819": case "iso-8859-1": case "iso-ir-100": case "iso8859-1":
                case "iso_8859-1": case "iso_8859-1:1987": case "l1": case "latin1":
                    return 28591;
                case "iso-10646-ucs-2": case "ucs-2": case "unicode": case "utf-16": case "utf-16le":
                    return 1200;
                case "unicodefffe": case "utf-16be":
                    return 1201;
                case "utf-32": case "utf-32le": return 12000;
                case "utf-32be": return 12001;
                case "csunicode11utf7": case "unicode-1-1-utf-7": case "unicode-2-0-utf-7": case "utf-7":
                case "x-unicode-1-1-utf-7": case "x-unicode-2-0-utf-7":
                    return 65000;
                case "unicode-1-1-utf-8": case "unicode-2-0-utf-8": case "utf-8": case "x-unicode-1-1-utf-8": case "x-unicode-2-0-utf-8":
                    return 65001;
            }
            throw new ArgumentException(string.Format("'{0}' is not a supported encoding name. For information on defining a custom encoding, see the documentation for the Encoding.RegisterProvider method.", name), "name");
        }

        public static byte[] Convert(Encoding srcEncoding, Encoding dstEncoding, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return Convert(srcEncoding, dstEncoding, bytes, 0, bytes.Length);
        }

        public static byte[] Convert(Encoding srcEncoding, Encoding dstEncoding, byte[] bytes, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(srcEncoding);
            ArgumentNullException.ThrowIfNull(dstEncoding);
            ArgumentNullException.ThrowIfNull(bytes);
            return dstEncoding.GetBytes(srcEncoding.GetChars(bytes, index, count));
        }

        // ---- names (EncodingTable's data item of the code page) ----

        private string[] DataItem()
        {
            switch (_codePage)
            {
                case 65001: return ["utf-8", "Unicode (UTF-8)"];
                case 1200: return ["utf-16", "Unicode"];
                case 1201: return ["utf-16BE", "Unicode (Big-Endian)"];
                case 20127: return ["us-ascii", "US-ASCII"];
                case 28591: return ["iso-8859-1", "Western European (ISO)"];
            }
            throw new NotSupportedException(string.Format(NoCodepageData, _codePage));
        }

        public virtual string BodyName => DataItem()[0];
        public virtual string EncodingName => DataItem()[1];
        public virtual string HeaderName => DataItem()[0];
        public virtual string WebName => DataItem()[0];
        public virtual int CodePage => _codePage;
        public virtual bool IsSingleByte => false;

        // EncodingTable's MIME flags for the code pages csharpc has (DataItem() rejects the others).
        public virtual bool IsBrowserDisplay { get { DataItem(); return _codePage is 65001 or 28591; } }
        public virtual bool IsBrowserSave { get { DataItem(); return _codePage is 65001 or 1200 or 28591; } }
        public virtual bool IsMailNewsDisplay { get { DataItem(); return _codePage is 65001 or 20127 or 28591; } }
        public virtual bool IsMailNewsSave { get { DataItem(); return _codePage is 65001 or 20127 or 28591; } }

        /// <summary>EncodingTable's UI family code page: 1200 for the Unicode encodings, 1252 for ASCII and Latin-1.</summary>
        public virtual int WindowsCodePage
        {
            get
            {
                DataItem(); // unknown code pages: .NET's NotSupportedException
                return _codePage is 20127 or 28591 ? 1252 : 1200;
            }
        }

        public virtual byte[] GetPreamble() => [];
        public virtual ReadOnlySpan<byte> Preamble => GetPreamble();

        // ---- fallbacks, cloning ----

        public EncoderFallback EncoderFallback
        {
            get => encoderFallback;
            set
            {
                if (IsReadOnly) throw new InvalidOperationException("Instance is read-only.");
                ArgumentNullException.ThrowIfNull(value);
                encoderFallback = value;
            }
        }

        public DecoderFallback DecoderFallback
        {
            get => decoderFallback;
            set
            {
                if (IsReadOnly) throw new InvalidOperationException("Instance is read-only.");
                ArgumentNullException.ThrowIfNull(value);
                decoderFallback = value;
            }
        }

        public virtual object Clone()
        {
            EncodingImpl newEncoding = (EncodingImpl)MemberwiseClone();
            newEncoding._isReadOnly = false;
            return newEncoding;
        }

        public bool IsReadOnly => _isReadOnly;

        public override bool Equals(object value) =>
            value is EncodingImpl that && _codePage == that._codePage
            && EncoderFallback.Equals(that.EncoderFallback) && DecoderFallback.Equals(that.DecoderFallback);

        public override int GetHashCode() => _codePage + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode();

        public override string ToString() => GetType().FullName;

        // ---- argument checks of the built-in encodings (UTF8Encoding & co., ThrowHelper's messages) ----

        private const string NeedNonNegNum = "Non-negative number required.";
        private const string IndexCountBuffer = "Index and count must refer to a location within the buffer.";
        private const string IndexCountString = "Index and count must refer to a location within the string.";
        private const string IndexMustBeLessOrEqual = "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        private const string ArrayNull = "Array cannot be null.";

        private static void CheckArray(object array, string name)
        {
            if (array == null) throw new ArgumentNullException(name, ArrayNull);
        }

        private static void CheckRange(int length, int index, int count, string indexName, string countName, string arrayName, string message)
        {
            if ((index | count) < 0) throw new ArgumentOutOfRangeException(index < 0 ? indexName : countName, NeedNonNegNum);
            if (length - index < count) throw new ArgumentOutOfRangeException(arrayName, message);
        }

        // ---- encoding: chars -> bytes ----

        public virtual int GetByteCount(char[] chars)
        {
            ArgumentNullException.ThrowIfNull(chars);
            return GetByteCount(chars, 0, chars.Length);
        }

        public virtual int GetByteCount(string s)
        {
            if (_codec != 0)
            {
                if (s == null) throw new ArgumentNullException("chars");
                return Codec.ByteCount(this, s);
            }
            if (s == null) throw new ArgumentNullException("s");
            char[] chars = s.ToCharArray();
            return GetByteCount(chars, 0, chars.Length);
        }

        /// <summary>Abstract in .NET (every derived encoding overrides it); the built-in encodings' implementation.</summary>
        public virtual int GetByteCount(char[] chars, int index, int count)
        {
            CheckArray(chars, "chars");
            CheckRange(chars.Length, index, count, "index", "count", "chars", IndexCountBuffer);
            return Codec.ByteCount(this, new ReadOnlySpan<char>(chars, index, count));
        }

        public int GetByteCount(string s, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(s);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, s.Length - count);
            if (_codec != 0) return Codec.ByteCount(this, s.AsSpan(index, count));
            return GetByteCount(s.ToCharArray(index, count), 0, count);
        }

        public virtual int GetByteCount(char* chars, int count)
        {
            if (_codec != 0)
            {
                if (chars == null) throw new ArgumentNullException("chars");
                if (count < 0) throw new ArgumentOutOfRangeException("count", NeedNonNegNum);
                return Codec.ByteCount(this, new ReadOnlySpan<char>(chars, count));
            }
            if (chars == null) throw new ArgumentNullException("chars");
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            char[] arrChar = new ReadOnlySpan<char>(chars, count).ToArray();
            return GetByteCount(arrChar, 0, count);
        }

        public virtual int GetByteCount(ReadOnlySpan<char> chars) =>
            _codec != 0 ? Codec.ByteCount(this, chars) : GetByteCount(chars.ToArray(), 0, chars.Length);

        public virtual byte[] GetBytes(char[] chars)
        {
            ArgumentNullException.ThrowIfNull(chars);
            return GetBytes(chars, 0, chars.Length);
        }

        public virtual byte[] GetBytes(char[] chars, int index, int count)
        {
            byte[] result = new byte[GetByteCount(chars, index, count)];
            GetBytes(chars, index, count, result, 0);
            return result;
        }

        /// <summary>Abstract in .NET; the built-in encodings' implementation.</summary>
        public virtual int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            if (chars == null || bytes == null) throw new ArgumentNullException(chars == null ? "chars" : "bytes", ArrayNull);
            CheckRange(chars.Length, charIndex, charCount, "charIndex", "charCount", "chars", IndexCountString);
            if ((uint)byteIndex > bytes.Length) throw new ArgumentOutOfRangeException("byteIndex", IndexMustBeLessOrEqual);
            return Codec.Bytes(this, new ReadOnlySpan<char>(chars, charIndex, charCount), new Span<byte>(bytes, byteIndex, bytes.Length - byteIndex));
        }

        public virtual byte[] GetBytes(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            int byteCount = GetByteCount(s);
            byte[] bytes = new byte[byteCount];
            GetBytes(s, 0, s.Length, bytes, 0);
            return bytes;
        }

        public byte[] GetBytes(string s, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(s);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, s.Length - count);
            if (_codec == 0) return GetBytes(s.ToCharArray(index, count), 0, count);
            int byteCount = Codec.ByteCount(this, s.AsSpan(index, count));
            if (byteCount == 0) return [];
            byte[] bytes = new byte[byteCount];
            Codec.Bytes(this, s.AsSpan(index, count), bytes);
            return bytes;
        }

        public virtual int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            if (_codec != 0)
            {
                if (s == null || bytes == null) throw new ArgumentNullException(s == null ? "s" : "bytes", ArrayNull);
                CheckRange(s.Length, charIndex, charCount, "charIndex", "charCount", "s", IndexCountString);
                if ((uint)byteIndex > bytes.Length) throw new ArgumentOutOfRangeException("byteIndex", IndexMustBeLessOrEqual);
                return Codec.Bytes(this, s.AsSpan(charIndex, charCount), new Span<byte>(bytes, byteIndex, bytes.Length - byteIndex));
            }
            if (s == null) throw new ArgumentNullException("s");
            return GetBytes(s.ToCharArray(), charIndex, charCount, bytes, byteIndex);
        }

        public virtual int GetBytes(char* chars, int charCount, byte* bytes, int byteCount)
        {
            if (_codec != 0)
            {
                if (chars == null || bytes == null) throw new ArgumentNullException(chars == null ? "chars" : "bytes", ArrayNull);
                if ((charCount | byteCount) < 0) throw new ArgumentOutOfRangeException(charCount < 0 ? "charCount" : "byteCount", NeedNonNegNum);
                return Codec.Bytes(this, new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount));
            }
            if (chars == null) throw new ArgumentNullException("chars");
            if (bytes == null) throw new ArgumentNullException("bytes");
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            char[] arrChar = new ReadOnlySpan<char>(chars, charCount).ToArray();
            byte[] arrByte = new byte[byteCount];
            int result = GetBytes(arrChar, 0, charCount, arrByte, 0);
            if (result < byteCount) byteCount = result;
            new ReadOnlySpan<byte>(arrByte, 0, byteCount).CopyTo(new Span<byte>(bytes, byteCount));
            return byteCount;
        }

        public virtual int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            if (_codec != 0) return Codec.Bytes(this, chars, bytes);
            byte[] arrByte = new byte[bytes.Length];
            int result = GetBytes(chars.ToArray(), 0, chars.Length, arrByte, 0);
            if (result > bytes.Length) result = bytes.Length;
            new ReadOnlySpan<byte>(arrByte, 0, result).CopyTo(bytes);
            return result;
        }

        public virtual bool TryGetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten)
        {
            int required = GetByteCount(chars);
            if (required <= bytes.Length)
            {
                bytesWritten = GetBytes(chars, bytes);
                return true;
            }
            bytesWritten = 0;
            return false;
        }

        // ---- decoding: bytes -> chars ----

        public virtual int GetCharCount(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return GetCharCount(bytes, 0, bytes.Length);
        }

        /// <summary>Abstract in .NET; the built-in encodings' implementation.</summary>
        public virtual int GetCharCount(byte[] bytes, int index, int count)
        {
            CheckArray(bytes, "bytes");
            CheckRange(bytes.Length, index, count, "index", "count", "bytes", IndexCountBuffer);
            return Codec.CharCount(this, new ReadOnlySpan<byte>(bytes, index, count));
        }

        public virtual int GetCharCount(byte* bytes, int count)
        {
            if (_codec != 0)
            {
                if (bytes == null) throw new ArgumentNullException("bytes", ArrayNull);
                if (count < 0) throw new ArgumentOutOfRangeException("count", NeedNonNegNum);
                return Codec.CharCount(this, new ReadOnlySpan<byte>(bytes, count));
            }
            if (bytes == null) throw new ArgumentNullException("bytes");
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            byte[] arrByte = new ReadOnlySpan<byte>(bytes, count).ToArray();
            return GetCharCount(arrByte, 0, count);
        }

        public virtual int GetCharCount(ReadOnlySpan<byte> bytes) =>
            _codec != 0 ? Codec.CharCount(this, bytes) : GetCharCount(bytes.ToArray(), 0, bytes.Length);

        public virtual char[] GetChars(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return GetChars(bytes, 0, bytes.Length);
        }

        public virtual char[] GetChars(byte[] bytes, int index, int count)
        {
            char[] result = new char[GetCharCount(bytes, index, count)];
            GetChars(bytes, index, count, result, 0);
            return result;
        }

        /// <summary>Abstract in .NET; the built-in encodings' implementation.</summary>
        public virtual int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            if (bytes == null || chars == null) throw new ArgumentNullException(bytes == null ? "bytes" : "chars", ArrayNull);
            CheckRange(bytes.Length, byteIndex, byteCount, "byteIndex", "byteCount", "bytes", IndexCountBuffer);
            if ((uint)charIndex > (uint)chars.Length) throw new ArgumentOutOfRangeException("charIndex", IndexMustBeLessOrEqual);
            return Codec.Chars(this, new ReadOnlySpan<byte>(bytes, byteIndex, byteCount), new Span<char>(chars, charIndex, chars.Length - charIndex));
        }

        public virtual int GetChars(byte* bytes, int byteCount, char* chars, int charCount)
        {
            if (_codec != 0)
            {
                if (bytes == null || chars == null) throw new ArgumentNullException(bytes == null ? "bytes" : "chars", ArrayNull);
                if ((byteCount | charCount) < 0) throw new ArgumentOutOfRangeException(byteCount < 0 ? "byteCount" : "charCount", NeedNonNegNum);
                return Codec.Chars(this, new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount));
            }
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (chars == null) throw new ArgumentNullException("chars");
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            byte[] arrByte = new ReadOnlySpan<byte>(bytes, byteCount).ToArray();
            char[] arrChar = new char[charCount];
            int result = GetChars(arrByte, 0, byteCount, arrChar, 0);
            if (result < charCount) charCount = result;
            new ReadOnlySpan<char>(arrChar, 0, charCount).CopyTo(new Span<char>(chars, charCount));
            return charCount;
        }

        public virtual int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
        {
            if (_codec != 0) return Codec.Chars(this, bytes, chars);
            char[] arrChar = new char[chars.Length];
            int result = GetChars(bytes.ToArray(), 0, bytes.Length, arrChar, 0);
            if (result > chars.Length) result = chars.Length;
            new ReadOnlySpan<char>(arrChar, 0, result).CopyTo(chars);
            return result;
        }

        public virtual bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten)
        {
            int required = GetCharCount(bytes);
            if (required <= chars.Length)
            {
                charsWritten = GetChars(bytes, chars);
                return true;
            }
            charsWritten = 0;
            return false;
        }

        public virtual string GetString(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return GetString(bytes, 0, bytes.Length);
        }

        public virtual string GetString(byte[] bytes, int index, int count)
        {
            if (_codec != 0)
            {
                CheckArray(bytes, "bytes");
                CheckRange(bytes.Length, index, count, "index", "count", "bytes", IndexCountBuffer);
                if (count == 0) return string.Empty;
                return Codec.String(this, new ReadOnlySpan<byte>(bytes, index, count));
            }
            return new string(GetChars(bytes, index, count));
        }

        public string GetString(byte* bytes, int byteCount)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            if (_codec != 0) return Codec.String(this, new ReadOnlySpan<byte>(bytes, byteCount));
            // string.CreateStringFromEncoding
            int length = GetCharCount(bytes, byteCount);
            if (length == 0) return string.Empty;
            char[] chars = new char[length];
            fixed (char* c = chars) GetChars(bytes, byteCount, c, length);
            return new string(chars);
        }

        public string GetString(ReadOnlySpan<byte> bytes)
        {
            if (_codec != 0) return Codec.String(this, bytes);
            byte[] array = bytes.ToArray();
            return array.Length == 0 ? new string(GetChars(array, 0, 0)) : GetString(array, 0, array.Length);
        }

        // ---- sizes, coders ----

        private const string GetByteCountOverflow = "Too many characters. The resulting number of bytes is larger than what can be returned as an int.";
        private const string GetCharCountOverflow = "Too many bytes. The resulting number of chars is larger than what can be returned as an int.";

        /// <summary>Abstract in .NET; the built-in encodings' implementation.</summary>
        public virtual int GetMaxByteCount(int charCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            long byteCount = (long)charCount + 1;
            if (EncoderFallback.MaxCharCount > 1) byteCount *= EncoderFallback.MaxCharCount;
            if (_codec == CodecUtf8) byteCount *= 3;
            else if (_codec is CodecUtf16LE or CodecUtf16BE) byteCount <<= 1;
            if (byteCount > 0x7fffffff) throw new ArgumentOutOfRangeException("charCount", GetByteCountOverflow);
            return (int)byteCount;
        }

        /// <summary>Abstract in .NET; the built-in encodings' implementation.</summary>
        public virtual int GetMaxCharCount(int byteCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            long charCount = _codec switch
            {
                CodecUtf8 => (long)byteCount + 1,
                CodecUtf16LE or CodecUtf16BE => (long)(byteCount >> 1) + (byteCount & 1) + 1,
                _ => byteCount,
            };
            if (_codec != CodecLatin1 && DecoderFallback.MaxCharCount > 1) charCount *= DecoderFallback.MaxCharCount;
            if (charCount > 0x7fffffff) throw new ArgumentOutOfRangeException("byteCount", GetCharCountOverflow);
            return (int)charCount;
        }

        public virtual Decoder GetDecoder() =>
            _codec != 0 ? (Decoder)(object)new BuiltinDecoder(this) : (Decoder)(object)new DefaultDecoder(this);

        public virtual Encoder GetEncoder() =>
            _codec != 0 ? (Encoder)(object)new BuiltinEncoder(this) : (Encoder)(object)new DefaultEncoder(this);

        internal void ThrowBytesOverflow() =>
            throw new ArgumentException(string.Format("The output byte buffer is too small to contain the encoded data, encoding codepage '{0}' and fallback '{1}'.",
                _codePage, EncoderFallback.GetType()), "bytes");

        internal void ThrowCharsOverflow() =>
            throw new ArgumentException(string.Format("The output char buffer is too small to contain the decoded characters, encoding codepage '{0}' and fallback '{1}'.",
                _codePage, DecoderFallback.GetType()), "chars");

        // .NET's Encoding.DefaultEncoder / DefaultDecoder: a derived encoding's coders, without state.
        internal sealed class DefaultEncoder : EncoderImpl
        {
            private readonly EncodingImpl _encoding;

            public DefaultEncoder(EncodingImpl encoding) { _encoding = encoding; }

            public override int GetByteCount(char[] chars, int index, int count, bool flush) => _encoding.GetByteCount(chars, index, count);
            public override int GetByteCount(char* chars, int count, bool flush) => _encoding.GetByteCount(chars, count);
            public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush) =>
                _encoding.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
            public override int GetBytes(char* chars, int charCount, byte* bytes, int byteCount, bool flush) =>
                _encoding.GetBytes(chars, charCount, bytes, byteCount);
        }

        internal sealed class DefaultDecoder : DecoderImpl
        {
            private readonly EncodingImpl _encoding;

            public DefaultDecoder(EncodingImpl encoding) { _encoding = encoding; }

            public override int GetCharCount(byte[] bytes, int index, int count) => GetCharCount(bytes, index, count, false);
            public override int GetCharCount(byte[] bytes, int index, int count, bool flush) => _encoding.GetCharCount(bytes, index, count);
            public override int GetCharCount(byte* bytes, int count, bool flush) => _encoding.GetCharCount(bytes, count);
            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) =>
                GetChars(bytes, byteIndex, byteCount, chars, charIndex, false);
            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, bool flush) =>
                _encoding.GetChars(bytes, byteIndex, byteCount, chars, charIndex);
            public override int GetChars(byte* bytes, int byteCount, char* chars, int charCount, bool flush) =>
                _encoding.GetChars(bytes, byteCount, chars, charCount);
        }
    }

    /// <summary>
    /// The built-in encodings' transcoding, one scalar at a time. Invalid input goes to the fallback: an
    /// unencodable char (or surrogate pair) to the EncoderFallback, whose replacement is encoded in turn; an
    /// invalid byte sequence to the DecoderFallback — for UTF-8, each maximal subpart of an ill-formed
    /// sequence (Unicode's "best practice", which .NET follows) is one fallback.
    /// </summary>
    internal static class Codec
    {
        // A stateful coder's leftovers: bytes of an incomplete sequence, or a high surrogate.
        internal sealed class State
        {
            internal byte[] Bytes = new byte[4];
            internal int ByteCount;
            internal char HighSurrogate;
        }

        // ---- per-codec steps ----

        /// <summary>
        /// The sequence at bytes[i]: its length (> 0) and scalar value; -n for an invalid sequence of n bytes;
        /// 0 for an incomplete one that more bytes could complete (only when not `final`).
        /// </summary>
        private static int DecodeStep(int codec, ReadOnlySpan<byte> bytes, int i, bool final, out int scalar)
        {
            int available = bytes.Length - i;
            scalar = 0;
            switch (codec)
            {
                case EncodingImpl.CodecUtf8:
                {
                    int b0 = bytes[i];
                    if (b0 < 0x80) { scalar = b0; return 1; }
                    int need, low = 0x80, high = 0xBF, cp;
                    if (b0 >= 0xC2 && b0 <= 0xDF) { need = 1; cp = b0 & 0x1F; }
                    else if (b0 >= 0xE0 && b0 <= 0xEF)
                    {
                        need = 2; cp = b0 & 0x0F;
                        if (b0 == 0xE0) low = 0xA0; else if (b0 == 0xED) high = 0x9F;
                    }
                    else if (b0 >= 0xF0 && b0 <= 0xF4)
                    {
                        need = 3; cp = b0 & 0x07;
                        if (b0 == 0xF0) low = 0x90; else if (b0 == 0xF4) high = 0x8F;
                    }
                    else return -1;
                    for (int k = 1; k <= need; k++)
                    {
                        if (k >= available) return final ? -k : 0;
                        int b = bytes[i + k];
                        if (b < (k == 1 ? low : 0x80) || b > (k == 1 ? high : 0xBF)) return -k;
                        cp = (cp << 6) | (b & 0x3F);
                    }
                    scalar = cp;
                    return need + 1;
                }
                case EncodingImpl.CodecUtf16LE:
                case EncodingImpl.CodecUtf16BE:
                {
                    bool big = codec == EncodingImpl.CodecUtf16BE;
                    if (available < 2) return final ? -1 : 0;
                    int u = big ? (bytes[i] << 8) | bytes[i + 1] : bytes[i] | (bytes[i + 1] << 8);
                    if (u < 0xD800 || u > 0xDFFF) { scalar = u; return 2; }
                    if (u >= 0xDC00) return -2;
                    if (available < 4) return final ? -2 : 0;
                    int u2 = big ? (bytes[i + 2] << 8) | bytes[i + 3] : bytes[i + 2] | (bytes[i + 3] << 8);
                    if (u2 < 0xDC00 || u2 > 0xDFFF) return -2;
                    scalar = 0x10000 + ((u - 0xD800) << 10) + (u2 - 0xDC00);
                    return 4;
                }
                case EncodingImpl.CodecAscii:
                    if (bytes[i] < 0x80) { scalar = bytes[i]; return 1; }
                    return -1;
                default: // Latin1
                    scalar = bytes[i];
                    return 1;
            }
        }

        /// <summary>The bytes `scalar` takes, or -1 if the codec can't encode it (a lone surrogate is passed as itself).</summary>
        private static int ScalarByteCount(int codec, int scalar)
        {
            bool surrogate = scalar >= 0xD800 && scalar <= 0xDFFF;
            switch (codec)
            {
                case EncodingImpl.CodecUtf8:
                    return surrogate ? -1 : scalar < 0x80 ? 1 : scalar < 0x800 ? 2 : scalar < 0x10000 ? 3 : 4;
                case EncodingImpl.CodecUtf16LE:
                case EncodingImpl.CodecUtf16BE:
                    return surrogate ? -1 : scalar < 0x10000 ? 2 : 4;
                case EncodingImpl.CodecAscii:
                    return scalar < 0x80 ? 1 : -1;
                default:
                    return scalar <= 0xFF ? 1 : -1;
            }
        }

        private static void WriteScalar(int codec, int scalar, Span<byte> dest)
        {
            switch (codec)
            {
                case EncodingImpl.CodecUtf8:
                    if (scalar < 0x80) dest[0] = (byte)scalar;
                    else if (scalar < 0x800) { dest[0] = (byte)(0xC0 | (scalar >> 6)); dest[1] = (byte)(0x80 | (scalar & 0x3F)); }
                    else if (scalar < 0x10000)
                    {
                        dest[0] = (byte)(0xE0 | (scalar >> 12)); dest[1] = (byte)(0x80 | ((scalar >> 6) & 0x3F)); dest[2] = (byte)(0x80 | (scalar & 0x3F));
                    }
                    else
                    {
                        dest[0] = (byte)(0xF0 | (scalar >> 18)); dest[1] = (byte)(0x80 | ((scalar >> 12) & 0x3F));
                        dest[2] = (byte)(0x80 | ((scalar >> 6) & 0x3F)); dest[3] = (byte)(0x80 | (scalar & 0x3F));
                    }
                    break;
                case EncodingImpl.CodecUtf16LE:
                case EncodingImpl.CodecUtf16BE:
                {
                    bool big = codec == EncodingImpl.CodecUtf16BE;
                    if (scalar < 0x10000) WriteUnit(dest, 0, scalar, big);
                    else
                    {
                        WriteUnit(dest, 0, 0xD800 + ((scalar - 0x10000) >> 10), big);
                        WriteUnit(dest, 2, 0xDC00 + ((scalar - 0x10000) & 0x3FF), big);
                    }
                    break;
                }
                default:
                    dest[0] = (byte)scalar;
                    break;
            }
        }

        private static void WriteUnit(Span<byte> dest, int at, int unit, bool big)
        {
            dest[at] = (byte)(big ? unit >> 8 : unit);
            dest[at + 1] = (byte)(big ? unit : unit >> 8);
        }

        // ---- encoding driver ----

        internal static int ByteCount(EncodingImpl e, ReadOnlySpan<char> chars)
        {
            EncoderFallbackBuffer fb = null;
            return Encode(e, chars, default, true, ref fb, e.EncoderFallback, null, true, false, out _, out _);
        }

        internal static int Bytes(EncodingImpl e, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            EncoderFallbackBuffer fb = null;
            return Encode(e, chars, bytes, false, ref fb, e.EncoderFallback, null, true, false, out _, out _);
        }

        /// <summary>
        /// Encodes `chars` after the high surrogate the state holds (stateful encoders). `count`: only count.
        /// `limit` (Encoder.Convert): stop at the first char whose bytes don't fit instead of throwing.
        /// `commit`: update the state (the counting calls of a stateful encoder don't).
        /// </summary>
        internal static int Encode(EncodingImpl e, ReadOnlySpan<char> chars, Span<byte> bytes, bool count, ref EncoderFallbackBuffer fb,
            EncoderFallback fallback, State state, bool flush, bool limit, out int charsUsed, out bool full, bool commit = true)
        {
            int codec = e._codec;
            bool asciiFast = codec is EncodingImpl.CodecUtf8 or EncodingImpl.CodecAscii or EncodingImpl.CodecLatin1;
            int written = 0, i = 0;
            char pending = state != null ? state.HighSurrogate : '\0';
            full = false;
            Span<byte> one = stackalloc byte[4];
            while (true)
            {
                // The next scalar: from a pending high surrogate, or the input.
                int scalar, units;
                char high = '\0', low = '\0';
                bool lone = false, fromPending = false;
                if (pending != '\0')
                {
                    fromPending = true;
                    if (i < chars.Length && char.IsLowSurrogate(chars[i]))
                    {
                        high = pending; low = chars[i];
                        scalar = char.ConvertToUtf32(high, low);
                        units = 1;
                    }
                    else if (i < chars.Length || flush)
                    {
                        lone = true; high = pending; scalar = pending; units = 0;
                    }
                    else break;
                }
                else
                {
                    if (i >= chars.Length) break;
                    char c = chars[i];
                    if (asciiFast && c < 0x80)
                    {
                        if (!count)
                        {
                            if (written >= bytes.Length)
                            {
                                if (limit) { full = true; break; }
                                e.ThrowBytesOverflow();
                            }
                            bytes[written] = (byte)c;
                        }
                        written++;
                        i++;
                        continue;
                    }
                    if (char.IsHighSurrogate(c))
                    {
                        if (i + 1 < chars.Length)
                        {
                            if (char.IsLowSurrogate(chars[i + 1])) { high = c; low = chars[i + 1]; scalar = char.ConvertToUtf32(c, low); units = 2; }
                            else { lone = true; high = c; scalar = c; units = 1; }
                        }
                        else if (flush || state == null) { lone = true; high = c; scalar = c; units = 1; }
                        else
                        {
                            // A high surrogate ending the input: kept for the next call.
                            pending = c;
                            i++;
                            continue;
                        }
                    }
                    else if (char.IsLowSurrogate(c)) { lone = true; high = c; scalar = c; units = 1; }
                    else { high = c; scalar = c; units = 1; }
                }

                int n = lone ? -1 : ScalarByteCount(codec, scalar);
                if (n >= 0)
                {
                    if (!count)
                    {
                        if (written + n > bytes.Length)
                        {
                            if (limit) { full = true; break; }
                            e.ThrowBytesOverflow();
                        }
                        WriteScalar(codec, scalar, bytes.Slice(written));
                    }
                    written += n;
                }
                else
                {
                    // The fallback replaces the char (or the pair), and its replacement is encoded.
                    fb ??= fallback.CreateFallbackBuffer();
                    int index = fromPending ? -1 : i;
                    if (!lone && low != '\0') fb.Fallback(high, low, index);
                    else fb.Fallback(high, index);
                    int fallbackBytes = 0, start = written;
                    bool fits = true;
                    // The fallback buffer protocol (EncoderNLS): GetNextChar until it returns '\0' — a buffer may
                    // only mark itself finished on that last call (System.Xml's CharEntityEncoderFallbackBuffer).
                    char next = '\0';
                    bool haveNext = false;
                    while (true)
                    {
                        char f = haveNext ? next : fb.GetNextChar();
                        haveNext = false;
                        if (f == '\0') break;
                        int fs = f;
                        if (char.IsHighSurrogate(f))
                        {
                            char f2 = fb.GetNextChar();
                            if (char.IsLowSurrogate(f2)) fs = char.ConvertToUtf32(f, f2);
                            else { next = f2; haveNext = true; }
                        }
                        int fn = fs >= 0xD800 && fs <= 0xDFFF ? -1 : ScalarByteCount(codec, fs);
                        if (fn < 0) EncoderFallbackBufferImpl.ThrowLastCharRecursive(fs);
                        if (!count)
                        {
                            if (start + fallbackBytes + fn > bytes.Length) { fits = false; break; }
                            WriteScalar(codec, fs, one);
                            one.Slice(0, fn).CopyTo(bytes.Slice(start + fallbackBytes));
                        }
                        fallbackBytes += fn;
                    }
                    if (!fits)
                    {
                        fb.Reset();
                        if (limit) { full = true; break; }
                        e.ThrowBytesOverflow();
                    }
                    written += fallbackBytes;
                }
                if (fromPending) pending = '\0';
                i += units;
            }
            if (state != null && commit) state.HighSurrogate = pending;
            charsUsed = i;
            return written;
        }

        // ---- decoding driver ----

        internal static int CharCount(EncodingImpl e, ReadOnlySpan<byte> bytes)
        {
            DecoderFallbackBuffer fb = null;
            return Decode(e, bytes, default, true, ref fb, e.DecoderFallback, null, true, false, out _, out _);
        }

        internal static int Chars(EncodingImpl e, ReadOnlySpan<byte> bytes, Span<char> chars)
        {
            DecoderFallbackBuffer fb = null;
            return Decode(e, bytes, chars, false, ref fb, e.DecoderFallback, null, true, false, out _, out _);
        }

        internal static string String(EncodingImpl e, ReadOnlySpan<byte> bytes)
        {
            int length = CharCount(e, bytes);
            if (length == 0) return string.Empty;
            char[] chars = new char[length];
            Chars(e, bytes, chars);
            return new string(chars);
        }

        /// <summary>
        /// Decodes `bytes` after the bytes the state holds (stateful decoders), like Encode: `count` only counts,
        /// `limit` stops before the first char that doesn't fit, `commit` updates the state.
        /// </summary>
        internal static int Decode(EncodingImpl e, ReadOnlySpan<byte> bytes, Span<char> chars, bool count, ref DecoderFallbackBuffer fb,
            DecoderFallback fallback, State state, bool flush, bool limit, out int bytesUsed, out bool full, bool commit = true)
        {
            int codec = e._codec;
            int leftover = state != null ? state.ByteCount : 0;
            ReadOnlySpan<byte> src = bytes;
            if (leftover > 0)
            {
                byte[] combined = new byte[leftover + bytes.Length];
                new ReadOnlySpan<byte>(state.Bytes, 0, leftover).CopyTo(combined);
                bytes.CopyTo(new Span<byte>(combined, leftover, bytes.Length));
                src = combined;
            }
            bool asciiFast = codec is EncodingImpl.CodecUtf8 or EncodingImpl.CodecAscii or EncodingImpl.CodecLatin1;
            int written = 0, i = 0, keep = 0;
            bool heldHigh = false;
            full = false;
            while (i < src.Length)
            {
                if (asciiFast && src[i] < 0x80)
                {
                    if (!count)
                    {
                        if (written >= chars.Length)
                        {
                            if (limit) { full = true; break; }
                            e.ThrowCharsOverflow();
                        }
                        chars[written] = (char)src[i];
                    }
                    written++;
                    i++;
                    continue;
                }
                bool final = flush || state == null;
                int step = DecodeStep(codec, src, i, final, out int scalar);
                if (step == 0)
                {
                    keep = src.Length - i; // an incomplete sequence waits for more bytes
                    break;
                }
                if (step > 0)
                {
                    int n = scalar >= 0x10000 ? 2 : 1;
                    if (!count)
                    {
                        if (written + n > chars.Length)
                        {
                            if (limit)
                            {
                                // UTF-16 (UnicodeEncoding's decoder): a pair's high surrogate is taken into the state
                                // when only the low one doesn't fit.
                                if (n == 2 && state != null && codec is EncodingImpl.CodecUtf16LE or EncodingImpl.CodecUtf16BE)
                                    heldHigh = true;
                                full = true;
                                break;
                            }
                            e.ThrowCharsOverflow();
                        }
                        if (n == 1) chars[written] = (char)scalar;
                        else
                        {
                            chars[written] = (char)(0xD800 + ((scalar - 0x10000) >> 10));
                            chars[written + 1] = (char)(0xDC00 + ((scalar - 0x10000) & 0x3FF));
                        }
                    }
                    written += n;
                    i += step;
                    continue;
                }
                int invalid = -step;
                fb ??= fallback.CreateFallbackBuffer();
                fb.Fallback(src.Slice(i, invalid).ToArray(), i - leftover);
                int start = written, fallbackChars = 0;
                bool fits = true;
                while (true)
                {
                    char f = fb.GetNextChar();
                    if (f == '\0') break;
                    if (!count)
                    {
                        if (start + fallbackChars >= chars.Length) { fits = false; break; }
                        chars[start + fallbackChars] = f;
                    }
                    fallbackChars++;
                }
                if (!fits)
                {
                    fb.Reset();
                    if (limit) { full = true; break; }
                    e.ThrowCharsOverflow();
                }
                written += fallbackChars;
                i += invalid;
            }
            if (full)
            {
                // Stopped for room (Convert): the state keeps what of its own bytes wasn't decoded yet.
                if (heldHigh) i += 2;
                keep = heldHigh ? 2 : Math.Max(0, leftover - i);
                if (state != null && commit && keep > 0) src.Slice(heldHigh ? i - 2 : i, keep).CopyTo(state.Bytes);
                if (state != null && commit) state.ByteCount = keep;
                bytesUsed = Math.Max(0, i - leftover);
                return written;
            }
            if (state != null && commit)
            {
                if (keep > 0) src.Slice(src.Length - keep).CopyTo(state.Bytes);
                state.ByteCount = keep;
            }
            // Bytes of this call consumed: everything decoded or kept, less what came from the state.
            bytesUsed = Math.Max(0, i + keep - leftover);
            return written;
        }
    }

    /// <summary>A built-in encoding's Decoder (.NET's DecoderNLS): an incomplete sequence carries over to the next call.</summary>
    internal sealed unsafe class BuiltinDecoder : DecoderImpl
    {
        private readonly EncodingImpl _encoding;
        private readonly Codec.State _state = new Codec.State();

        internal BuiltinDecoder(EncodingImpl encoding)
        {
            _encoding = encoding;
            _fallback = encoding.DecoderFallback;
        }

        public override void Reset()
        {
            _state.ByteCount = 0;
            _fallbackBuffer?.Reset();
        }

        private int Run(ReadOnlySpan<byte> bytes, Span<char> chars, bool count, bool flush, bool limit, out int bytesUsed, out bool full)
        {
            DecoderFallbackBuffer fb = FallbackBuffer;
            return Codec.Decode(_encoding, bytes, chars, count, ref fb, _fallback, _state, flush, limit, out bytesUsed, out full, commit: !count);
        }

        public override int GetCharCount(byte[] bytes, int index, int count) => GetCharCount(bytes, index, count, false);

        public override int GetCharCount(byte[] bytes, int index, int count, bool flush)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (bytes.Length - index < count) throw new ArgumentOutOfRangeException("bytes", "Index and count must refer to a location within the buffer.");
            return Run(new ReadOnlySpan<byte>(bytes, index, count), default, true, flush, false, out _, out _);
        }

        public override int GetCharCount(byte* bytes, int count, bool flush)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            return Run(new ReadOnlySpan<byte>(bytes, count), default, true, flush, false, out _, out _);
        }

        public override int GetCharCount(ReadOnlySpan<byte> bytes, bool flush) => Run(bytes, default, true, flush, false, out _, out _);

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) =>
            GetChars(bytes, byteIndex, byteCount, chars, charIndex, false);

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, bool flush)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            if (bytes.Length - byteIndex < byteCount) throw new ArgumentOutOfRangeException("bytes", "Index and count must refer to a location within the buffer.");
            if (charIndex < 0 || charIndex > chars.Length)
                throw new ArgumentOutOfRangeException("charIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            return Run(new ReadOnlySpan<byte>(bytes, byteIndex, byteCount), new Span<char>(chars, charIndex, chars.Length - charIndex), false, flush, false, out _, out _);
        }

        public override int GetChars(byte* bytes, int byteCount, char* chars, int charCount, bool flush)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (chars == null) throw new ArgumentNullException("chars");
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            return Run(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount), false, flush, false, out _, out _);
        }

        public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush) => Run(bytes, chars, false, flush, false, out _, out _);

        public override void Convert(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, int charCount, bool flush,
            out int bytesUsed, out int charsUsed, out bool completed)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            if (bytes.Length - byteIndex < byteCount) throw new ArgumentOutOfRangeException("bytes", "Index and count must refer to a location within the buffer.");
            if (chars.Length - charIndex < charCount) throw new ArgumentOutOfRangeException("chars", "Index and count must refer to a location within the buffer.");
            Convert(new ReadOnlySpan<byte>(bytes, byteIndex, byteCount), new Span<char>(chars, charIndex, charCount), flush, out bytesUsed, out charsUsed, out completed);
        }

        public override void Convert(byte* bytes, int byteCount, char* chars, int charCount, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (chars == null) throw new ArgumentNullException("chars");
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            Convert(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount), flush, out bytesUsed, out charsUsed, out completed);
        }

        public override void Convert(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            charsUsed = Run(bytes, chars, false, flush, true, out bytesUsed, out bool full);
            if (full && charsUsed == 0 && bytesUsed == 0) _encoding.ThrowCharsOverflow(); // not even one char fits
            completed = bytesUsed == bytes.Length && (!flush || _state.ByteCount == 0) && (_fallbackBuffer == null || _fallbackBuffer.Remaining == 0);
        }
    }

    /// <summary>A built-in encoding's Encoder (.NET's EncoderNLS): a trailing high surrogate carries over to the next call.</summary>
    internal sealed unsafe class BuiltinEncoder : EncoderImpl
    {
        private readonly EncodingImpl _encoding;
        private readonly Codec.State _state = new Codec.State();

        internal BuiltinEncoder(EncodingImpl encoding)
        {
            _encoding = encoding;
            _fallback = encoding.EncoderFallback;
        }

        public override void Reset()
        {
            _state.HighSurrogate = '\0';
            _fallbackBuffer?.Reset();
        }

        private int Run(ReadOnlySpan<char> chars, Span<byte> bytes, bool count, bool flush, bool limit, out int charsUsed, out bool full)
        {
            EncoderFallbackBuffer fb = FallbackBuffer;
            return Codec.Encode(_encoding, chars, bytes, count, ref fb, _fallback, _state, flush, limit, out charsUsed, out full, commit: !count);
        }

        public override int GetByteCount(char[] chars, int index, int count, bool flush)
        {
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (chars.Length - index < count) throw new ArgumentOutOfRangeException("chars", "Index and count must refer to a location within the buffer.");
            return Run(new ReadOnlySpan<char>(chars, index, count), default, true, flush, false, out _, out _);
        }

        public override int GetByteCount(char* chars, int count, bool flush)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            return Run(new ReadOnlySpan<char>(chars, count), default, true, flush, false, out _, out _);
        }

        public override int GetByteCount(ReadOnlySpan<char> chars, bool flush) => Run(chars, default, true, flush, false, out _, out _);

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush)
        {
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            if (chars.Length - charIndex < charCount) throw new ArgumentOutOfRangeException("chars", "Index and count must refer to a location within the buffer.");
            if (byteIndex < 0 || byteIndex > bytes.Length)
                throw new ArgumentOutOfRangeException("byteIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            return Run(new ReadOnlySpan<char>(chars, charIndex, charCount), new Span<byte>(bytes, byteIndex, bytes.Length - byteIndex), false, flush, false, out _, out _);
        }

        public override int GetBytes(char* chars, int charCount, byte* bytes, int byteCount, bool flush)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            if (bytes == null) throw new ArgumentNullException("bytes");
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            return Run(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount), false, flush, false, out _, out _);
        }

        public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush) => Run(chars, bytes, false, flush, false, out _, out _);

        public override void Convert(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, int byteCount, bool flush,
            out int charsUsed, out int bytesUsed, out bool completed)
        {
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            if (chars.Length - charIndex < charCount) throw new ArgumentOutOfRangeException("chars", "Index and count must refer to a location within the buffer.");
            if (bytes.Length - byteIndex < byteCount) throw new ArgumentOutOfRangeException("bytes", "Index and count must refer to a location within the buffer.");
            Convert(new ReadOnlySpan<char>(chars, charIndex, charCount), new Span<byte>(bytes, byteIndex, byteCount), flush, out charsUsed, out bytesUsed, out completed);
        }

        public override void Convert(char* chars, int charCount, byte* bytes, int byteCount, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            if (bytes == null) throw new ArgumentNullException("bytes");
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            Convert(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount), flush, out charsUsed, out bytesUsed, out completed);
        }

        public override void Convert(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            bytesUsed = Run(chars, bytes, false, flush, true, out charsUsed, out bool full);
            if (full && bytesUsed == 0 && charsUsed == 0) _encoding.ThrowBytesOverflow(); // not even one char's bytes fit
            completed = charsUsed == chars.Length && (!flush || _state.HighSurrogate == '\0') && (_fallbackBuffer == null || _fallbackBuffer.Remaining == 0);
        }
    }

    [Shadows("System.Text.UTF8Encoding")]
    public unsafe class UTF8EncodingImpl : EncodingImpl
    {
        private readonly bool _emitUTF8Identifier;
        private readonly bool _isThrowException;

        public UTF8EncodingImpl() : base(65001) { _codec = CodecUtf8; }

        public UTF8EncodingImpl(bool encoderShouldEmitUTF8Identifier) : this() { _emitUTF8Identifier = encoderShouldEmitUTF8Identifier; }

        public UTF8EncodingImpl(bool encoderShouldEmitUTF8Identifier, bool throwOnInvalidBytes) : this(encoderShouldEmitUTF8Identifier)
        {
            _isThrowException = throwOnInvalidBytes;
            if (_isThrowException) SetDefaultFallbacks();
        }

        internal override void SetDefaultFallbacks()
        {
            if (_isThrowException)
            {
                encoderFallback = EncoderFallback.ExceptionFallback;
                decoderFallback = DecoderFallback.ExceptionFallback;
            }
            else
            {
                encoderFallback = new EncoderReplacementFallback("�");
                decoderFallback = new DecoderReplacementFallback("�");
            }
        }

        public override byte[] GetPreamble() => _emitUTF8Identifier ? [0xEF, 0xBB, 0xBF] : [];
        public override ReadOnlySpan<byte> Preamble => GetPreamble();

        public override bool Equals(object value) =>
            value is UTF8EncodingImpl that && _emitUTF8Identifier == that._emitUTF8Identifier
            && EncoderFallback.Equals(that.EncoderFallback) && DecoderFallback.Equals(that.DecoderFallback);

        public override int GetHashCode() => EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode() + 65001 + (_emitUTF8Identifier ? 1 : 0);
    }

    [Shadows("System.Text.UnicodeEncoding")]
    public unsafe class UnicodeEncodingImpl : EncodingImpl
    {
        public const int CharSize = 2;
        private readonly bool _bigEndian, _byteOrderMark, _isThrowException;

        public UnicodeEncodingImpl() : this(false, true) { }

        public UnicodeEncodingImpl(bool bigEndian, bool byteOrderMark) : base(bigEndian ? 1201 : 1200)
        {
            _bigEndian = bigEndian;
            _byteOrderMark = byteOrderMark;
            _codec = bigEndian ? CodecUtf16BE : CodecUtf16LE;
        }

        public UnicodeEncodingImpl(bool bigEndian, bool byteOrderMark, bool throwOnInvalidBytes) : this(bigEndian, byteOrderMark)
        {
            _isThrowException = throwOnInvalidBytes;
            if (_isThrowException) SetDefaultFallbacks();
        }

        internal override void SetDefaultFallbacks()
        {
            if (_isThrowException)
            {
                encoderFallback = EncoderFallback.ExceptionFallback;
                decoderFallback = DecoderFallback.ExceptionFallback;
            }
            else
            {
                encoderFallback = new EncoderReplacementFallback("�");
                decoderFallback = new DecoderReplacementFallback("�");
            }
        }

        public override byte[] GetPreamble() =>
            !_byteOrderMark ? [] : _bigEndian ? [0xFE, 0xFF] : [0xFF, 0xFE];

        public override ReadOnlySpan<byte> Preamble => GetPreamble();

        public override bool Equals(object value) =>
            value is UnicodeEncodingImpl that && CodePage == that.CodePage && _byteOrderMark == that._byteOrderMark && _bigEndian == that._bigEndian
            && EncoderFallback.Equals(that.EncoderFallback) && DecoderFallback.Equals(that.DecoderFallback);

        public override int GetHashCode() =>
            CodePage + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode() + (_byteOrderMark ? 4 : 0) + (_bigEndian ? 8 : 0);
    }

    [Shadows("System.Text.ASCIIEncoding")]
    public unsafe class ASCIIEncodingImpl : EncodingImpl
    {
        public ASCIIEncodingImpl() : base(20127) { _codec = CodecAscii; }
        public override bool IsSingleByte => true;
    }

    /// <summary>Encoding.Latin1 (an internal class in the BCL): unencodable chars get .NET's best-fit fallback.</summary>
    internal sealed unsafe class Latin1EncodingImpl : EncodingImpl
    {
        public Latin1EncodingImpl() : base(28591) { _codec = CodecLatin1; }

        internal override void SetDefaultFallbacks()
        {
            encoderFallback = EncoderLatin1BestFitFallback.SingletonInstance;
            decoderFallback = DecoderFallback.ReplacementFallback;
        }

        public override bool IsSingleByte => true;
    }
}
