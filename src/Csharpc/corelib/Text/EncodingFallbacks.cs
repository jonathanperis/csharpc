// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/Text/
// {Encoder,Decoder}Fallback.cs, {Encoder,Decoder}ReplacementFallback.cs and {Encoder,Decoder}ExceptionFallback.cs;
// see THIRD-PARTY-NOTICES.md. The public fallback model (classes code derives from: System.Private.Xml's
// CharEntityEncoderFallback), without .NET's internal fast paths; the corelib's encodings call a fallback
// for what they can't encode or decode (Encoding.cs).
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.EncoderFallback")]
    public abstract class EncoderFallbackImpl
    {
        public static EncoderFallback ReplacementFallback => EncoderReplacementFallbackImpl.s_default;
        public static EncoderFallback ExceptionFallback => EncoderExceptionFallbackImpl.s_default;

        public abstract EncoderFallbackBuffer CreateFallbackBuffer();
        public abstract int MaxCharCount { get; }
    }

    [Shadows("System.Text.EncoderFallbackBuffer")]
    public abstract class EncoderFallbackBufferImpl
    {
        public abstract bool Fallback(char charUnknown, int index);
        public abstract bool Fallback(char charUnknownHigh, char charUnknownLow, int index);
        public abstract char GetNextChar();
        public abstract bool MovePrevious();
        public abstract int Remaining { get; }

        public virtual void Reset()
        {
            while (GetNextChar() != (char)0) { }
        }

        internal static void ThrowLastCharRecursive(int charRecursive) =>
            throw new ArgumentException(string.Format("Recursive fallback not allowed for character \\\\u{0:X4}.", charRecursive), "chars");
    }

    [Shadows("System.Text.EncoderReplacementFallback")]
    public sealed class EncoderReplacementFallbackImpl : EncoderFallback
    {
        internal static readonly EncoderFallback s_default = (EncoderFallback)(object)new EncoderReplacementFallbackImpl();
        private readonly string _strDefault;

        public EncoderReplacementFallbackImpl() : this("?") { }

        public EncoderReplacementFallbackImpl(string replacement)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            if (!IsWellFormed(replacement)) throw new ArgumentException("String contains invalid Unicode code points.", "replacement");
            _strDefault = replacement;
        }

        /// <summary>No lone surrogate (the check .NET's constructors make).</summary>
        internal static bool IsWellFormed(string s)
        {
            bool foundHigh = false;
            foreach (char c in s)
            {
                if (char.IsSurrogate(c))
                {
                    if (char.IsHighSurrogate(c))
                    {
                        if (foundHigh) return false;
                        foundHigh = true;
                    }
                    else
                    {
                        if (!foundHigh) return false;
                        foundHigh = false;
                    }
                }
                else if (foundHigh) return false;
            }
            return !foundHigh;
        }

        public string DefaultString => _strDefault;
        public override EncoderFallbackBuffer CreateFallbackBuffer() => (EncoderFallbackBuffer)(object)new EncoderReplacementFallbackBufferImpl(this);
        public override int MaxCharCount => _strDefault.Length;
        public override bool Equals(object value) => value is EncoderReplacementFallback that && _strDefault == that.DefaultString;
        public override int GetHashCode() => _strDefault.GetHashCode();
    }

    [Shadows("System.Text.EncoderReplacementFallbackBuffer")]
    public sealed class EncoderReplacementFallbackBufferImpl : EncoderFallbackBuffer
    {
        private readonly string _strDefault;
        private int _fallbackCount = -1;
        private int _fallbackIndex = -1;

        public EncoderReplacementFallbackBufferImpl(EncoderReplacementFallbackImpl fallback)
        {
            // Twice: a surrogate pair falls back with the string once per char.
            _strDefault = fallback.DefaultString + fallback.DefaultString;
        }

        public override bool Fallback(char charUnknown, int index)
        {
            if (_fallbackCount >= 1)
            {
                if (char.IsHighSurrogate(charUnknown) && _fallbackCount >= 0 && char.IsLowSurrogate(_strDefault[_fallbackIndex + 1]))
                    EncoderFallbackBufferImpl.ThrowLastCharRecursive(char.ConvertToUtf32(charUnknown, _strDefault[_fallbackIndex + 1]));
                EncoderFallbackBufferImpl.ThrowLastCharRecursive(unchecked((int)charUnknown));
            }
            _fallbackCount = _strDefault.Length / 2;
            _fallbackIndex = -1;
            return _fallbackCount != 0;
        }

        public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
        {
            if (!char.IsHighSurrogate(charUnknownHigh))
                throw new ArgumentOutOfRangeException("charUnknownHigh", string.Format("Valid values are between {0} and {1}, inclusive.", 0xD800, 0xDBFF));
            if (!char.IsLowSurrogate(charUnknownLow))
                throw new ArgumentOutOfRangeException("charUnknownLow", string.Format("Valid values are between {0} and {1}, inclusive.", 0xDC00, 0xDFFF));
            if (_fallbackCount >= 1) EncoderFallbackBufferImpl.ThrowLastCharRecursive(char.ConvertToUtf32(charUnknownHigh, charUnknownLow));
            _fallbackCount = _strDefault.Length;
            _fallbackIndex = -1;
            return _fallbackCount != 0;
        }

        public override char GetNextChar()
        {
            _fallbackCount--;
            _fallbackIndex++;
            if (_fallbackCount < 0) return '\0';
            if (_fallbackCount == int.MaxValue)
            {
                _fallbackCount = -1;
                return '\0';
            }
            return _strDefault[_fallbackIndex];
        }

        public override bool MovePrevious()
        {
            if (_fallbackCount >= -1 && _fallbackIndex >= 0)
            {
                _fallbackIndex--;
                _fallbackCount++;
                return true;
            }
            return false;
        }

        public override int Remaining => _fallbackCount < 0 ? 0 : _fallbackCount;

        public override void Reset()
        {
            _fallbackCount = -1;
            _fallbackIndex = 0;
        }
    }

    [Shadows("System.Text.EncoderExceptionFallback")]
    public sealed class EncoderExceptionFallbackImpl : EncoderFallback
    {
        internal static readonly EncoderFallback s_default = (EncoderFallback)(object)new EncoderExceptionFallbackImpl();
        public EncoderExceptionFallbackImpl() { }
        public override EncoderFallbackBuffer CreateFallbackBuffer() => (EncoderFallbackBuffer)(object)new EncoderExceptionFallbackBufferImpl();
        public override int MaxCharCount => 0;
        public override bool Equals(object value) => value is EncoderExceptionFallback;
        public override int GetHashCode() => 654;
    }

    [Shadows("System.Text.EncoderExceptionFallbackBuffer")]
    public sealed class EncoderExceptionFallbackBufferImpl : EncoderFallbackBuffer
    {
        public EncoderExceptionFallbackBufferImpl() { }

        public override bool Fallback(char charUnknown, int index) =>
            throw (Exception)(object)new EncoderFallbackExceptionImpl(
                string.Format("Unable to translate Unicode character \\\\u{0:X4} at index {1} to specified code page.", (int)charUnknown, index), charUnknown, index);

        public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
        {
            if (!char.IsHighSurrogate(charUnknownHigh))
                throw new ArgumentOutOfRangeException("charUnknownHigh", string.Format("Valid values are between {0} and {1}, inclusive.", 0xD800, 0xDBFF));
            if (!char.IsLowSurrogate(charUnknownLow))
                throw new ArgumentOutOfRangeException("charUnknownLow", string.Format("Valid values are between {0} and {1}, inclusive.", 0xDC00, 0xDFFF));
            int scalar = char.ConvertToUtf32(charUnknownHigh, charUnknownLow);
            throw (Exception)(object)new EncoderFallbackExceptionImpl(
                string.Format("Unable to translate Unicode character \\\\u{0:X4} at index {1} to specified code page.", scalar, index), charUnknownHigh, charUnknownLow, index);
        }

        public override char GetNextChar() => (char)0;
        public override bool MovePrevious() => false;
        public override int Remaining => 0;
    }

    [Shadows("System.Text.EncoderFallbackException")]
    public sealed class EncoderFallbackExceptionImpl : ArgumentException
    {
        private readonly char _charUnknown, _charUnknownHigh, _charUnknownLow;
        private readonly int _index;

        public EncoderFallbackExceptionImpl() : base("Value does not fall within the expected range.") { }
        public EncoderFallbackExceptionImpl(string message) : base(message) { }
        public EncoderFallbackExceptionImpl(string message, Exception innerException) : base(message, innerException) { }

        internal EncoderFallbackExceptionImpl(string message, char charUnknown, int index) : base(message)
        {
            _charUnknown = charUnknown;
            _index = index;
        }

        internal EncoderFallbackExceptionImpl(string message, char charUnknownHigh, char charUnknownLow, int index) : base(message)
        {
            _charUnknownHigh = charUnknownHigh;
            _charUnknownLow = charUnknownLow;
            _index = index;
        }

        public char CharUnknown => _charUnknown;
        public char CharUnknownHigh => _charUnknownHigh;
        public char CharUnknownLow => _charUnknownLow;
        public int Index => _index;
        public bool IsUnknownSurrogate() => _charUnknownHigh != '\0';
    }

    // ---- decoding -----------------------------------------------------------------------------

    [Shadows("System.Text.DecoderFallback")]
    public abstract class DecoderFallbackImpl
    {
        public static DecoderFallback ReplacementFallback => DecoderReplacementFallbackImpl.s_default;
        public static DecoderFallback ExceptionFallback => DecoderExceptionFallbackImpl.s_default;

        public abstract DecoderFallbackBuffer CreateFallbackBuffer();
        public abstract int MaxCharCount { get; }
    }

    [Shadows("System.Text.DecoderFallbackBuffer")]
    public abstract class DecoderFallbackBufferImpl
    {
        public abstract bool Fallback(byte[] bytesUnknown, int index);
        public abstract char GetNextChar();
        public abstract bool MovePrevious();
        public abstract int Remaining { get; }

        public virtual void Reset()
        {
            while (GetNextChar() != (char)0) { }
        }
    }

    [Shadows("System.Text.DecoderReplacementFallback")]
    public sealed class DecoderReplacementFallbackImpl : DecoderFallback
    {
        internal static readonly DecoderFallback s_default = (DecoderFallback)(object)new DecoderReplacementFallbackImpl();
        private readonly string _strDefault;

        public DecoderReplacementFallbackImpl() : this("?") { }

        public DecoderReplacementFallbackImpl(string replacement)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            if (!EncoderReplacementFallbackImpl.IsWellFormed(replacement)) throw new ArgumentException("String contains invalid Unicode code points.", "replacement");
            _strDefault = replacement;
        }

        public string DefaultString => _strDefault;
        public override DecoderFallbackBuffer CreateFallbackBuffer() => (DecoderFallbackBuffer)(object)new DecoderReplacementFallbackBufferImpl(this);
        public override int MaxCharCount => _strDefault.Length;
        public override bool Equals(object value) => value is DecoderReplacementFallback that && _strDefault == that.DefaultString;
        public override int GetHashCode() => _strDefault.GetHashCode();
    }

    [Shadows("System.Text.DecoderReplacementFallbackBuffer")]
    public sealed class DecoderReplacementFallbackBufferImpl : DecoderFallbackBuffer
    {
        private readonly string _strDefault;
        private int _fallbackCount = -1;
        private int _fallbackIndex = -1;

        public DecoderReplacementFallbackBufferImpl(DecoderReplacementFallbackImpl fallback) { _strDefault = fallback.DefaultString; }

        public override bool Fallback(byte[] bytesUnknown, int index)
        {
            if (_fallbackCount >= 1)
                throw new ArgumentException(string.Format("Recursive fallback not allowed for bytes {0}.", BytesText(bytesUnknown)));
            if (_strDefault.Length == 0) return false;
            _fallbackCount = _strDefault.Length;
            _fallbackIndex = -1;
            return true;
        }

        internal static string BytesText(byte[] bytes)
        {
            var sb = new StringBuilder();
            const int MaxLength = 20;
            for (int i = 0; bytes != null && i < bytes.Length && i < MaxLength; i++) sb.Append('[').Append(bytes[i].ToString("X2")).Append(']');
            if (bytes != null && bytes.Length > MaxLength) sb.Append(" ...");
            return sb.ToString();
        }

        public override char GetNextChar()
        {
            _fallbackCount--;
            _fallbackIndex++;
            if (_fallbackCount < 0) return '\0';
            if (_fallbackCount == int.MaxValue)
            {
                _fallbackCount = -1;
                return '\0';
            }
            return _strDefault[_fallbackIndex];
        }

        public override bool MovePrevious()
        {
            if (_fallbackCount >= -1 && _fallbackIndex >= 0)
            {
                _fallbackIndex--;
                _fallbackCount++;
                return true;
            }
            return false;
        }

        public override int Remaining => _fallbackCount < 0 ? 0 : _fallbackCount;

        public override void Reset()
        {
            _fallbackCount = -1;
            _fallbackIndex = -1;
        }
    }

    [Shadows("System.Text.DecoderExceptionFallback")]
    public sealed class DecoderExceptionFallbackImpl : DecoderFallback
    {
        internal static readonly DecoderFallback s_default = (DecoderFallback)(object)new DecoderExceptionFallbackImpl();
        public override DecoderFallbackBuffer CreateFallbackBuffer() => (DecoderFallbackBuffer)(object)new DecoderExceptionFallbackBufferImpl();
        public override int MaxCharCount => 0;
        public override bool Equals(object value) => value is DecoderExceptionFallback;
        public override int GetHashCode() => 879;
    }

    [Shadows("System.Text.DecoderExceptionFallbackBuffer")]
    public sealed class DecoderExceptionFallbackBufferImpl : DecoderFallbackBuffer
    {
        public override bool Fallback(byte[] bytesUnknown, int index) =>
            throw (Exception)(object)new DecoderFallbackExceptionImpl(
                string.Format("Unable to translate bytes {0} at index {1} from specified code page to Unicode.", DecoderReplacementFallbackBufferImpl.BytesText(bytesUnknown ?? new byte[0]), index),
                bytesUnknown, index);

        public override char GetNextChar() => (char)0;
        public override bool MovePrevious() => false;
        public override int Remaining => 0;
    }

    [Shadows("System.Text.DecoderFallbackException")]
    public sealed class DecoderFallbackExceptionImpl : ArgumentException
    {
        private readonly byte[] _bytesUnknown;
        private readonly int _index;

        public DecoderFallbackExceptionImpl() : base("Value does not fall within the expected range.") { }
        public DecoderFallbackExceptionImpl(string message) : base(message) { }
        public DecoderFallbackExceptionImpl(string message, Exception innerException) : base(message, innerException) { }

        public DecoderFallbackExceptionImpl(string message, byte[] bytesUnknown, int index) : base(message)
        {
            _bytesUnknown = bytesUnknown;
            _index = index;
        }

        public byte[] BytesUnknown => _bytesUnknown;
        public int Index => _index;
    }
}
