// System.Text.Json shared pieces: JsonException, the reader exception, exception messages,
// the depth bit stack and constants.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json (release/11.0).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Text;
using System.Text.Json;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.JsonException")]
    public class JsonExceptionImpl : Exception
    {
        // The message mutates when the serializer adds path information (as in .NET).
        internal string _message;

        public JsonExceptionImpl(string message, string path, long? lineNumber, long? bytePositionInLine, Exception innerException)
            : base(message, innerException)
        {
            _message = message;
            LineNumber = lineNumber;
            BytePositionInLine = bytePositionInLine;
            Path = path;
        }

        public JsonExceptionImpl(string message, string path, long? lineNumber, long? bytePositionInLine) : base(message)
        {
            _message = message;
            LineNumber = lineNumber;
            BytePositionInLine = bytePositionInLine;
            Path = path;
        }

        public JsonExceptionImpl(string message, Exception innerException) : base(message, innerException)
        {
            _message = message;
        }

        public JsonExceptionImpl(string message) : base(message)
        {
            _message = message;
        }

        public JsonExceptionImpl() : base() { }

        internal bool AppendPathInformation;

        public long? LineNumber { get; internal set; }

        public long? BytePositionInLine { get; internal set; }

        public string Path { get; internal set; }

        public override string Message => _message ?? base.Message;

        internal void SetMessage(string message) => _message = message;
    }

    // System.Text.Json.JsonReaderException is internal in .NET; it is what JsonDocument.Parse and
    // Utf8JsonReader throw, so GetType() must print its name.
    [Shadows("System.Text.Json.JsonReaderException")]
    internal sealed class JsonReaderExceptionImpl : JsonExceptionImpl
    {
        public JsonReaderExceptionImpl(string message, long lineNumber, long bytePositionInLine)
            : base(message, null, lineNumber, bytePositionInLine)
        {
        }
    }

    internal static class JsonConstants
    {
        public const byte OpenBrace = (byte)'{';
        public const byte CloseBrace = (byte)'}';
        public const byte OpenBracket = (byte)'[';
        public const byte CloseBracket = (byte)']';
        public const byte Space = (byte)' ';
        public const byte CarriageReturn = (byte)'\r';
        public const byte LineFeed = (byte)'\n';
        public const byte Tab = (byte)'\t';
        public const byte ListSeparator = (byte)',';
        public const byte KeyValueSeparator = (byte)':';
        public const byte Quote = (byte)'"';
        public const byte BackSlash = (byte)'\\';
        public const byte Slash = (byte)'/';
        public const byte BackSpace = (byte)'\b';
        public const byte FormFeed = (byte)'\f';
        public const byte Asterisk = (byte)'*';
        public const byte Colon = (byte)':';
        public const byte Period = (byte)'.';
        public const byte Plus = (byte)'+';
        public const byte Hyphen = (byte)'-';
        public const byte UtcOffsetToken = (byte)'Z';
        public const byte TimePrefix = (byte)'T';

        // Explicitly skipping ReverseSolidus since that is handled separately
        public static ReadOnlySpan<byte> EscapableChars => "\"nrt/ubf"u8;
        public static ReadOnlySpan<byte> Delimiters => ",}] \n\r\t/"u8;
        public static ReadOnlySpan<byte> TrueValue => "true"u8;
        public static ReadOnlySpan<byte> FalseValue => "false"u8;
        public static ReadOnlySpan<byte> NullValue => "null"u8;
        public const byte StartingByteOfNonStandardSeparator = 0xE2;

        public const int RemoveFlagsBitMask = 0x7FFFFFFF;

        // In the worst case, an ASCII character represented as a single utf-8 byte could expand 6x when escaped.
        public const int MaxExpansionFactorWhileEscaping = 6;
        // In the worst case, a single UTF-16 character could be expanded to 3 UTF-8 bytes.
        public const int MaxExpansionFactorWhileTranscoding = 3;

        public const int MaxEscapedTokenSize = 1_000_000_000;   // Max size for already escaped value.
        public const int MaxUnescapedTokenSize = MaxEscapedTokenSize / MaxExpansionFactorWhileEscaping;  // 166_666_666 bytes
        public const int MaxCharacterTokenSize = MaxEscapedTokenSize / MaxExpansionFactorWhileEscaping; // 166_666_666 characters

        public const int MaximumFormatDateTimeOffsetLength = 33;  // StandardFormat 'O', e.g. 2017-06-12T05:30:45.7680000-07:00
        public const int MaximumDateTimeOffsetParseLength = 42;

        public const int DefaultMaxDepth = 64;
        public const int MaxUtf16RawValueLength = int.MaxValue / MaxExpansionFactorWhileTranscoding;
        public const int WriterDefaultMaxDepth = 1000;

        public const int UnicodePlane01StartValue = 0x10000;
        public const int HighSurrogateStartValue = 0xD800;
        public const int HighSurrogateEndValue = 0xDBFF;
        public const int LowSurrogateStartValue = 0xDC00;
        public const int LowSurrogateEndValue = 0xDFFF;
        public const int BitShiftBy10 = 0x400;

        public const int MaxDateTimeFraction = 9_999_999;
        public const int DateTimeParseNumFractionDigits = 16;
        public const int MaxDateTimeUtcOffsetHours = 14;
        public const int DateTimeNumFractionDigits = 7;
        public const int MaxDateTimeFractionDigits = 16;
        public const int MinimumDateTimeParseLength = 10; // YYYY-MM-DD
        public const int MaximumEscapedDateTimeOffsetParseLength = MaxExpansionFactorWhileEscaping * MaximumDateTimeOffsetParseLength;
    }

    /// <summary>Exception messages of System.Text.Json (Strings.resx).</summary>
    internal static class JsonSR
    {
        public const string ArrayDepthTooLarge = "The maximum configured depth of {0} has been exceeded. Cannot read next JSON array.";
        public const string CallFlushToAvoidDataLoss = "The JSON writer needs to be flushed before getting the current state. There are {0} bytes that have not been committed to the output.";
        public const string CannotReadIncompleteUTF16 = "Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.";
        public const string CannotReadInvalidUTF16 = "Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value: '{0}'.";
        public const string CannotStartObjectArrayAfterPrimitiveOrClose = "Cannot write the start of an object/array after a single JSON value or outside of an existing closed object/array. Current token type is '{0}'.";
        public const string CannotStartObjectArrayWithoutProperty = "Cannot write the start of an object or array without a property name. Current token type is '{0}'.";
        public const string CannotTranscodeInvalidUtf8 = "Cannot transcode invalid UTF-8 JSON text to UTF-16 string.";
        public const string CannotDecodeInvalidBase64 = "Cannot decode JSON text that is not encoded as valid Base64 to bytes.";
        public const string CannotTranscodeInvalidUtf16 = "Cannot transcode invalid UTF-16 string to UTF-8 JSON text.";
        public const string CannotEncodeInvalidUTF16 = "Cannot encode invalid UTF-16 text as JSON. Invalid surrogate value: '{0}'.";
        public const string CannotEncodeInvalidUTF8 = "Cannot encode invalid UTF-8 text as JSON. Invalid input: '{0}'.";
        public const string CannotWritePropertyWithinArray = "Cannot write a JSON property within an array or as the first JSON token. Current token type is '{0}'.";
        public const string CannotWritePropertyAfterProperty = "Cannot write a JSON property name following another property name. A JSON value is missing.";
        public const string CannotWriteValueAfterPrimitiveOrClose = "Cannot write a JSON value after a single JSON value or outside of an existing closed object/array. Current token type is '{0}'.";
        public const string CannotWriteValueWithinObject = "Cannot write a JSON value within an object without a property name. Current token type is '{0}'.";
        public const string DepthTooLarge = "CurrentDepth ({0}) is equal to or larger than the maximum allowed depth of {1}. Cannot write the next JSON object or array.";
        public const string DestinationTooShort = "Destination is too short.";
        public const string EmptyJsonIsInvalid = "Writing an empty JSON payload (excluding comments) is invalid.";
        public const string EndOfCommentNotFound = "Expected end of comment, but instead reached end of data.";
        public const string EndOfStringNotFound = "Expected end of string, but instead reached end of data.";
        public const string ExpectedEndAfterSingleJson = "'{0}' is invalid after a single JSON value. Expected end of data.";
        public const string ExpectedEndOfDigitNotFound = "'{0}' is an invalid end of a number. Expected a delimiter.";
        public const string ExpectedFalse = "'{0}' is an invalid JSON literal. Expected the literal 'false'.";
        public const string ExpectedJsonTokens = "The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true.";
        public const string ExpectedOneCompleteToken = "The input does not contain any complete JSON tokens. Expected the input to have at least one valid, complete, JSON token.";
        public const string ExpectedNextDigitEValueNotFound = "'{0}' is an invalid end of a number. Expected 'E' or 'e'.";
        public const string ExpectedNull = "'{0}' is an invalid JSON literal. Expected the literal 'null'.";
        public const string ExpectedSeparatorAfterPropertyNameNotFound = "'{0}' is invalid after a property name. Expected a ':'.";
        public const string ExpectedStartOfPropertyNotFound = "'{0}' is an invalid start of a property name. Expected a '\"'.";
        public const string ExpectedStartOfPropertyOrValueNotFound = "Expected start of a property name or value, but instead reached end of data.";
        public const string ExpectedStartOfValueNotFound = "'{0}' is an invalid start of a value.";
        public const string ExpectedTrue = "'{0}' is an invalid JSON literal. Expected the literal 'true'.";
        public const string ExpectedValueAfterPropertyNameNotFound = "Expected a value, but instead reached end of data.";
        public const string FailedToGetLargerSpan = "The 'IBufferWriter' could not provide an output buffer that is large enough to continue writing.";
        public const string FoundInvalidCharacter = "'{0}' is invalid after a value. Expected either ',', '}}', or ']'.";
        public const string InvalidCast = "Cannot get the value of a token type '{0}' as a {1}.";
        public const string InvalidCharacterAfterEscapeWithinString = "'{0}' is an invalid escapable character within a JSON string. The string should be correctly escaped.";
        public const string InvalidCharacterWithinString = "'{0}' is invalid within a JSON string. The string should be correctly escaped.";
        public const string InvalidEndOfJsonNonPrimitive = "'{0}' is an invalid token type for the end of the JSON payload. Expected either 'EndArray' or 'EndObject'.";
        public const string InvalidHexCharacterWithinString = "'{0}' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.";
        public const string JsonDocumentDoesNotSupportComments = "Comments cannot be stored in a JsonDocument, only the Skip and Disallow comment handling modes are supported.";
        public const string JsonElementHasWrongType = "The requested operation requires an element of type '{0}', but the target element has type '{1}'.";
        public const string MaxDepthMustBePositive = "Max depth must be positive.";
        public const string CommentHandlingMustBeValid = "The JsonCommentHandling enum must be set to one of the supported values.";
        public const string MismatchedObjectArray = "'{0}' is invalid without a matching open.";
        public const string CannotWriteEndAfterProperty = "'{0}' is invalid following a property name.";
        public const string ObjectDepthTooLarge = "The maximum configured depth of {0} has been exceeded. Cannot read next JSON object.";
        public const string PropertyNameTooLarge = "The JSON property name of length {0} is too large and not supported.";
        public const string RequiredDigitNotFoundAfterDecimal = "'{0}' is invalid within a number, immediately after a decimal point ('.'). Expected a digit ('0'-'9').";
        public const string RequiredDigitNotFoundAfterSign = "'{0}' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').";
        public const string RequiredDigitNotFoundEndOfData = "Expected a digit ('0'-'9'), but instead reached end of data.";
        public const string SpecialNumberValuesNotSupported = ".NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work when using 'JsonSerializer', consider specifying 'JsonNumberHandling.AllowNamedFloatingPointLiterals' (see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).";
        public const string ValueTooLarge = "The JSON value of length {0} is too large and not supported.";
        public const string ZeroDepthAtEnd = "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.";
        public const string DeserializeUnableToConvertValue = "The JSON value could not be converted to {0}.";
        public const string InvalidComparison = "Cannot compare the value of a token type '{0}' to text.";
        public const string UnsupportedFormat = "The JSON value is not in a supported {0} format.";
        public const string ExpectedStartOfPropertyOrValueAfterComment = "'{0}' is an invalid start of a property name or value, after a comment.";
        public const string TrailingCommaNotAllowedBeforeArrayEnd = "The JSON array contains a trailing comma at the end which is not supported in this mode. Change the reader options.";
        public const string TrailingCommaNotAllowedBeforeObjectEnd = "The JSON object contains a trailing comma at the end which is not supported in this mode. Change the reader options.";
        public const string SerializerOptionsReadOnly = "This JsonSerializerOptions instance is read-only or has already been used in serialization or deserialization.";
        public const string StreamNotWritable = "Stream is not writable.";
        public const string CannotWriteCommentWithEmbeddedDelimiter = "Cannot write a comment value which contains the end of comment delimiter.";
        public const string InvalidCharacterAtStartOfComment = "'{0}' is invalid after '/' at the beginning of the comment. Expected either '/' or '*'.";
        public const string UnexpectedEndOfDataWhileReadingComment = "Unexpected end of data while reading a comment.";
        public const string CannotSkip = "Cannot skip tokens on partial JSON. Either get the whole payload and create a Utf8JsonReader instance where isFinalBlock is true or call TrySkip.";
        public const string NotEnoughData = "There is not enough data to read through the entire JSON array or object.";
        public const string UnexpectedEndOfLineSeparator = "Found invalid line or paragraph separator character while reading a comment.";
        public const string InvalidLeadingZeroInNumber = "Invalid leading zero before '{0}'.";
        public const string InvalidNewLine = "New line can be only \"\\n\" or \"\\r\\n\".";
        public const string InvalidIndentCharacter = "Supported indentation characters are space and horizontal tab.";
        public const string InvalidIndentSize = "Indentation size must be between {0} and {1}.";
        public const string JsonNumberExponentTooLarge = "The exponent value in the specified JSON number is too large.";
        public const string ObjectDisposed = "Cannot access a disposed object.";

        public static string Format(string format, object a) => string.Format(format, a);
        public static string Format(string format, object a, object b) => string.Format(format, a, b);
    }

    /// <summary>One bit per nesting level: true = object, false = array (BitStack.cs, simplified).</summary>
    internal struct JsonBitStack
    {
        private ulong _allocationFreeContainer;
        private int[] _array;
        private int _currentDepth;

        public int CurrentDepth => _currentDepth;

        public void PushTrue()
        {
            if (_currentDepth < 64) _allocationFreeContainer = (_allocationFreeContainer << 1) | 1;
            else PushToArray(true);
            _currentDepth++;
        }

        public void PushFalse()
        {
            if (_currentDepth < 64) _allocationFreeContainer <<= 1;
            else PushToArray(false);
            _currentDepth++;
        }

        private void PushToArray(bool value)
        {
            if (_array == null) _array = new int[2];
            int index = _currentDepth - 64;
            int elementIndex = index >> 5;
            if (elementIndex >= _array.Length)
            {
                int[] grown = new int[_array.Length * 2];
                Array.Copy(_array, grown, _array.Length);
                _array = grown;
            }
            int bit = index & 31;
            int element = _array[elementIndex];
            if (value) element |= 1 << bit; else element &= ~(1 << bit);
            _array[elementIndex] = element;
        }

        public bool Pop()
        {
            _currentDepth--;
            bool inObject;
            if (_currentDepth < 64)
            {
                _allocationFreeContainer >>= 1;
                inObject = (_allocationFreeContainer & 1) != 0;
            }
            else if (_currentDepth == 64)
            {
                inObject = (_allocationFreeContainer & 1) != 0;
            }
            else
            {
                int index = _currentDepth - 64 - 1;
                inObject = (_array[index >> 5] & (1 << (index & 31))) != 0;
            }
            return inObject;
        }

        public void SetFirstBit()
        {
            _currentDepth++;
            _allocationFreeContainer = 1;
        }

        public void ResetFirstBit()
        {
            _currentDepth++;
            _allocationFreeContainer = 0;
        }

        /// <summary>A copy that does not share the overflow array (struct copies of readers/states must be independent).</summary>
        public JsonBitStack Clone()
        {
            JsonBitStack copy = this;
            if (_array != null) copy._array = (int[])_array.Clone();
            return copy;
        }
    }

    internal static class JsonUtf8
    {
        /// <summary>UTF-8 bytes of a UTF-16 string; null when it contains an unpaired surrogate (index in badIndex).</summary>
        public static byte[] FromUtf16(string s, out int badIndex)
        {
            badIndex = -1;
            int length = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x80) length += 1;
                else if (c < 0x800) length += 2;
                else if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF) { length += 4; i++; }
                    else { badIndex = i; return null; }
                }
                else length += 3;
            }
            byte[] result = new byte[length];
            int pos = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                if (c < 0x80) result[pos++] = (byte)c;
                else if (c < 0x800)
                {
                    result[pos++] = (byte)(0xC0 | (c >> 6));
                    result[pos++] = (byte)(0x80 | (c & 0x3F));
                }
                else if (c >= 0xD800 && c <= 0xDBFF)
                {
                    int scalar = ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00) + 0x10000;
                    i++;
                    result[pos++] = (byte)(0xF0 | (scalar >> 18));
                    result[pos++] = (byte)(0x80 | ((scalar >> 12) & 0x3F));
                    result[pos++] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
                    result[pos++] = (byte)(0x80 | (scalar & 0x3F));
                }
                else
                {
                    result[pos++] = (byte)(0xE0 | (c >> 12));
                    result[pos++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    result[pos++] = (byte)(0x80 | (c & 0x3F));
                }
            }
            return result;
        }

        /// <summary>
        /// Decodes one scalar at bytes[i]; returns its byte length, or -(number of bytes in the
        /// maximal invalid subsequence) when invalid (the Unicode "maximal subpart" rule .NET uses).
        /// </summary>
        public static int DecodeScalar(byte[] bytes, int i, int end, out int scalar)
        {
            scalar = 0;
            int b0 = bytes[i];
            if (b0 < 0x80) { scalar = b0; return 1; }
            int need;
            int min;
            if (b0 >= 0xC2 && b0 <= 0xDF) { need = 1; scalar = b0 & 0x1F; min = 0x80; }
            else if (b0 >= 0xE0 && b0 <= 0xEF) { need = 2; scalar = b0 & 0x0F; min = 0x800; }
            else if (b0 >= 0xF0 && b0 <= 0xF4) { need = 3; scalar = b0 & 0x07; min = 0x10000; }
            else return -1;
            int k = 1;
            for (; k <= need; k++)
            {
                if (i + k >= end) return -k;
                int b = bytes[i + k];
                // Second-byte ranges that exclude overlongs, surrogates and > U+10FFFF.
                if (k == 1)
                {
                    int lo = 0x80, hi = 0xBF;
                    if (b0 == 0xE0) lo = 0xA0;
                    else if (b0 == 0xED) hi = 0x9F;
                    else if (b0 == 0xF0) lo = 0x90;
                    else if (b0 == 0xF4) hi = 0x8F;
                    if (b < lo || b > hi) return -1;
                }
                else if ((b & 0xC0) != 0x80) return -k;
                scalar = (scalar << 6) | (b & 0x3F);
            }
            if (scalar < min) return -1;
            return need + 1;
        }

        /// <summary>UTF-16 string of UTF-8 bytes; null when they are not valid UTF-8.</summary>
        public static string ToUtf16(byte[] bytes, int start, int length)
        {
            int end = start + length;
            bool ascii = true;
            for (int i = start; i < end; i++) if (bytes[i] >= 0x80) { ascii = false; break; }
            if (ascii)
            {
                char[] simple = new char[length];
                for (int i = 0; i < length; i++) simple[i] = (char)bytes[start + i];
                return new string(simple);
            }
            StringBuilder sb = new StringBuilder(length);
            int p = start;
            while (p < end)
            {
                int n = DecodeScalar(bytes, p, end, out int scalar);
                if (n < 0) return null;
                AppendScalar(sb, scalar);
                p += n;
            }
            return sb.ToString();
        }

        public static void AppendScalar(StringBuilder sb, int scalar)
        {
            if (scalar < 0x10000) sb.Append((char)scalar);
            else
            {
                scalar -= 0x10000;
                sb.Append((char)(0xD800 + (scalar >> 10)));
                sb.Append((char)(0xDC00 + (scalar & 0x3FF)));
            }
        }

        public static byte[] Slice(byte[] bytes, int start, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(bytes, start, result, 0, length);
            return result;
        }

        public static string PrintableByte(byte value) =>
            value >= 0x20 && value < 0x7F ? ((char)value).ToString() : "0x" + value.ToString("X2");
    }
}
