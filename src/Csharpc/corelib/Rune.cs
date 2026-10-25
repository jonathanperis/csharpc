// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Text.Rune, after dotnet/runtime's src/libraries/System.Private.CoreLib/src/System/Text/Rune.cs
// (release/11.0; see THIRD-PARTY-NOTICES.md): construction and validation, UTF-16/UTF-8 decoding with
// .NET's maximal-subpart rules, encoding, classification and casing (through the corelib's char data),
// with .NET's exception types, messages and parameter names. ASP.NET Core's URL decoder and
// System.Text.Encodings.Web walk text rune by rune.
#nullable disable
using System;
using System.Buffers;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Rune")]
    public readonly struct RuneImpl : IComparable, IComparable<RuneImpl>, IEquatable<RuneImpl>, IFormattable
    {
        private const int MaxUtf16CharsPerRune = 2;
        private const uint HighSurrogateStart = 0xD800;
        private const uint LowSurrogateStart = 0xDC00;
        private const uint HighSurrogateRange = 0x3FF;

        private readonly uint _value;

        public RuneImpl(char ch)
        {
            uint expanded = ch;
            if (IsSurrogateCodePoint(expanded)) throw new ArgumentOutOfRangeException("ch");
            _value = expanded;
        }

        public RuneImpl(char highSurrogate, char lowSurrogate) : this((uint)char.ConvertToUtf32(highSurrogate, lowSurrogate), false) { }

        public RuneImpl(int value) : this((uint)value) { }

        public RuneImpl(uint value)
        {
            if (!IsValidUnicodeScalar(value)) throw new ArgumentOutOfRangeException("value");
            _value = value;
        }

        private RuneImpl(uint scalarValue, bool _) => _value = scalarValue;

        public static bool operator ==(RuneImpl left, RuneImpl right) => left._value == right._value;
        public static bool operator !=(RuneImpl left, RuneImpl right) => left._value != right._value;
        public static bool operator <(RuneImpl left, RuneImpl right) => left._value < right._value;
        public static bool operator <=(RuneImpl left, RuneImpl right) => left._value <= right._value;
        public static bool operator >(RuneImpl left, RuneImpl right) => left._value > right._value;
        public static bool operator >=(RuneImpl left, RuneImpl right) => left._value >= right._value;

        public static explicit operator RuneImpl(char ch) => new RuneImpl(ch);
        public static explicit operator RuneImpl(uint value) => new RuneImpl(value);
        public static explicit operator RuneImpl(int value) => new RuneImpl(value);

        public bool IsAscii => _value <= 0x7Fu;
        public bool IsBmp => _value <= 0xFFFFu;
        public int Plane => (int)(_value >> 16);
        public static RuneImpl ReplacementChar => UnsafeCreate(0xFFFD);
        public int Utf16SequenceLength => _value <= 0xFFFFu ? 1 : 2;
        public int Utf8SequenceLength => _value <= 0x7Fu ? 1 : _value <= 0x7FFu ? 2 : _value <= 0xFFFFu ? 3 : 4;
        public int Value => (int)_value;

        private static bool IsSurrogateCodePoint(uint value) => value - 0xD800u <= 0x7FFu;
        private static bool IsValidUnicodeScalar(uint value) => ((value - 0x110000u) ^ 0xD800u) >= 0xFFEF0800u;
        internal static RuneImpl UnsafeCreate(uint scalarValue) => new RuneImpl(scalarValue, false);

        public int CompareTo(RuneImpl other) => Value - other.Value;

        int IComparable.CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (obj is RuneImpl other) return CompareTo(other);
            throw new ArgumentException("Object must be of type Rune.");
        }

        // ---- decoding -----------------------------------------------------------------------------
        public static OperationStatus DecodeFromUtf16(ReadOnlySpan<char> source, out RuneImpl result, out int charsConsumed)
        {
            if (!source.IsEmpty)
            {
                char first = source[0];
                if (TryCreate(first, out result))
                {
                    charsConsumed = 1;
                    return OperationStatus.Done;
                }
                if (source.Length > 1)
                {
                    if (TryCreate(first, source[1], out result))
                    {
                        charsConsumed = 2;
                        return OperationStatus.Done;
                    }
                    charsConsumed = 1;
                    result = ReplacementChar;
                    return OperationStatus.InvalidData;
                }
                if (!char.IsHighSurrogate(first))
                {
                    charsConsumed = 1;
                    result = ReplacementChar;
                    return OperationStatus.InvalidData;
                }
            }
            charsConsumed = source.Length;
            result = ReplacementChar;
            return OperationStatus.NeedMoreData;
        }

        public static OperationStatus DecodeFromUtf8(ReadOnlySpan<byte> source, out RuneImpl result, out int bytesConsumed)
        {
            // dotnet/runtime's decoder: the maximal invalid subsequence is consumed (1..3 bytes).
            int index = 0;
            if (source.IsEmpty) goto NeedsMoreData;
            uint tempValue = source[0];
            if (tempValue <= 0x7Fu)
            {
                bytesConsumed = 1;
                result = UnsafeCreate(tempValue);
                return OperationStatus.Done;
            }
            index = 1;
            if (!InRange(tempValue, 0xC2, 0xF4)) goto Invalid;
            tempValue = (tempValue - 0xC2) << 6;
            if (source.Length <= 1) goto NeedsMoreData;
            int thisByteSignExtended = (sbyte)source[1];
            if (thisByteSignExtended >= -64) goto Invalid;
            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80;
            tempValue += (0xC2 - 0xC0) << 6;
            if (tempValue < 0x0800) goto Finish;
            if (!InRange(tempValue, ((0xE0 - 0xC0) << 6) + (0xA0 - 0x80), ((0xF4 - 0xC0) << 6) + (0x8F - 0x80))) goto Invalid;
            if (InRange(tempValue, ((0xED - 0xC0) << 6) + (0xA0 - 0x80), ((0xED - 0xC0) << 6) + (0xBF - 0x80))) goto Invalid;
            if (InRange(tempValue, ((0xF0 - 0xC0) << 6) + (0x80 - 0x80), ((0xF0 - 0xC0) << 6) + (0x8F - 0x80))) goto Invalid;
            index = 2;
            if (source.Length <= 2) goto NeedsMoreData;
            thisByteSignExtended = (sbyte)source[2];
            if (thisByteSignExtended >= -64) goto Invalid;
            tempValue <<= 6;
            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80;
            tempValue -= (0xE0 - 0xC0) << 12;
            if (tempValue <= 0xFFFF) goto Finish;
            index = 3;
            if (source.Length <= 3) goto NeedsMoreData;
            thisByteSignExtended = (sbyte)source[3];
            if (thisByteSignExtended >= -64) goto Invalid;
            tempValue <<= 6;
            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80;
            tempValue -= (0xF0 - 0xE0) << 18;
        Finish:
            bytesConsumed = index + 1;
            result = UnsafeCreate(tempValue);
            return OperationStatus.Done;
        NeedsMoreData:
            bytesConsumed = index;
            result = ReplacementChar;
            return OperationStatus.NeedMoreData;
        Invalid:
            bytesConsumed = index;
            result = ReplacementChar;
            return OperationStatus.InvalidData;
        }

        private static bool InRange(uint value, uint lowerBound, uint upperBound) => value - lowerBound <= upperBound - lowerBound;

        public static OperationStatus DecodeLastFromUtf16(ReadOnlySpan<char> source, out RuneImpl result, out int charsConsumed)
        {
            int index = source.Length - 1;
            if ((uint)index < (uint)source.Length)
            {
                char finalChar = source[index];
                if (TryCreate(finalChar, out result))
                {
                    charsConsumed = 1;
                    return OperationStatus.Done;
                }
                if (char.IsLowSurrogate(finalChar))
                {
                    index--;
                    if ((uint)index < (uint)source.Length && TryCreate(source[index], finalChar, out result))
                    {
                        charsConsumed = 2;
                        return OperationStatus.Done;
                    }
                    charsConsumed = 1;
                    result = ReplacementChar;
                    return OperationStatus.InvalidData;
                }
            }
            charsConsumed = source.Length == 0 ? 0 : 1;
            result = ReplacementChar;
            return OperationStatus.NeedMoreData;
        }

        public static OperationStatus DecodeLastFromUtf8(ReadOnlySpan<byte> source, out RuneImpl value, out int bytesConsumed)
        {
            int index = source.Length - 1;
            if ((uint)index >= (uint)source.Length)
            {
                value = ReplacementChar;
                bytesConsumed = 0;
                return OperationStatus.NeedMoreData;
            }
            uint tempValue = source[index];
            if (tempValue <= 0x7Fu)
            {
                bytesConsumed = 1;
                value = UnsafeCreate(tempValue);
                return OperationStatus.Done;
            }
            if (((byte)tempValue & 0x40) != 0) return DecodeFromUtf8(source.Slice(index), out value, out bytesConsumed);
            for (int i = 3; i > 0; i--)
            {
                index--;
                if ((uint)index >= (uint)source.Length) goto Invalid;
                if ((sbyte)source[index] >= -64)
                {
                    ReadOnlySpan<byte> tail = source.Slice(index);
                    OperationStatus status = DecodeFromUtf8(tail, out RuneImpl tempRune, out int tempBytesConsumed);
                    if (tempBytesConsumed == tail.Length)
                    {
                        bytesConsumed = tempBytesConsumed;
                        value = tempRune;
                        return status;
                    }
                    goto Invalid;
                }
            }
        Invalid:
            value = ReplacementChar;
            bytesConsumed = 1;
            return OperationStatus.InvalidData;
        }

        // ---- encoding -----------------------------------------------------------------------------
        public int EncodeToUtf16(Span<char> destination)
        {
            if (!TryEncodeToUtf16(destination, out int charsWritten)) throw new ArgumentException("Destination is too short.", "destination");
            return charsWritten;
        }

        public int EncodeToUtf8(Span<byte> destination)
        {
            if (!TryEncodeToUtf8(destination, out int bytesWritten)) throw new ArgumentException("Destination is too short.", "destination");
            return bytesWritten;
        }

        public bool TryEncodeToUtf16(Span<char> destination, out int charsWritten)
        {
            if (!destination.IsEmpty)
            {
                if (IsBmp)
                {
                    destination[0] = (char)_value;
                    charsWritten = 1;
                    return true;
                }
                if (destination.Length > 1)
                {
                    destination[0] = (char)((_value + ((0xD800u - 0x40u) << 10)) >> 10);
                    destination[1] = (char)((_value & 0x3FFu) + 0xDC00u);
                    charsWritten = 2;
                    return true;
                }
            }
            charsWritten = 0;
            return false;
        }

        public bool TryEncodeToUtf8(Span<byte> destination, out int bytesWritten)
        {
            uint v = _value;
            if (!destination.IsEmpty)
            {
                if (v <= 0x7Fu)
                {
                    destination[0] = (byte)v;
                    bytesWritten = 1;
                    return true;
                }
                if (destination.Length > 1)
                {
                    if (v <= 0x7FFu)
                    {
                        destination[0] = (byte)((v + (0b110u << 11)) >> 6);
                        destination[1] = (byte)((v & 0x3Fu) + 0x80u);
                        bytesWritten = 2;
                        return true;
                    }
                    if (destination.Length > 2)
                    {
                        if (v <= 0xFFFFu)
                        {
                            destination[0] = (byte)((v + (0b1110u << 16)) >> 12);
                            destination[1] = (byte)(((v & (0x3Fu << 6)) >> 6) + 0x80u);
                            destination[2] = (byte)((v & 0x3Fu) + 0x80u);
                            bytesWritten = 3;
                            return true;
                        }
                        if (destination.Length > 3)
                        {
                            destination[0] = (byte)((v + (0b11110u << 21)) >> 18);
                            destination[1] = (byte)(((v & (0x3Fu << 12)) >> 12) + 0x80u);
                            destination[2] = (byte)(((v & (0x3Fu << 6)) >> 6) + 0x80u);
                            destination[3] = (byte)((v & 0x3Fu) + 0x80u);
                            bytesWritten = 4;
                            return true;
                        }
                    }
                }
            }
            bytesWritten = 0;
            return false;
        }

        // ---- creation -----------------------------------------------------------------------------
        public static bool TryCreate(char ch, out RuneImpl result)
        {
            uint extended = ch;
            if (!IsSurrogateCodePoint(extended))
            {
                result = UnsafeCreate(extended);
                return true;
            }
            result = default;
            return false;
        }

        public static bool TryCreate(char highSurrogate, char lowSurrogate, out RuneImpl result)
        {
            uint highOffset = (uint)highSurrogate - HighSurrogateStart;
            uint lowOffset = (uint)lowSurrogate - LowSurrogateStart;
            if ((highOffset | lowOffset) <= HighSurrogateRange)
            {
                result = UnsafeCreate((highOffset << 10) + ((uint)lowSurrogate - LowSurrogateStart) + (0x40u << 10));
                return true;
            }
            result = default;
            return false;
        }

        public static bool TryCreate(int value, out RuneImpl result) => TryCreate((uint)value, out result);

        public static bool TryCreate(uint value, out RuneImpl result)
        {
            if (IsValidUnicodeScalar(value))
            {
                result = UnsafeCreate(value);
                return true;
            }
            result = default;
            return false;
        }

        public static bool IsValid(int value) => IsValidUnicodeScalar((uint)value);
        public static bool IsValid(uint value) => IsValidUnicodeScalar(value);

        public static RuneImpl GetRuneAt(string input, int index)
        {
            int value = ReadRuneFromString(input, index);
            if (value < 0) throw new ArgumentException("Cannot extract a Unicode scalar value from the specified index in the input.", "index");
            return UnsafeCreate((uint)value);
        }

        public static bool TryGetRuneAt(string input, int index, out RuneImpl value)
        {
            int read = ReadRuneFromString(input, index);
            if (read >= 0)
            {
                value = UnsafeCreate((uint)read);
                return true;
            }
            value = default;
            return false;
        }

        private static int ReadRuneFromString(string input, int index)
        {
            if (input == null) throw new ArgumentNullException("input");
            if ((uint)index >= (uint)input.Length)
                throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
            uint value = input[index];
            if (IsSurrogateCodePoint(value))
            {
                if (value > 0xDBFFu) return -1;
                index++;
                if ((uint)index >= (uint)input.Length) return -1;
                uint low = input[index];
                if (low - 0xDC00u > 0x3FFu) return -1;
                value = (value << 10) + low - ((0xD800u << 10) + 0xDC00u - (1u << 16));
            }
            return (int)value;
        }

        // ---- equality and text --------------------------------------------------------------------
        public override bool Equals(object obj) => obj is RuneImpl other && this == other;
        public bool Equals(RuneImpl other) => this == other;

        public bool Equals(RuneImpl other, StringComparison comparisonType) =>
            comparisonType == StringComparison.Ordinal ? this == other : string.Equals(ToString(), other.ToString(), comparisonType);

        public override int GetHashCode() => Value;

        public override string ToString()
        {
            if (IsBmp) return ((char)_value).ToString();
            Span<char> buffer = stackalloc char[MaxUtf16CharsPerRune];
            TryEncodeToUtf16(buffer, out _);
            return new string(buffer);
        }

        string IFormattable.ToString(string format, IFormatProvider formatProvider) => ToString();

        // ---- classification and casing ------------------------------------------------------------
        public static double GetNumericValue(RuneImpl value)
        {
            if (value.IsAscii)
            {
                uint digit = value._value - '0';
                return digit <= 9 ? digit : -1;
            }
            // The corelib has no Unicode numeric-value table yet: say so rather than answer -1 for '½' or '٣'.
            throw new NotSupportedException("Rune.GetNumericValue of a non-ASCII value is not supported by csharpc yet.");
        }

        public static UnicodeCategory GetUnicodeCategory(RuneImpl value) =>
            value.IsBmp ? CharOps.GetUnicodeCategory((char)value._value) : CharOps.CategoryAt(value.ToString(), 0);

        private static bool CategoryIn(RuneImpl value, UnicodeCategory first, UnicodeCategory last) =>
            InRange((uint)GetUnicodeCategory(value), (uint)first, (uint)last);

        public static bool IsControl(RuneImpl value) => ((value._value + 1) & ~0x80u) <= 0x20u;
        public static bool IsDigit(RuneImpl value) => value.IsAscii ? InRange(value._value, '0', '9') : GetUnicodeCategory(value) == UnicodeCategory.DecimalDigitNumber;
        public static bool IsLetter(RuneImpl value) =>
            value.IsAscii ? ((value._value - 'A') & ~0x20u) <= (uint)('Z' - 'A') : CategoryIn(value, UnicodeCategory.UppercaseLetter, UnicodeCategory.OtherLetter);
        public static bool IsLetterOrDigit(RuneImpl value) =>
            CategoryIn(value, UnicodeCategory.UppercaseLetter, UnicodeCategory.OtherLetter) || GetUnicodeCategory(value) == UnicodeCategory.DecimalDigitNumber;
        public static bool IsLower(RuneImpl value) => value.IsAscii ? InRange(value._value, 'a', 'z') : GetUnicodeCategory(value) == UnicodeCategory.LowercaseLetter;
        public static bool IsNumber(RuneImpl value) => value.IsAscii ? InRange(value._value, '0', '9') : CategoryIn(value, UnicodeCategory.DecimalDigitNumber, UnicodeCategory.OtherNumber);
        public static bool IsPunctuation(RuneImpl value) => CategoryIn(value, UnicodeCategory.ConnectorPunctuation, UnicodeCategory.OtherPunctuation);
        public static bool IsSeparator(RuneImpl value) => CategoryIn(value, UnicodeCategory.SpaceSeparator, UnicodeCategory.ParagraphSeparator);
        public static bool IsSymbol(RuneImpl value) => CategoryIn(value, UnicodeCategory.MathSymbol, UnicodeCategory.OtherSymbol);
        public static bool IsUpper(RuneImpl value) => value.IsAscii ? InRange(value._value, 'A', 'Z') : GetUnicodeCategory(value) == UnicodeCategory.UppercaseLetter;
        public static bool IsWhiteSpace(RuneImpl value) => value.IsBmp && char.IsWhiteSpace((char)value._value);

        // Simple case mappings keep a rune in its plane, so the changed text is one rune again.
        public static RuneImpl ToLowerInvariant(RuneImpl value) =>
            value.IsBmp ? UnsafeCreate(char.ToLowerInvariant((char)value._value)) : FromText(value.ToString().ToLowerInvariant());
        public static RuneImpl ToUpperInvariant(RuneImpl value) =>
            value.IsBmp ? UnsafeCreate(char.ToUpperInvariant((char)value._value)) : FromText(value.ToString().ToUpperInvariant());

        public static RuneImpl ToLower(RuneImpl value, CultureInfo culture)
        {
            if (culture == null) throw new ArgumentNullException("culture");
            return FromText(culture.TextInfo.ToLower(value.ToString()));
        }

        public static RuneImpl ToUpper(RuneImpl value, CultureInfo culture)
        {
            if (culture == null) throw new ArgumentNullException("culture");
            return FromText(culture.TextInfo.ToUpper(value.ToString()));
        }

        private static RuneImpl FromText(string text) =>
            text.Length == 1 ? UnsafeCreate(text[0]) : UnsafeCreate((uint)char.ConvertToUtf32(text[0], text[1]));
    }

    /// <summary>string.EnumerateRunes() (StringRuneEnumerator.cs): an ill-formed surrogate is U+FFFD, one char long.</summary>
    [Shadows("System.Text.StringRuneEnumerator")]
    public struct StringRuneEnumeratorImpl : System.Collections.Generic.IEnumerable<System.Text.Rune>, System.Collections.Generic.IEnumerator<System.Text.Rune>
    {
        private readonly string _string;
        private System.Text.Rune _current;
        private int _nextIndex;

        internal StringRuneEnumeratorImpl(string value)
        {
            _string = value;
            _current = default;
            _nextIndex = 0;
        }

        public System.Text.Rune Current => _current;
        public StringRuneEnumeratorImpl GetEnumerator() => this;

        public bool MoveNext()
        {
            if ((uint)_nextIndex >= (uint)_string.Length)
            {
                _current = default;
                return false;
            }
            if (!System.Text.Rune.TryGetRuneAt(_string, _nextIndex, out _current)) _current = System.Text.Rune.ReplacementChar;
            _nextIndex += _current.Utf16SequenceLength;
            return true;
        }

        object System.Collections.IEnumerator.Current => _current;
        void IDisposable.Dispose() { }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this;
        System.Collections.Generic.IEnumerator<System.Text.Rune> System.Collections.Generic.IEnumerable<System.Text.Rune>.GetEnumerator() => this;
        void System.Collections.IEnumerator.Reset()
        {
            _current = default;
            _nextIndex = 0;
        }
    }

    /// <summary>span.EnumerateRunes() (SpanRuneEnumerator.cs): ill-formed UTF-16 decodes to U+FFFD.</summary>
    [Shadows("System.Text.SpanRuneEnumerator")]
    public ref struct SpanRuneEnumeratorImpl : System.Collections.Generic.IEnumerator<System.Text.Rune>
    {
        private ReadOnlySpan<char> _remaining;
        private System.Text.Rune _current;

        internal SpanRuneEnumeratorImpl(ReadOnlySpan<char> buffer)
        {
            _remaining = buffer;
            _current = default;
        }

        public System.Text.Rune Current => _current;
        public SpanRuneEnumeratorImpl GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                _current = default;
                return false;
            }
            System.Text.Rune.DecodeFromUtf16(_remaining, out _current, out int charsConsumed);
            _remaining = _remaining.Slice(charsConsumed);
            return true;
        }

        // .NET 9+: a ref struct implementing the enumerator interfaces (foreach in IL disposes it through IDisposable).
        object System.Collections.IEnumerator.Current => throw new NotSupportedException();
        void System.Collections.IEnumerator.Reset() => throw new NotSupportedException();
        void IDisposable.Dispose() { }
    }

    [ShadowMembers("System.String")]
    internal static class StringRunes
    {
        [ShadowInstance] public static StringRuneEnumeratorImpl EnumerateRunes(string s) => new StringRuneEnumeratorImpl(s);
    }

    [ShadowMembers("System.MemoryExtensions")]
    internal static class SpanRunes
    {
        public static SpanRuneEnumeratorImpl EnumerateRunes(ReadOnlySpan<char> span) => new SpanRuneEnumeratorImpl(span);
        public static SpanRuneEnumeratorImpl EnumerateRunes(Span<char> span) => new SpanRuneEnumeratorImpl(span);
    }
}
