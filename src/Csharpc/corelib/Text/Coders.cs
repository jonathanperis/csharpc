// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/Text/
// Decoder.cs and Encoder.cs; see THIRD-PARTY-NOTICES.md. The abstract bases (derivable: System.Private.Xml's
// decoders), with .NET's default implementations; the built-in encodings' stateful decoders and encoders are
// in Encoding.cs.
#nullable enable annotations
#nullable disable warnings
using System;
using System.Text;

namespace CsharpcCoreLib
{
    // A Decoder is used to decode a sequence of blocks of bytes into a
    // sequence of blocks of characters. Following instantiation of a decoder,
    // sequential blocks of bytes are converted into blocks of characters through
    // calls to the GetChars method. The decoder maintains state between the
    // conversions, allowing it to correctly decode byte sequences that span
    // adjacent blocks.
    //
    // Instances of specific implementations of the Decoder abstract base
    // class are typically obtained through calls to the GetDecoder method
    // of Encoding objects.
    //
    [Shadows("System.Text.Decoder")]
    public abstract class DecoderImpl
    {
        internal DecoderFallback? _fallback;

        internal DecoderFallbackBuffer? _fallbackBuffer;

        protected DecoderImpl()
        {
            // We don't call default reset because default reset probably isn't good if we aren't initialized.
        }

        public DecoderFallback? Fallback
        {
            get => _fallback;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                // Can't change fallback if buffer is wrong
                if (_fallbackBuffer is not null && _fallbackBuffer.Remaining > 0)
                    throw new ArgumentException(
                      "Cannot change fallback when buffer is not empty. Previous Convert() call left data in the fallback buffer.", nameof(value));

                _fallback = value;
                _fallbackBuffer = null;
            }
        }

        // Note: we don't test for threading here because async access to Encoders and Decoders
        // doesn't work anyway.
        public DecoderFallbackBuffer FallbackBuffer
        {
            get
            {
                _fallbackBuffer ??= _fallback is not null ?
                    _fallback.CreateFallbackBuffer() :
                    DecoderFallback.ReplacementFallback.CreateFallbackBuffer();

                return _fallbackBuffer;
            }
        }

        internal bool InternalHasFallbackBuffer => _fallbackBuffer is not null;

        // Reset the Decoder
        //
        // Normally if we call GetChars() and an error is thrown we don't change the state of the Decoder.  This
        // would allow the caller to correct the error condition and try again (such as if they need a bigger buffer.)
        //
        // If the caller doesn't want to try again after GetChars() throws an error, then they need to call Reset().
        //
        // Virtual implementation has to call GetChars with flush and a big enough buffer to clear a 0 byte string
        // We avoid GetMaxCharCount() because a) we can't call the base encoder and b) it might be really big.
        public virtual void Reset()
        {
            byte[] byteTemp = [];
            char[] charTemp = new char[GetCharCount(byteTemp, 0, 0, true)];
            GetChars(byteTemp, 0, 0, charTemp, 0, true);
            _fallbackBuffer?.Reset();
        }

        // Returns the number of characters the next call to GetChars will
        // produce if presented with the given range of bytes. The returned value
        // takes into account the state in which the decoder was left following the
        // last call to GetChars. The state of the decoder is not affected
        // by a call to this method.
        //
        public abstract int GetCharCount(byte[] bytes, int index, int count);

        public virtual int GetCharCount(byte[] bytes, int index, int count, bool flush)
        {
            return GetCharCount(bytes, index, count);
        }

        // We expect this to be the workhorse for NLS Encodings, but for existing
        // ones we need a working (if slow) default implementation)
        public virtual unsafe int GetCharCount(byte* bytes, int count, bool flush)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            byte[] arrbyte = new byte[count];

            for (int index = 0; index < count; index++)
                arrbyte[index] = bytes[index];

            return GetCharCount(arrbyte, 0, count);
        }

        public virtual int GetCharCount(ReadOnlySpan<byte> bytes, bool flush) => GetCharCount(bytes.ToArray(), 0, bytes.Length, flush);

        // Decodes a range of bytes in a byte array into a range of characters
        // in a character array. The method decodes byteCount bytes from
        // bytes starting at index byteIndex, storing the resulting
        // characters in chars starting at index charIndex. The
        // decoding takes into account the state in which the decoder was left
        // following the last call to this method.
        //
        // An exception occurs if the character array is not large enough to
        // hold the complete decoding of the bytes. The GetCharCount method
        // can be used to determine the exact number of characters that will be
        // produced for a given range of bytes. Alternatively, the
        // GetMaxCharCount method of the Encoding that produced this
        // decoder can be used to determine the maximum number of characters that
        // will be produced for a given number of bytes, regardless of the actual
        // byte values.
        //
        public abstract int GetChars(byte[] bytes, int byteIndex, int byteCount,
                                        char[] chars, int charIndex);

        public virtual int GetChars(byte[] bytes, int byteIndex, int byteCount,
                                       char[] chars, int charIndex, bool flush)
        {
            return GetChars(bytes, byteIndex, byteCount, chars, charIndex);
        }

        // We expect this to be the workhorse for NLS Encodings, but for existing
        // ones we need a working (if slow) default implementation)
        //
        // WARNING: If this breaks it could be a security threat.  Obviously we
        // call this internally, so you need to make sure that your pointers, counts
        // and indexes are correct when you call this method.
        //
        // In addition, we have internal code, which will be marked as "safe" calling
        // this code.  However this code is dependent upon the implementation of an
        // external GetChars() method, which could be overridden by a third party and
        // the results of which cannot be guaranteed.  We use that result to copy
        // the char[] to our char* output buffer.  If the result count was wrong, we
        // could easily overflow our output buffer.  Therefore we do an extra test
        // when we copy the buffer so that we don't overflow charCount either.
        public virtual unsafe int GetChars(byte* bytes, int byteCount,
                                           char* chars, int charCount, bool flush)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            // Get the byte array to convert
            byte[] arrByte = new byte[byteCount];

            for (int index = 0; index < byteCount; index++)
                arrByte[index] = bytes[index];

            // Get the char array to fill
            char[] arrChar = new char[charCount];

            // Do the work
            int result = GetChars(arrByte, 0, byteCount, arrChar, 0, flush);


            // Copy the char array
            // WARNING: We MUST make sure that we don't copy too many chars.  We can't
            // rely on result because it could be a 3rd party implementation.  We need
            // to make sure we never copy more than charCount chars no matter the value
            // of result
            if (result < charCount)
                charCount = result;

            // We check both result and charCount so that we don't accidentally overrun
            // our pointer buffer just because of an issue in GetChars
            for (int index = 0; index < charCount; index++)
                chars[index] = arrChar[index];

            return charCount;
        }

        public virtual int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush)
        {
            char[] arrChar = new char[chars.Length];
            int result = GetChars(bytes.ToArray(), 0, bytes.Length, arrChar, 0, flush);
            if (result > chars.Length) result = chars.Length;
            new ReadOnlySpan<char>(arrChar, 0, result).CopyTo(chars);
            return result;
        }

        // This method is used when the output buffer might not be large enough.
        // It will decode until it runs out of bytes, and then it will return
        // true if it the entire input was converted.  In either case it
        // will also return the number of converted bytes and output characters used.
        // It will only throw a buffer overflow exception if the entire length of chars[] is
        // too small to store the next char. (like 0 or maybe 1 or 4 for some encodings)
        // We're done processing this buffer only if completed returns true.
        //
        // Might consider checking Max...Count to avoid the extra counting step.
        //
        // Note that if all of the input bytes are not consumed, then we'll do a /2, which means
        // that its likely that we didn't consume as many bytes as we could have.  For some
        // applications this could be slow.  (Like trying to exactly fill an output buffer from a bigger stream)
        public virtual void Convert(byte[] bytes, int byteIndex, int byteCount,
                                    char[] chars, int charIndex, int charCount, bool flush,
                                    out int bytesUsed, out int charsUsed, out bool completed)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);

            ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            if (bytes.Length - byteIndex < byteCount)
                throw new ArgumentOutOfRangeException(nameof(bytes),
                      "Index and count must refer to a location within the buffer.");

            if (chars.Length - charIndex < charCount)
                throw new ArgumentOutOfRangeException(nameof(chars),
                      "Index and count must refer to a location within the buffer.");

            bytesUsed = byteCount;

            // Its easy to do if it won't overrun our buffer.
            while (bytesUsed > 0)
            {
                if (GetCharCount(bytes, byteIndex, bytesUsed, flush) <= charCount)
                {
                    charsUsed = GetChars(bytes, byteIndex, bytesUsed, chars, charIndex, flush);
                    completed = (bytesUsed == byteCount &&
                        (_fallbackBuffer is null || _fallbackBuffer.Remaining == 0));
                    return;
                }

                // Try again with 1/2 the count, won't flush then 'cause won't read it all
                flush = false;
                bytesUsed /= 2;
            }

            // Oops, we didn't have anything, we'll have to throw an overflow
            throw new ArgumentException("Conversion buffer overflow.");
        }

        // This is the version that uses *.
        // We're done processing this buffer only if completed returns true.
        //
        // Might consider checking Max...Count to avoid the extra counting step.
        //
        // Note that if all of the input bytes are not consumed, then we'll do a /2, which means
        // that its likely that we didn't consume as many bytes as we could have.  For some
        // applications this could be slow.  (Like trying to exactly fill an output buffer from a bigger stream)
        public virtual unsafe void Convert(byte* bytes, int byteCount,
                                           char* chars, int charCount, bool flush,
                                           out int bytesUsed, out int charsUsed, out bool completed)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);

            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            // Get ready to do it
            bytesUsed = byteCount;

            // Its easy to do if it won't overrun our buffer.
            while (bytesUsed > 0)
            {
                if (GetCharCount(bytes, bytesUsed, flush) <= charCount)
                {
                    charsUsed = GetChars(bytes, bytesUsed, chars, charCount, flush);
                    completed = (bytesUsed == byteCount &&
                        (_fallbackBuffer is null || _fallbackBuffer.Remaining == 0));
                    return;
                }

                // Try again with 1/2 the count, won't flush then 'cause won't read it all
                flush = false;
                bytesUsed /= 2;
            }

            // Oops, we didn't have anything, we'll have to throw an overflow
            throw new ArgumentException("Conversion buffer overflow.");
        }

        public virtual void Convert(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            char[] arrChar = new char[chars.Length];
            Convert(bytes.ToArray(), 0, bytes.Length, arrChar, 0, chars.Length, flush, out bytesUsed, out charsUsed, out completed);
            new ReadOnlySpan<char>(arrChar, 0, charsUsed).CopyTo(chars);
        }
    }

    // An Encoder is used to encode a sequence of blocks of characters into
    // a sequence of blocks of bytes. Following instantiation of an encoder,
    // sequential blocks of characters are converted into blocks of bytes through
    // calls to the GetBytes method. The encoder maintains state between the
    // conversions, allowing it to correctly encode character sequences that span
    // adjacent blocks.
    //
    // Instances of specific implementations of the Encoder abstract base
    // class are typically obtained through calls to the GetEncoder method
    // of Encoding objects.
    //
    [Shadows("System.Text.Encoder")]
    public abstract class EncoderImpl
    {
        internal EncoderFallback? _fallback;

        internal EncoderFallbackBuffer? _fallbackBuffer;

        protected EncoderImpl()
        {
            // We don't call default reset because default reset probably isn't good if we aren't initialized.
        }

        public EncoderFallback? Fallback
        {
            get => _fallback;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                // Can't change fallback if buffer is wrong
                if (_fallbackBuffer is not null && _fallbackBuffer.Remaining > 0)
                    throw new ArgumentException(
                      "Cannot change fallback when buffer is not empty. Previous Convert() call left data in the fallback buffer.", nameof(value));

                _fallback = value;
                _fallbackBuffer = null;
            }
        }

        // Note: we don't test for threading here because async access to Encoders and Decoders
        // doesn't work anyway.
        public EncoderFallbackBuffer FallbackBuffer
        {
            get
            {
                _fallbackBuffer ??= _fallback is not null ?
                    _fallback.CreateFallbackBuffer() :
                    EncoderFallback.ReplacementFallback.CreateFallbackBuffer();

                return _fallbackBuffer;
            }
        }

        internal bool InternalHasFallbackBuffer => _fallbackBuffer is not null;

        // Reset the Encoder
        //
        // Normally if we call GetBytes() and an error is thrown we don't change the state of the encoder.  This
        // would allow the caller to correct the error condition and try again (such as if they need a bigger buffer.)
        //
        // If the caller doesn't want to try again after GetBytes() throws an error, then they need to call Reset().
        //
        // Virtual implementation has to call GetBytes with flush and a big enough buffer to clear a 0 char string
        // We avoid GetMaxByteCount() because a) we can't call the base encoder and b) it might be really big.
        public virtual void Reset()
        {
            char[] charTemp = [];
            byte[] byteTemp = new byte[GetByteCount(charTemp, 0, 0, true)];
            GetBytes(charTemp, 0, 0, byteTemp, 0, true);
            _fallbackBuffer?.Reset();
        }

        // Returns the number of bytes the next call to GetBytes will
        // produce if presented with the given range of characters and the given
        // value of the flush parameter. The returned value takes into
        // account the state in which the encoder was left following the last call
        // to GetBytes. The state of the encoder is not affected by a call
        // to this method.
        //
        public abstract int GetByteCount(char[] chars, int index, int count, bool flush);

        // We expect this to be the workhorse for NLS encodings
        // unfortunately for existing overrides, it has to call the [] version,
        // which is really slow, so avoid this method if you might be calling external encodings.
        public virtual unsafe int GetByteCount(char* chars, int count, bool flush)
        {
            ArgumentNullException.ThrowIfNull(chars);

            ArgumentOutOfRangeException.ThrowIfNegative(count);

            char[] arrChar = new char[count];

            for (int index = 0; index < count; index++)
                arrChar[index] = chars[index];

            return GetByteCount(arrChar, 0, count, flush);
        }

        public virtual int GetByteCount(ReadOnlySpan<char> chars, bool flush) => GetByteCount(chars.ToArray(), 0, chars.Length, flush);

        // Encodes a range of characters in a character array into a range of bytes
        // in a byte array. The method encodes charCount characters from
        // chars starting at index charIndex, storing the resulting
        // bytes in bytes starting at index byteIndex. The encoding
        // takes into account the state in which the encoder was left following the
        // last call to this method. The flush parameter indicates whether
        // the encoder should flush any shift-states and partial characters at the
        // end of the conversion. To ensure correct termination of a sequence of
        // blocks of encoded bytes, the last call to GetBytes should specify
        // a value of true for the flush parameter.
        //
        // An exception occurs if the byte array is not large enough to hold the
        // complete encoding of the characters. The GetByteCount method can
        // be used to determine the exact number of bytes that will be produced for
        // a given range of characters. Alternatively, the GetMaxByteCount
        // method of the Encoding that produced this encoder can be used to
        // determine the maximum number of bytes that will be produced for a given
        // number of characters, regardless of the actual character values.
        //
        public abstract int GetBytes(char[] chars, int charIndex, int charCount,
                                        byte[] bytes, int byteIndex, bool flush);

        // We expect this to be the workhorse for NLS Encodings, but for existing
        // ones we need a working (if slow) default implementation)
        //
        // WARNING WARNING WARNING
        //
        // WARNING: If this breaks it could be a security threat.  Obviously we
        // call this internally, so you need to make sure that your pointers, counts
        // and indexes are correct when you call this method.
        //
        // In addition, we have internal code, which will be marked as "safe" calling
        // this code.  However this code is dependent upon the implementation of an
        // external GetBytes() method, which could be overridden by a third party and
        // the results of which cannot be guaranteed.  We use that result to copy
        // the byte[] to our byte* output buffer.  If the result count was wrong, we
        // could easily overflow our output buffer.  Therefore we do an extra test
        // when we copy the buffer so that we don't overflow byteCount either.
        public virtual unsafe int GetBytes(char* chars, int charCount,
                                           byte* bytes, int byteCount, bool flush)
        {
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            // Get the char array to convert
            char[] arrChar = new char[charCount];

            for (int index = 0; index < charCount; index++)
                arrChar[index] = chars[index];

            // Get the byte array to fill
            byte[] arrByte = new byte[byteCount];

            // Do the work
            int result = GetBytes(arrChar, 0, charCount, arrByte, 0, flush);


            // Copy the byte array
            // WARNING: We MUST make sure that we don't copy too many bytes.  We can't
            // rely on result because it could be a 3rd party implementation.  We need
            // to make sure we never copy more than byteCount bytes no matter the value
            // of result
            if (result < byteCount)
                byteCount = result;

            // Don't copy too many bytes!
            for (int index = 0; index < byteCount; index++)
                bytes[index] = arrByte[index];

            return byteCount;
        }

        public virtual int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush)
        {
            byte[] arrByte = new byte[bytes.Length];
            int result = GetBytes(chars.ToArray(), 0, chars.Length, arrByte, 0, flush);
            if (result > bytes.Length) result = bytes.Length;
            new ReadOnlySpan<byte>(arrByte, 0, result).CopyTo(bytes);
            return result;
        }

        // This method is used to avoid running out of output buffer space.
        // It will encode until it runs out of chars, and then it will return
        // true if it the entire input was converted.  In either case it
        // will also return the number of converted chars and output bytes used.
        // It will only throw a buffer overflow exception if the entire length of bytes[] is
        // too small to store the next byte. (like 0 or maybe 1 or 4 for some encodings)
        // We're done processing this buffer only if completed returns true.
        //
        // Might consider checking Max...Count to avoid the extra counting step.
        //
        // Note that if all of the input chars are not consumed, then we'll do a /2, which means
        // that its likely that we didn't consume as many chars as we could have.  For some
        // applications this could be slow.  (Like trying to exactly fill an output buffer from a bigger stream)
        public virtual void Convert(char[] chars, int charIndex, int charCount,
                                    byte[] bytes, int byteIndex, int byteCount, bool flush,
                                    out int charsUsed, out int bytesUsed, out bool completed)
        {
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            if (chars.Length - charIndex < charCount)
                throw new ArgumentOutOfRangeException(nameof(chars),
                      "Index and count must refer to a location within the buffer.");

            if (bytes.Length - byteIndex < byteCount)
                throw new ArgumentOutOfRangeException(nameof(bytes),
                      "Index and count must refer to a location within the buffer.");

            charsUsed = charCount;

            // Its easy to do if it won't overrun our buffer.
            // Note: We don't want to call unsafe version because that might be an untrusted version
            // which could be really unsafe and we don't want to mix it up.
            while (charsUsed > 0)
            {
                if (GetByteCount(chars, charIndex, charsUsed, flush) <= byteCount)
                {
                    bytesUsed = GetBytes(chars, charIndex, charsUsed, bytes, byteIndex, flush);
                    completed = (charsUsed == charCount &&
                        (_fallbackBuffer == null || _fallbackBuffer.Remaining == 0));
                    return;
                }

                // Try again with 1/2 the count, won't flush then 'cause won't read it all
                flush = false;
                charsUsed /= 2;
            }

            // Oops, we didn't have anything, we'll have to throw an overflow
            throw new ArgumentException("Conversion buffer overflow.");
        }

        // Same thing, but using pointers
        //
        // Might consider checking Max...Count to avoid the extra counting step.
        //
        // Note that if all of the input chars are not consumed, then we'll do a /2, which means
        // that its likely that we didn't consume as many chars as we could have.  For some
        // applications this could be slow.  (Like trying to exactly fill an output buffer from a bigger stream)
        public virtual unsafe void Convert(char* chars, int charCount,
                                           byte* bytes, int byteCount, bool flush,
                                           out int charsUsed, out int bytesUsed, out bool completed)
        {
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            // Get ready to do it
            charsUsed = charCount;

            // Its easy to do if it won't overrun our buffer.
            while (charsUsed > 0)
            {
                if (GetByteCount(chars, charsUsed, flush) <= byteCount)
                {
                    bytesUsed = GetBytes(chars, charsUsed, bytes, byteCount, flush);
                    completed = (charsUsed == charCount &&
                        (_fallbackBuffer == null || _fallbackBuffer.Remaining == 0));
                    return;
                }

                // Try again with 1/2 the count, won't flush then 'cause won't read it all
                flush = false;
                charsUsed /= 2;
            }

            // Oops, we didn't have anything, we'll have to throw an overflow
            throw new ArgumentException("Conversion buffer overflow.");
        }

        public virtual void Convert(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            byte[] arrByte = new byte[bytes.Length];
            Convert(chars.ToArray(), 0, chars.Length, arrByte, 0, bytes.Length, flush, out charsUsed, out bytesUsed, out completed);
            new ReadOnlySpan<byte>(arrByte, 0, bytesUsed).CopyTo(bytes);
        }
    }
}
