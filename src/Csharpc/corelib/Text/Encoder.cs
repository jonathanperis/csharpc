// System.Text.Encoder: Encoding.GetEncoder()'s stateful encoder (EncoderNLS in the BCL). Its only state
// is a high surrogate that ended the previous block; Convert encodes as much as fits.
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Encoder")]
    public abstract unsafe class EncoderImpl
    {
        protected EncoderImpl() { }

        public virtual void Reset() { }

        public abstract int GetByteCount(char[] chars, int index, int count, bool flush);
        public abstract int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush);

        public virtual int GetByteCount(ReadOnlySpan<char> chars, bool flush) => GetByteCount(chars.ToArray(), 0, chars.Length, flush);

        public virtual int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush)
        {
            byte[] buffer = new byte[bytes.Length];
            int n = GetBytes(chars.ToArray(), 0, chars.Length, buffer, 0, flush);
            buffer.AsSpan(0, n).CopyTo(bytes);
            return n;
        }

        public virtual void Convert(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            byte[] buffer = new byte[bytes.Length];
            Convert(chars.ToArray(), 0, chars.Length, buffer, 0, bytes.Length, flush, out charsUsed, out bytesUsed, out completed);
            buffer.AsSpan(0, bytesUsed).CopyTo(bytes);
        }

        public virtual void Convert(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, int byteCount, bool flush,
            out int charsUsed, out int bytesUsed, out bool completed)
        {
            throw new NotSupportedException("Encoder.Convert on a custom encoder is not supported by csharpc yet.");
        }
    }

    internal sealed unsafe class EncodingEncoder : EncoderImpl
    {
        private readonly EncodingImpl _encoding;
        private char _highSurrogate;

        internal EncodingEncoder(EncodingImpl encoding) { _encoding = encoding; }

        public override void Reset() { _highSurrogate = '\0'; }

        private static void CheckArray(char[] chars, int index, int count)
        {
            if (chars == null) throw new ArgumentNullException("chars", "Array cannot be null.");
            if (index < 0 || count < 0) throw new ArgumentOutOfRangeException(index < 0 ? "index" : "count", "Non-negative number required.");
            if (chars.Length - index < count) throw new ArgumentOutOfRangeException("chars", "Index and count must refer to a location within the buffer.");
        }

        /// <summary>The block to encode: the saved high surrogate, then the chars, minus a trailing high
        /// surrogate kept for the next block unless flushing.</summary>
        private char[] Block(char* chars, int count, bool flush, out bool keepsTail)
        {
            keepsTail = !flush && count > 0 && char.IsHighSurrogate(chars[count - 1]);
            int start = _highSurrogate != '\0' ? 1 : 0;
            int take = keepsTail ? count - 1 : count;
            char[] block = new char[start + take];
            if (start == 1) block[0] = _highSurrogate;
            for (int i = 0; i < take; i++) block[start + i] = chars[i];
            return block;
        }

        private int Count(char[] block)
        {
            fixed (char* c = block) return _encoding.ByteCount(c, block.Length);
        }

        public override int GetByteCount(char[] chars, int index, int count, bool flush)
        {
            CheckArray(chars, index, count);
            fixed (char* c = chars)
            {
                char[] block = Block(c + index, count, flush, out _);
                return Count(block);
            }
        }

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush)
        {
            CheckArray(chars, charIndex, charCount);
            if (bytes == null) throw new ArgumentNullException("bytes", "Array cannot be null.");
            if (byteIndex < 0 || byteIndex > bytes.Length) throw new ArgumentOutOfRangeException("byteIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            fixed (char* c = chars)
            {
                char[] block = Block(c + charIndex, charCount, flush, out bool keepsTail);
                int needed = Count(block);
                if (needed > bytes.Length - byteIndex) throw TooSmall();
                int n;
                fixed (char* b = block) fixed (byte* d = bytes) n = _encoding.EncodeChars(b, block.Length, d + byteIndex);
                _highSurrogate = keepsTail ? c[charIndex + charCount - 1] : '\0';
                return n;
            }
        }

        public override void Convert(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, int byteCount, bool flush,
            out int charsUsed, out int bytesUsed, out bool completed)
        {
            CheckArray(chars, charIndex, charCount);
            if (bytes == null) throw new ArgumentNullException("bytes", "Array cannot be null.");
            if (byteIndex < 0 || byteCount < 0) throw new ArgumentOutOfRangeException(byteIndex < 0 ? "byteIndex" : "byteCount", "Non-negative number required.");
            if (bytes.Length - byteIndex < byteCount) throw new ArgumentOutOfRangeException("bytes", "Index and count must refer to a location within the buffer.");
            fixed (char* c = chars)
            {
                char* input = c + charIndex;
                // The longest prefix (whole surrogate pairs only) whose encoding fits.
                int used = charCount;
                bool flushNow = flush;
                char[] block = Block(input, used, flushNow, out bool keepsTail);
                while (Count(block) > byteCount)
                {
                    used--;
                    if (used > 0 && char.IsLowSurrogate(input[used]) && char.IsHighSurrogate(input[used - 1])) used--;
                    flushNow = false;
                    if (used <= 0) { used = 0; block = Block(input, 0, false, out keepsTail); break; }
                    block = Block(input, used, false, out keepsTail);
                }
                if (Count(block) > byteCount) throw TooSmall();
                if (used == 0 && charCount > 0 && !(keepsTail && charCount == 1)) throw TooSmall();
                int n;
                fixed (char* b = block) fixed (byte* d = bytes) n = _encoding.EncodeChars(b, block.Length, d + byteIndex);
                _highSurrogate = keepsTail ? input[used - 1] : '\0';
                charsUsed = used;
                bytesUsed = n;
                completed = used == charCount && (!flush || _highSurrogate == '\0');
            }
        }

        private ArgumentException TooSmall() => new ArgumentException(
            "The output byte buffer is too small to contain the encoded data, encoding codepage '" + _encoding.CodePage
            + "' and fallback 'System.Text.EncoderReplacementFallback'.", "bytes");
    }
}
