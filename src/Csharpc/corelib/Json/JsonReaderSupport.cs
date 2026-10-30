// System.Text.Json reader support: JsonReaderOptions, JsonReaderState, the reader's throw helpers,
// unescaping, number parsing (Utf8Parser semantics) and ISO 8601 date parsing.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json (release/11.0): JsonReaderOptions.cs,
// JsonReaderState.cs, ThrowHelper.cs, JsonReaderHelper*.cs, JsonHelpers.Date.cs; decimal parsing from
// src/libraries/System.Private.CoreLib (Number.NumberToDecimal) as used by Utf8Parser.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.JsonReaderOptions")]
    public struct JsonReaderOptionsImpl
    {
        private int _maxDepth;
        private JsonCommentHandling _commentHandling;

        public JsonCommentHandling CommentHandling
        {
            get => _commentHandling;
            set
            {
                if (value > JsonCommentHandling.Allow) throw new ArgumentOutOfRangeException("value", JsonSR.CommentHandlingMustBeValid);
                _commentHandling = value;
            }
        }

        public int MaxDepth
        {
            get => _maxDepth;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException("value", JsonSR.MaxDepthMustBePositive);
                _maxDepth = value;
            }
        }

        public bool AllowTrailingCommas { get; set; }

        public bool AllowMultipleValues { get; set; }
    }

    [Shadows("System.Text.Json.JsonReaderState")]
    public readonly struct JsonReaderStateImpl
    {
        internal readonly long _lineNumber;
        internal readonly long _bytePositionInLine;
        internal readonly bool _inObject;
        internal readonly bool _isNotPrimitive;
        internal readonly bool _valueIsEscaped;
        internal readonly bool _trailingCommaBeforeComment;
        internal readonly JsonTokenType _tokenType;
        internal readonly JsonTokenType _previousTokenType;
        internal readonly JsonReaderOptionsImpl _readerOptions;
        internal readonly JsonBitStack _bitStack;

        public JsonReaderStateImpl(JsonReaderOptionsImpl options = default)
        {
            _lineNumber = 0;
            _bytePositionInLine = 0;
            _inObject = false;
            _isNotPrimitive = false;
            _valueIsEscaped = false;
            _trailingCommaBeforeComment = false;
            _tokenType = JsonTokenType.None;
            _previousTokenType = JsonTokenType.None;
            _readerOptions = options;
            _bitStack = default;
        }

        internal JsonReaderStateImpl(long lineNumber, long bytePositionInLine, bool inObject, bool isNotPrimitive, bool valueIsEscaped,
            bool trailingCommaBeforeComment, JsonTokenType tokenType, JsonTokenType previousTokenType,
            JsonReaderOptionsImpl readerOptions, JsonBitStack bitStack)
        {
            _lineNumber = lineNumber;
            _bytePositionInLine = bytePositionInLine;
            _inObject = inObject;
            _isNotPrimitive = isNotPrimitive;
            _valueIsEscaped = valueIsEscaped;
            _trailingCommaBeforeComment = trailingCommaBeforeComment;
            _tokenType = tokenType;
            _previousTokenType = previousTokenType;
            _readerOptions = readerOptions;
            _bitStack = bitStack;
        }

        public JsonReaderOptionsImpl Options => _readerOptions;
    }

    internal enum ExceptionResource
    {
        ArrayDepthTooLarge,
        EndOfCommentNotFound,
        EndOfStringNotFound,
        RequiredDigitNotFoundAfterDecimal,
        RequiredDigitNotFoundAfterSign,
        RequiredDigitNotFoundEndOfData,
        ExpectedEndAfterSingleJson,
        ExpectedEndOfDigitNotFound,
        ExpectedFalse,
        ExpectedNextDigitEValueNotFound,
        ExpectedNull,
        ExpectedSeparatorAfterPropertyNameNotFound,
        ExpectedStartOfPropertyNotFound,
        ExpectedStartOfPropertyOrValueNotFound,
        ExpectedStartOfPropertyOrValueAfterComment,
        ExpectedStartOfValueNotFound,
        ExpectedTrue,
        ExpectedValueAfterPropertyNameNotFound,
        FoundInvalidCharacter,
        InvalidCharacterWithinString,
        InvalidCharacterAfterEscapeWithinString,
        InvalidHexCharacterWithinString,
        InvalidEndOfJsonNonPrimitive,
        MismatchedObjectArray,
        ObjectDepthTooLarge,
        ZeroDepthAtEnd,
        ExpectedJsonTokens,
        TrailingCommaNotAllowedBeforeArrayEnd,
        TrailingCommaNotAllowedBeforeObjectEnd,
        InvalidCharacterAtStartOfComment,
        UnexpectedEndOfDataWhileReadingComment,
        UnexpectedEndOfLineSeparator,
        ExpectedOneCompleteToken,
        NotEnoughData,
        InvalidLeadingZeroInNumber,
    }

    internal enum ConsumeNumberResult : byte
    {
        Success,
        OperationIncomplete,
        NeedMoreData,
    }

    internal enum ConsumeTokenResult : byte
    {
        Success,
        NotEnoughDataRollBackState,
        IncompleteNoRollBackNecessary,
    }

    internal enum NumericType
    {
        Byte, SByte, Int16, Int32, Int64, UInt16, UInt32, UInt64, Single, Double, Decimal,
    }

    internal enum DataType
    {
        DateTime, DateTimeOffset, Base64String,
    }

    internal static class JsonThrowHelper
    {
        public static void ThrowJsonReaderException(ref Utf8JsonReaderImpl json, ExceptionResource resource, byte nextByte = 0, ReadOnlySpan<byte> bytes = default)
        {
            throw GetJsonReaderException(ref json, resource, nextByte, bytes);
        }

        public static JsonExceptionImpl GetJsonReaderException(ref Utf8JsonReaderImpl json, ExceptionResource resource, byte nextByte, ReadOnlySpan<byte> bytes)
        {
            string characters = Encoding.UTF8.GetString(bytes.ToArray());
            string message = GetResourceString(ref json, resource, nextByte, characters);
            JsonReaderStateImpl state = json.CurrentState;
            long lineNumber = state._lineNumber;
            long bytePositionInLine = state._bytePositionInLine;
            message += " LineNumber: " + lineNumber.ToString() + " | BytePositionInLine: " + bytePositionInLine.ToString() + ".";
            return new JsonReaderExceptionImpl(message, lineNumber, bytePositionInLine);
        }

        private static string GetResourceString(ref Utf8JsonReaderImpl json, ExceptionResource resource, byte nextByte, string characters)
        {
            string character = JsonUtf8.PrintableByte(nextByte);
            switch (resource)
            {
                case ExceptionResource.ArrayDepthTooLarge: return JsonSR.Format(JsonSR.ArrayDepthTooLarge, json.CurrentState.Options.MaxDepth);
                case ExceptionResource.MismatchedObjectArray: return JsonSR.Format(JsonSR.MismatchedObjectArray, character);
                case ExceptionResource.TrailingCommaNotAllowedBeforeArrayEnd: return JsonSR.TrailingCommaNotAllowedBeforeArrayEnd;
                case ExceptionResource.TrailingCommaNotAllowedBeforeObjectEnd: return JsonSR.TrailingCommaNotAllowedBeforeObjectEnd;
                case ExceptionResource.EndOfStringNotFound: return JsonSR.EndOfStringNotFound;
                case ExceptionResource.RequiredDigitNotFoundAfterSign: return JsonSR.Format(JsonSR.RequiredDigitNotFoundAfterSign, character);
                case ExceptionResource.RequiredDigitNotFoundAfterDecimal: return JsonSR.Format(JsonSR.RequiredDigitNotFoundAfterDecimal, character);
                case ExceptionResource.RequiredDigitNotFoundEndOfData: return JsonSR.RequiredDigitNotFoundEndOfData;
                case ExceptionResource.ExpectedEndAfterSingleJson: return JsonSR.Format(JsonSR.ExpectedEndAfterSingleJson, character);
                case ExceptionResource.ExpectedEndOfDigitNotFound: return JsonSR.Format(JsonSR.ExpectedEndOfDigitNotFound, character);
                case ExceptionResource.ExpectedNextDigitEValueNotFound: return JsonSR.Format(JsonSR.ExpectedNextDigitEValueNotFound, character);
                case ExceptionResource.ExpectedSeparatorAfterPropertyNameNotFound: return JsonSR.Format(JsonSR.ExpectedSeparatorAfterPropertyNameNotFound, character);
                case ExceptionResource.ExpectedStartOfPropertyNotFound: return JsonSR.Format(JsonSR.ExpectedStartOfPropertyNotFound, character);
                case ExceptionResource.ExpectedStartOfPropertyOrValueNotFound: return JsonSR.ExpectedStartOfPropertyOrValueNotFound;
                case ExceptionResource.ExpectedStartOfPropertyOrValueAfterComment: return JsonSR.Format(JsonSR.ExpectedStartOfPropertyOrValueAfterComment, character);
                case ExceptionResource.ExpectedStartOfValueNotFound: return JsonSR.Format(JsonSR.ExpectedStartOfValueNotFound, character);
                case ExceptionResource.ExpectedValueAfterPropertyNameNotFound: return JsonSR.ExpectedValueAfterPropertyNameNotFound;
                case ExceptionResource.FoundInvalidCharacter: return JsonSR.Format(JsonSR.FoundInvalidCharacter, character);
                case ExceptionResource.InvalidEndOfJsonNonPrimitive: return JsonSR.Format(JsonSR.InvalidEndOfJsonNonPrimitive, TokenName(json.TokenType));
                case ExceptionResource.ObjectDepthTooLarge: return JsonSR.Format(JsonSR.ObjectDepthTooLarge, json.CurrentState.Options.MaxDepth);
                case ExceptionResource.ExpectedFalse: return JsonSR.Format(JsonSR.ExpectedFalse, characters);
                case ExceptionResource.ExpectedNull: return JsonSR.Format(JsonSR.ExpectedNull, characters);
                case ExceptionResource.ExpectedTrue: return JsonSR.Format(JsonSR.ExpectedTrue, characters);
                case ExceptionResource.InvalidCharacterWithinString: return JsonSR.Format(JsonSR.InvalidCharacterWithinString, character);
                case ExceptionResource.InvalidCharacterAfterEscapeWithinString: return JsonSR.Format(JsonSR.InvalidCharacterAfterEscapeWithinString, character);
                case ExceptionResource.InvalidHexCharacterWithinString: return JsonSR.Format(JsonSR.InvalidHexCharacterWithinString, character);
                case ExceptionResource.EndOfCommentNotFound: return JsonSR.EndOfCommentNotFound;
                case ExceptionResource.ZeroDepthAtEnd: return JsonSR.ZeroDepthAtEnd;
                case ExceptionResource.ExpectedJsonTokens: return JsonSR.ExpectedJsonTokens;
                case ExceptionResource.NotEnoughData: return JsonSR.NotEnoughData;
                case ExceptionResource.ExpectedOneCompleteToken: return JsonSR.ExpectedOneCompleteToken;
                case ExceptionResource.InvalidCharacterAtStartOfComment: return JsonSR.Format(JsonSR.InvalidCharacterAtStartOfComment, character);
                case ExceptionResource.UnexpectedEndOfDataWhileReadingComment: return JsonSR.UnexpectedEndOfDataWhileReadingComment;
                case ExceptionResource.UnexpectedEndOfLineSeparator: return JsonSR.UnexpectedEndOfLineSeparator;
                case ExceptionResource.InvalidLeadingZeroInNumber: return JsonSR.Format(JsonSR.InvalidLeadingZeroInNumber, character);
                default: return "";
            }
        }

        public static string TokenName(JsonTokenType tokenType)
        {
            switch (tokenType)
            {
                case JsonTokenType.None: return "None";
                case JsonTokenType.StartObject: return "StartObject";
                case JsonTokenType.EndObject: return "EndObject";
                case JsonTokenType.StartArray: return "StartArray";
                case JsonTokenType.EndArray: return "EndArray";
                case JsonTokenType.PropertyName: return "PropertyName";
                case JsonTokenType.Comment: return "Comment";
                case JsonTokenType.String: return "String";
                case JsonTokenType.Number: return "Number";
                case JsonTokenType.True: return "True";
                case JsonTokenType.False: return "False";
                case JsonTokenType.Null: return "Null";
                default: return ((int)tokenType).ToString();
            }
        }

        public static string ValueKindName(JsonValueKind kind)
        {
            switch (kind)
            {
                case JsonValueKind.Undefined: return "Undefined";
                case JsonValueKind.Object: return "Object";
                case JsonValueKind.Array: return "Array";
                case JsonValueKind.String: return "String";
                case JsonValueKind.Number: return "Number";
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Null: return "Null";
                default: return ((int)kind).ToString();
            }
        }

        private static InvalidOperationException InvalidCast(string message, JsonTokenType tokenType) =>
            new InvalidOperationException(JsonSR.Format(JsonSR.InvalidCast, TokenName(tokenType), message));

        public static void ThrowInvalidOperationException_ExpectedNumber(JsonTokenType tokenType) => throw InvalidCast("number", tokenType);
        public static void ThrowInvalidOperationException_ExpectedBoolean(JsonTokenType tokenType) => throw InvalidCast("boolean", tokenType);
        public static void ThrowInvalidOperationException_ExpectedString(JsonTokenType tokenType) => throw InvalidCast("string", tokenType);
        public static void ThrowInvalidOperationException_ExpectedPropertyName(JsonTokenType tokenType) => throw InvalidCast("propertyName", tokenType);
        public static void ThrowInvalidOperationException_ExpectedComment(JsonTokenType tokenType) => throw InvalidCast("comment", tokenType);
        public static InvalidOperationException GetInvalidOperationException_ExpectedArray(JsonTokenType tokenType) => InvalidCast("array", tokenType);
        public static InvalidOperationException GetInvalidOperationException_ExpectedObject(JsonTokenType tokenType) => InvalidCast("object", tokenType);

        public static void ThrowInvalidOperationException_ExpectedStringComparison(JsonTokenType tokenType) =>
            throw new InvalidOperationException(JsonSR.Format(JsonSR.InvalidComparison, TokenName(tokenType)));

        public static void ThrowInvalidOperationException_CannotSkipOnPartial() => throw new InvalidOperationException(JsonSR.CannotSkip);

        public static void ThrowArgumentException_DestinationTooShort() => throw new ArgumentException(JsonSR.DestinationTooShort);

        public static void ThrowFormatException(NumericType numericType) =>
            throw new FormatException("Either the JSON value is not in a supported format, or is out of bounds for the " + NumericName(numericType) + " type.");

        private static string NumericName(NumericType type)
        {
            switch (type)
            {
                case NumericType.Byte: return "Byte";
                case NumericType.SByte: return "SByte";
                case NumericType.Int16: return "Int16";
                case NumericType.Int32: return "Int32";
                case NumericType.Int64: return "Int64";
                case NumericType.UInt16: return "UInt16";
                case NumericType.UInt32: return "UInt32";
                case NumericType.UInt64: return "UInt64";
                case NumericType.Single: return "Single";
                case NumericType.Double: return "Double";
                default: return "Decimal";
            }
        }

        public static void ThrowFormatException(DataType dataType)
        {
            if (dataType == DataType.Base64String) throw new FormatException(JsonSR.CannotDecodeInvalidBase64);
            throw new FormatException(JsonSR.Format(JsonSR.UnsupportedFormat, dataType == DataType.DateTimeOffset ? "DateTimeOffset" : "DateTime"));
        }

        public static void ThrowFormatException(string dataTypeName) =>
            throw new FormatException(JsonSR.Format(JsonSR.UnsupportedFormat, dataTypeName));

        public static void ThrowInvalidOperationException_ReadInvalidUTF16(int charAsInt) =>
            throw new InvalidOperationException(JsonSR.Format(JsonSR.CannotReadInvalidUTF16, "0x" + charAsInt.ToString("X2")));

        public static void ThrowInvalidOperationException_ReadIncompleteUTF16() =>
            throw new InvalidOperationException(JsonSR.CannotReadIncompleteUTF16);

        public static InvalidOperationException GetInvalidOperationException_ReadInvalidUTF8() =>
            new InvalidOperationException(JsonSR.CannotTranscodeInvalidUtf8);

        public static void ThrowJsonElementWrongTypeException(JsonValueKind expected, JsonValueKind actual) =>
            throw new InvalidOperationException(JsonSR.Format(JsonSR.JsonElementHasWrongType, ValueKindName(expected), ValueKindName(actual)));

        public static void ThrowJsonElementWrongTypeException(string expected, JsonValueKind actual) =>
            throw new InvalidOperationException(JsonSR.Format(JsonSR.JsonElementHasWrongType, expected, ValueKindName(actual)));
    }

    internal static class JsonHelpers
    {
        public static bool IsDigit(byte value) => (uint)(value - '0') <= '9' - '0';

        public static bool IsInRangeInclusive(int value, int lowerBound, int upperBound) => (uint)(value - lowerBound) <= (uint)(upperBound - lowerBound);

        public static bool IsInRangeInclusive(uint value, uint lowerBound, uint upperBound) => (value - lowerBound) <= (upperBound - lowerBound);

        public static bool IsInRangeInclusive(long value, long lowerBound, long upperBound) => (ulong)(value - lowerBound) <= (ulong)(upperBound - lowerBound);

        public static bool IsValidDateTimeOffsetParseLength(int length) =>
            IsInRangeInclusive(length, JsonConstants.MinimumDateTimeParseLength, JsonConstants.MaximumEscapedDateTimeOffsetParseLength);

        public static bool IsValidUnescapedDateTimeOffsetParseLength(int length) =>
            IsInRangeInclusive(length, JsonConstants.MinimumDateTimeParseLength, JsonConstants.MaximumDateTimeOffsetParseLength);

        private struct DateTimeParseData
        {
            public int Year;
            public int Month;
            public int Day;
            public bool IsCalendarDateOnly;
            public int Hour;
            public int Minute;
            public int Second;
            public int Fraction;
            public int OffsetHours;
            public int OffsetMinutes;
            public byte OffsetToken;
            public bool OffsetNegative => OffsetToken == JsonConstants.Hyphen;
        }

        /// <summary>The extended ISO 8601-1:2019 profile System.Text.Json accepts for DateTime.</summary>
        public static bool TryParseAsISO(ReadOnlySpan<byte> source, out DateTime value)
        {
            if (!TryParseDateTimeOffset(source, out DateTimeParseData parseData))
            {
                value = default;
                return false;
            }
            if (parseData.OffsetToken == JsonConstants.UtcOffsetToken)
            {
                return TryCreateDateTime(parseData, DateTimeKind.Utc, out value);
            }
            if (parseData.OffsetToken == JsonConstants.Plus || parseData.OffsetToken == JsonConstants.Hyphen)
            {
                // new DateTimeOffset(dateTime, offset).LocalDateTime
                if (!TryCreateDateTime(parseData, DateTimeKind.Unspecified, out DateTime dateTime) || !TryGetOffsetTicks(ref parseData, out long offsetTicks))
                {
                    value = default;
                    return false;
                }
                long utcTicks = dateTime.Ticks - offsetTicks;
                if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
                {
                    value = default;
                    return false;
                }
                value = new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime();
                return true;
            }
            return TryCreateDateTime(parseData, DateTimeKind.Unspecified, out value);
        }

        /// <summary>The ISO 8601 profile for DateTimeOffset: no offset means the local zone's offset.</summary>
        public static bool TryParseAsISO(ReadOnlySpan<byte> source, out DateTimeOffset value)
        {
            if (!TryParseDateTimeOffset(source, out DateTimeParseData parseData))
            {
                value = default;
                return false;
            }
            if (parseData.OffsetToken == JsonConstants.UtcOffsetToken || // same as "+00:00"
                parseData.OffsetToken == JsonConstants.Plus || parseData.OffsetToken == JsonConstants.Hyphen)
            {
                if (!TryCreateDateTime(parseData, DateTimeKind.Unspecified, out DateTime dateTime) || !TryGetOffsetTicks(ref parseData, out long offsetTicks))
                {
                    value = default;
                    return false;
                }
                // new DateTimeOffset(ticks, offset) throws ArgumentOutOfRangeException when the UTC time leaves the range.
                long utcTicks = dateTime.Ticks - offsetTicks;
                if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
                {
                    value = default;
                    return false;
                }
                value = new DateTimeOffset(dateTime.Ticks, new TimeSpan(offsetTicks));
                return true;
            }
            if (!TryCreateDateTime(parseData, DateTimeKind.Local, out DateTime local))
            {
                value = default;
                return false;
            }
            try
            {
                value = new DateTimeOffset(local);
            }
            catch (ArgumentOutOfRangeException)
            {
                value = default;
                return false;
            }
            return true;
        }

        public static bool TryParseAsIsoDateOnly(ReadOnlySpan<byte> source, out DateTime value)
        {
            if (TryParseDateTimeOffset(source, out DateTimeParseData parseData) && parseData.IsCalendarDateOnly &&
                TryCreateDateTime(parseData, DateTimeKind.Unspecified, out value))
            {
                return true;
            }
            value = default;
            return false;
        }

        private static bool TryParseDateTimeOffset(ReadOnlySpan<byte> source, out DateTimeParseData parseData)
        {
            parseData = default;
            if (source.Length < 10) return false;
            {
                uint digit1 = source[0] - (uint)'0';
                uint digit2 = source[1] - (uint)'0';
                uint digit3 = source[2] - (uint)'0';
                uint digit4 = source[3] - (uint)'0';
                if (digit1 > 9 || digit2 > 9 || digit3 > 9 || digit4 > 9) return false;
                parseData.Year = (int)(digit1 * 1000 + digit2 * 100 + digit3 * 10 + digit4);
            }
            if (source[4] != JsonConstants.Hyphen
                || !TryGetNextTwoDigits(source.Slice(5, 2), ref parseData.Month)
                || source[7] != JsonConstants.Hyphen
                || !TryGetNextTwoDigits(source.Slice(8, 2), ref parseData.Day))
            {
                return false;
            }
            if (source.Length == 10)
            {
                parseData.IsCalendarDateOnly = true;
                return true;
            }
            if (source.Length < 16) return false;
            if (source[10] != JsonConstants.TimePrefix || source[13] != JsonConstants.Colon
                || !TryGetNextTwoDigits(source.Slice(11, 2), ref parseData.Hour)
                || !TryGetNextTwoDigits(source.Slice(14, 2), ref parseData.Minute))
            {
                return false;
            }
            if (source.Length == 16) return true;

            byte curByte = source[16];
            int sourceIndex = 17;
            switch (curByte)
            {
                case JsonConstants.UtcOffsetToken:
                    parseData.OffsetToken = JsonConstants.UtcOffsetToken;
                    return sourceIndex == source.Length;
                case JsonConstants.Plus:
                case JsonConstants.Hyphen:
                    parseData.OffsetToken = curByte;
                    return ParseOffset(ref parseData, source.Slice(sourceIndex));
                case JsonConstants.Colon:
                    break;
                default:
                    return false;
            }
            if (source.Length < 19 || !TryGetNextTwoDigits(source.Slice(17, 2), ref parseData.Second)) return false;
            if (source.Length == 19) return true;

            curByte = source[19];
            sourceIndex = 20;
            switch (curByte)
            {
                case JsonConstants.UtcOffsetToken:
                    parseData.OffsetToken = JsonConstants.UtcOffsetToken;
                    return sourceIndex == source.Length;
                case JsonConstants.Plus:
                case JsonConstants.Hyphen:
                    parseData.OffsetToken = curByte;
                    return ParseOffset(ref parseData, source.Slice(sourceIndex));
                case JsonConstants.Period:
                    break;
                default:
                    return false;
            }
            if (source.Length < 21) return false;
            {
                int numDigitsRead = 0;
                int fractionEnd = Math.Min(sourceIndex + JsonConstants.DateTimeParseNumFractionDigits, source.Length);
                while (sourceIndex < fractionEnd && IsDigit(curByte = source[sourceIndex]))
                {
                    if (numDigitsRead < JsonConstants.DateTimeNumFractionDigits)
                    {
                        parseData.Fraction = (parseData.Fraction * 10) + (int)(curByte - (uint)'0');
                        numDigitsRead++;
                    }
                    sourceIndex++;
                }
                if (parseData.Fraction != 0)
                {
                    while (numDigitsRead < JsonConstants.DateTimeNumFractionDigits)
                    {
                        parseData.Fraction *= 10;
                        numDigitsRead++;
                    }
                }
            }
            if (sourceIndex == source.Length) return true;

            curByte = source[sourceIndex++];
            switch (curByte)
            {
                case JsonConstants.UtcOffsetToken:
                    parseData.OffsetToken = JsonConstants.UtcOffsetToken;
                    return sourceIndex == source.Length;
                case JsonConstants.Plus:
                case JsonConstants.Hyphen:
                    parseData.OffsetToken = curByte;
                    return ParseOffset(ref parseData, source.Slice(sourceIndex));
                default:
                    return false;
            }
        }

        private static bool ParseOffset(ref DateTimeParseData parseData, ReadOnlySpan<byte> offsetData)
        {
            if (offsetData.Length < 2 || !TryGetNextTwoDigits(offsetData.Slice(0, 2), ref parseData.OffsetHours)) return false;
            if (offsetData.Length == 2) return true;
            if (offsetData.Length != 5 || offsetData[2] != JsonConstants.Colon || !TryGetNextTwoDigits(offsetData.Slice(3), ref parseData.OffsetMinutes))
                return false;
            return true;
        }

        private static bool TryGetNextTwoDigits(ReadOnlySpan<byte> source, ref int value)
        {
            uint digit1 = source[0] - (uint)'0';
            uint digit2 = source[1] - (uint)'0';
            if (digit1 > 9 || digit2 > 9)
            {
                value = 0;
                return false;
            }
            value = (int)(digit1 * 10 + digit2);
            return true;
        }

        private static bool TryGetOffsetTicks(ref DateTimeParseData parseData, out long offsetTicks)
        {
            offsetTicks = 0;
            if (((uint)parseData.OffsetHours) > JsonConstants.MaxDateTimeUtcOffsetHours) return false;
            if (((uint)parseData.OffsetMinutes) > 59) return false;
            if (parseData.OffsetHours == JsonConstants.MaxDateTimeUtcOffsetHours && parseData.OffsetMinutes != 0) return false;
            offsetTicks = (((long)parseData.OffsetHours) * 3600 + ((long)parseData.OffsetMinutes) * 60) * TimeSpan.TicksPerSecond;
            if (parseData.OffsetNegative) offsetTicks = -offsetTicks;
            return true;
        }

        private static bool TryCreateDateTime(DateTimeParseData parseData, DateTimeKind kind, out DateTime value)
        {
            value = default;
            if (parseData.Year == 0) return false;
            if ((((uint)parseData.Month) - 1) >= 12) return false;
            uint dayMinusOne = ((uint)parseData.Day) - 1;
            if (dayMinusOne >= 28 && dayMinusOne >= DateTime.DaysInMonth(parseData.Year, parseData.Month)) return false;
            bool isEndOfDay = false;
            if (parseData.Hour == 24)
            {
                if (parseData.Minute != 0 || parseData.Second != 0 || parseData.Fraction != 0) return false;
                parseData.Hour = 0;
                isEndOfDay = true;
            }
            if (((uint)parseData.Hour) > 23) return false;
            if (((uint)parseData.Minute) > 59) return false;
            if (((uint)parseData.Second) > 59) return false;
            int[] days = DateTime.IsLeapYear(parseData.Year) ? DaysToMonth366 : DaysToMonth365;
            int yearMinusOne = parseData.Year - 1;
            int totalDays = (yearMinusOne * 365) + (yearMinusOne / 4) - (yearMinusOne / 100) + (yearMinusOne / 400) + days[parseData.Month - 1] + parseData.Day - 1;
            long ticks = totalDays * TimeSpan.TicksPerDay;
            int totalSeconds = (parseData.Hour * 3600) + (parseData.Minute * 60) + parseData.Second;
            ticks += totalSeconds * TimeSpan.TicksPerSecond;
            ticks += parseData.Fraction;
            if (isEndOfDay)
            {
                ticks += TimeSpan.TicksPerDay;
                if ((ulong)ticks > (ulong)DateTime.MaxValue.Ticks) return false;
            }
            value = new DateTime(ticks, kind);
            return true;
        }

        private static int[] s_daysToMonth365;
        private static int[] s_daysToMonth366;
        private static int[] DaysToMonth365 => s_daysToMonth365 ??= new int[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };
        private static int[] DaysToMonth366 => s_daysToMonth366 ??= new int[] { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 };
    }

    internal static class JsonReaderHelper
    {
        public static int IndexOfQuoteOrAnyControlOrBackSlash(ReadOnlySpan<byte> span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                byte b = span[i];
                if (b == JsonConstants.Quote || b == JsonConstants.BackSlash || b < JsonConstants.Space) return i;
            }
            return -1;
        }

        public static int IndexOfAny(ReadOnlySpan<byte> span, byte a, byte b, byte c)
        {
            for (int i = 0; i < span.Length; i++)
            {
                byte x = span[i];
                if (x == a || x == b || x == c) return i;
            }
            return -1;
        }

        public static (int, int) CountNewLines(ReadOnlySpan<byte> data)
        {
            int lastLineFeedIndex = -1;
            int newLines = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == JsonConstants.LineFeed)
                {
                    newLines++;
                    lastLineFeedIndex = i;
                }
            }
            return (newLines, lastLineFeedIndex);
        }

        internal static JsonValueKind ToValueKind(JsonTokenType tokenType)
        {
            switch (tokenType)
            {
                case JsonTokenType.None: return JsonValueKind.Undefined;
                case JsonTokenType.StartArray: return JsonValueKind.Array;
                case JsonTokenType.StartObject: return JsonValueKind.Object;
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    return (JsonValueKind)((byte)tokenType - 4);
                default: return JsonValueKind.Undefined;
            }
        }

        public static bool IsTokenTypePrimitive(JsonTokenType tokenType) =>
            (tokenType - JsonTokenType.String) <= (JsonTokenType.Null - JsonTokenType.String);

        public static bool IsHexDigit(byte b) => (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');

        private static int HexValue(byte b) => b <= '9' ? b - '0' : (b | 0x20) - 'a' + 10;

        public static bool TryGetValue(ReadOnlySpan<byte> segment, bool isEscaped, out DateTime value)
        {
            if (!JsonHelpers.IsValidDateTimeOffsetParseLength(segment.Length))
            {
                value = default;
                return false;
            }
            if (isEscaped)
            {
                byte[] unescaped = GetUnescaped(segment);
                if (JsonHelpers.IsValidUnescapedDateTimeOffsetParseLength(unescaped.Length) && JsonHelpers.TryParseAsISO(unescaped, out DateTime tmp))
                {
                    value = tmp;
                    return true;
                }
                value = default;
                return false;
            }
            return JsonHelpers.TryParseAsISO(segment, out value);
        }

        public static bool TryGetValue(ReadOnlySpan<byte> segment, bool isEscaped, out DateTimeOffset value)
        {
            if (!JsonHelpers.IsValidDateTimeOffsetParseLength(segment.Length))
            {
                value = default;
                return false;
            }
            if (isEscaped)
            {
                byte[] unescaped = GetUnescaped(segment);
                if (JsonHelpers.IsValidUnescapedDateTimeOffsetParseLength(unescaped.Length) && JsonHelpers.TryParseAsISO(unescaped, out DateTimeOffset tmp))
                {
                    value = tmp;
                    return true;
                }
                value = default;
                return false;
            }
            return JsonHelpers.TryParseAsISO(segment, out value);
        }

        // Guids are the 36-character "D" format only (Utf8Parser 'D'), after unescaping (at most 6 bytes a character).
        public static bool TryGetValue(ReadOnlySpan<byte> segment, bool isEscaped, out Guid value)
        {
            value = default;
            if (segment.Length > 36 * 6) return false;
            ReadOnlySpan<byte> text = isEscaped ? GetUnescaped(segment) : segment;
            if (text.Length != 36) return false;
            for (int i = 0; i < 36; i++)
            {
                byte b = text[i];
                bool dash = i == 8 || i == 13 || i == 18 || i == 23;
                if (dash ? b != (byte)'-' : !((b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F'))) return false;
            }
            return Guid.TryParseExact(Encoding.ASCII.GetString(text), "D", out value);
        }

        public static bool TryGetFloatingPointConstant(ReadOnlySpan<byte> span, out float value)
        {
            if (TryGetFloatingPointConstant(span, out double d))
            {
                value = (float)d;
                return true;
            }
            value = 0;
            return false;
        }

        public static bool TryGetFloatingPointConstant(ReadOnlySpan<byte> span, out double value)
        {
            if (span.Length == 3 && span[0] == 'N' && span[1] == 'a' && span[2] == 'N') { value = double.NaN; return true; }
            if (span.Length == 8 && span.SequenceEqual("Infinity"u8)) { value = double.PositiveInfinity; return true; }
            if (span.Length == 9 && span.SequenceEqual("-Infinity"u8)) { value = double.NegativeInfinity; return true; }
            value = 0;
            return false;
        }

        public static string GetUnescapedString(ReadOnlySpan<byte> utf8Source) => TranscodeHelper(GetUnescaped(utf8Source));

        public static byte[] GetUnescaped(ReadOnlySpan<byte> utf8Source)
        {
            byte[] destination = new byte[utf8Source.Length];
            Unescape(utf8Source, destination, out int written);
            byte[] result = new byte[written];
            Array.Copy(destination, result, written);
            return result;
        }

        public static bool UnescapeAndCompare(ReadOnlySpan<byte> utf8Source, ReadOnlySpan<byte> other)
        {
            byte[] unescaped = GetUnescaped(utf8Source);
            return other.SequenceEqual(unescaped);
        }

        public static string TranscodeHelper(ReadOnlySpan<byte> utf8Unescaped)
        {
            byte[] bytes = utf8Unescaped.ToArray();
            string s = JsonUtf8.ToUtf16(bytes, 0, bytes.Length);
            if (s == null) throw JsonThrowHelper.GetInvalidOperationException_ReadInvalidUTF8();
            return s;
        }

        public static int TranscodeHelper(ReadOnlySpan<byte> utf8Unescaped, Span<char> destination)
        {
            string s = TranscodeHelper(utf8Unescaped);
            if (s.Length > destination.Length)
            {
                destination.Clear();
                throw new ArgumentException("The output char buffer is too small to contain the decoded characters, encoding 'Unicode (UTF-8)' fallback 'System.Text.DecoderExceptionFallback'.", "chars");
            }
            for (int i = 0; i < s.Length; i++) destination[i] = s[i];
            return s.Length;
        }

        public static void ValidateUtf8(ReadOnlySpan<byte> utf8Buffer)
        {
            byte[] bytes = utf8Buffer.ToArray();
            if (JsonUtf8.ToUtf16(bytes, 0, bytes.Length) == null) throw JsonThrowHelper.GetInvalidOperationException_ReadInvalidUTF8();
        }

        public static bool TryGetUnescapedBase64Bytes(ReadOnlySpan<byte> utf8Source, out byte[] bytes) =>
            TryDecodeBase64(GetUnescaped(utf8Source), out bytes);

        /// <summary>Base64.DecodeFromUtf8: strict, padded, no whitespace.</summary>
        public static bool TryDecodeBase64(ReadOnlySpan<byte> utf8, out byte[] bytes)
        {
            bytes = null;
            if (utf8.Length % 4 != 0) return false;
            int padding = 0;
            if (utf8.Length > 0 && utf8[utf8.Length - 1] == '=') padding++;
            if (utf8.Length > 1 && utf8[utf8.Length - 2] == '=') padding++;
            int outLength = utf8.Length / 4 * 3 - padding;
            byte[] result = new byte[outLength];
            int o = 0;
            for (int i = 0; i < utf8.Length; i += 4)
            {
                int v = 0;
                int pads = 0;
                for (int k = 0; k < 4; k++)
                {
                    byte c = utf8[i + k];
                    int d;
                    if (c >= 'A' && c <= 'Z') d = c - 'A';
                    else if (c >= 'a' && c <= 'z') d = c - 'a' + 26;
                    else if (c >= '0' && c <= '9') d = c - '0' + 52;
                    else if (c == '+') d = 62;
                    else if (c == '/') d = 63;
                    else if (c == '=' && i + 4 == utf8.Length && k >= 2) { d = 0; pads++; }
                    else return false;
                    if (pads > 0 && c != '=') return false;
                    v = (v << 6) | d;
                }
                if (pads == 1 && (v & 0xFF) != 0) return false;
                if (pads == 2 && (v & 0xFFFF) != 0) return false;
                result[o++] = (byte)(v >> 16);
                if (pads < 2) result[o++] = (byte)(v >> 8);
                if (pads < 1) result[o++] = (byte)v;
            }
            bytes = result;
            return true;
        }

        internal static void Unescape(ReadOnlySpan<byte> source, Span<byte> destination, out int written)
        {
            int idx = source.IndexOf(JsonConstants.BackSlash);
            TryUnescape(source, destination, idx, out written);
        }

        internal static bool TryUnescape(ReadOnlySpan<byte> source, Span<byte> destination, out int written)
        {
            int idx = source.IndexOf(JsonConstants.BackSlash);
            return TryUnescape(source, destination, idx, out written);
        }

        private static int ParseHex4(ReadOnlySpan<byte> source, int start) =>
            (HexValue(source[start]) << 12) | (HexValue(source[start + 1]) << 8) | (HexValue(source[start + 2]) << 4) | HexValue(source[start + 3]);

        private static bool TryUnescape(ReadOnlySpan<byte> source, Span<byte> destination, int idx, out int written)
        {
            if (idx < 0)
            {
                if (source.Length > destination.Length) { written = 0; return false; }
                source.CopyTo(destination);
                written = source.Length;
                return true;
            }
            if (idx > destination.Length)
            {
                written = 0;
                return false;
            }
            source.Slice(0, idx).CopyTo(destination);
            written = idx;

            while (true)
            {
                if (written == destination.Length) return false;
                switch (source[++idx])
                {
                    case JsonConstants.Quote: destination[written++] = JsonConstants.Quote; break;
                    case (byte)'n': destination[written++] = JsonConstants.LineFeed; break;
                    case (byte)'r': destination[written++] = JsonConstants.CarriageReturn; break;
                    case JsonConstants.BackSlash: destination[written++] = JsonConstants.BackSlash; break;
                    case JsonConstants.Slash: destination[written++] = JsonConstants.Slash; break;
                    case (byte)'t': destination[written++] = JsonConstants.Tab; break;
                    case (byte)'b': destination[written++] = JsonConstants.BackSpace; break;
                    case (byte)'f': destination[written++] = JsonConstants.FormFeed; break;
                    default:
                    {
                        int scalar = ParseHex4(source, idx + 1);
                        idx += 4;
                        if (JsonHelpers.IsInRangeInclusive((uint)scalar, JsonConstants.HighSurrogateStartValue, JsonConstants.LowSurrogateEndValue))
                        {
                            if (scalar >= JsonConstants.LowSurrogateStartValue)
                                JsonThrowHelper.ThrowInvalidOperationException_ReadInvalidUTF16(scalar);
                            if (source.Length < idx + 7 || source[idx + 1] != '\\' || source[idx + 2] != 'u')
                                JsonThrowHelper.ThrowInvalidOperationException_ReadIncompleteUTF16();
                            int lowSurrogate = ParseHex4(source, idx + 3);
                            idx += 6;
                            if (!JsonHelpers.IsInRangeInclusive((uint)lowSurrogate, JsonConstants.LowSurrogateStartValue, JsonConstants.LowSurrogateEndValue))
                                JsonThrowHelper.ThrowInvalidOperationException_ReadInvalidUTF16(lowSurrogate);
                            scalar = (JsonConstants.BitShiftBy10 * (scalar - JsonConstants.HighSurrogateStartValue))
                                + (lowSurrogate - JsonConstants.LowSurrogateStartValue) + JsonConstants.UnicodePlane01StartValue;
                        }
                        int needed = scalar < 0x80 ? 1 : scalar < 0x800 ? 2 : scalar < 0x10000 ? 3 : 4;
                        if (written + needed > destination.Length) return false;
                        if (needed == 1) destination[written++] = (byte)scalar;
                        else if (needed == 2)
                        {
                            destination[written++] = (byte)(0xC0 | (scalar >> 6));
                            destination[written++] = (byte)(0x80 | (scalar & 0x3F));
                        }
                        else if (needed == 3)
                        {
                            destination[written++] = (byte)(0xE0 | (scalar >> 12));
                            destination[written++] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
                            destination[written++] = (byte)(0x80 | (scalar & 0x3F));
                        }
                        else
                        {
                            destination[written++] = (byte)(0xF0 | (scalar >> 18));
                            destination[written++] = (byte)(0x80 | ((scalar >> 12) & 0x3F));
                            destination[written++] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
                            destination[written++] = (byte)(0x80 | (scalar & 0x3F));
                        }
                        break;
                    }
                }

                if (++idx == source.Length) return true;

                if (source[idx] != JsonConstants.BackSlash)
                {
                    ReadOnlySpan<byte> remaining = source.Slice(idx);
                    int nextUnescapedSegmentLength = remaining.IndexOf(JsonConstants.BackSlash);
                    if (nextUnescapedSegmentLength < 0) nextUnescapedSegmentLength = remaining.Length;
                    if ((uint)(written + nextUnescapedSegmentLength) >= (uint)destination.Length)
                    {
                        if (written + nextUnescapedSegmentLength == destination.Length && nextUnescapedSegmentLength == remaining.Length)
                        {
                            remaining.CopyTo(destination.Slice(written));
                            written += nextUnescapedSegmentLength;
                            return true;
                        }
                        return false;
                    }
                    remaining.Slice(0, nextUnescapedSegmentLength).CopyTo(destination.Slice(written));
                    written += nextUnescapedSegmentLength;
                    idx += nextUnescapedSegmentLength;
                    if (idx == source.Length) return true;
                }
            }
        }
    }

    /// <summary>A JSON number as decimal (Utf8Parser 'G': exponents allowed, 29 significant digits, out of range fails).</summary>
    /// <summary>
    /// Utf8Parser.TryParse(out decimal) on a JSON number: the digits go into a NumberBuffer (at most 30
    /// significant digits, the rest only marking a non-zero tail) and Number.TryNumberToDecimal rounds
    /// them half-to-even into 96 bits, keeping trailing zeros as scale and the sign of zero.
    /// </summary>
    internal static class JsonDecimalParser
    {
        private const int MaxDigits = 30;
        private const int DecimalPrecision = 29;

        public static bool TryParse(string s, out decimal value)
        {
            value = 0;
            int i = 0;
            bool negative = false;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) { negative = s[i] == '-'; i++; }
            char[] digits = new char[MaxDigits + 1];
            int count = 0;
            long scale = 0;
            bool nonZeroTail = false;
            while (i < s.Length && s[i] == '0') i++;
            for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++)
            {
                if (count < MaxDigits) digits[count++] = s[i];
                else if (s[i] != '0') nonZeroTail = true;
                scale++;
            }
            if (i < s.Length && s[i] == '.')
            {
                i++;
                for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++)
                {
                    if (count == 0 && s[i] == '0') scale--;
                    else if (count < MaxDigits) digits[count++] = s[i];
                    else if (s[i] != '0') nonZeroTail = true;
                }
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                bool expNegative = false;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) { expNegative = s[i] == '-'; i++; }
                long exponent = 0;
                for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) exponent = Math.Min(exponent * 10 + (s[i] - '0'), 1000000000L);
                scale += expNegative ? -exponent : exponent;
            }
            if (i != s.Length) return false;
            digits[count] = '\0';
            int e = (int)Math.Max(Math.Min(scale, int.MaxValue / 2), int.MinValue / 2);
            return TryNumberToDecimal(digits, e, negative, nonZeroTail, out value);
        }

        private static bool TryNumberToDecimal(char[] digits, int e, bool sign, bool nonZeroTail, out decimal value)
        {
            int p = 0;
            uint c = digits[p];
            if (c == 0)
            {
                value = new decimal(0, 0, 0, sign, (byte)Math.Clamp(-e, 0, 28));
                return true;
            }
            if (e > DecimalPrecision)
            {
                value = 0;
                return false;
            }
            ulong low64 = 0;
            while (e > -28)
            {
                e--;
                low64 *= 10;
                low64 += c - '0';
                c = digits[++p];
                if (low64 >= ulong.MaxValue / 10) break;
                if (c == 0)
                {
                    while (e > 0)
                    {
                        e--;
                        low64 *= 10;
                        if (low64 >= ulong.MaxValue / 10) break;
                    }
                    break;
                }
            }
            uint high = 0;
            while ((e > 0 || (c != 0 && e > -28))
                && (high < uint.MaxValue / 10 || (high == uint.MaxValue / 10 && (low64 < 0x99999999_99999999UL || (low64 == 0x99999999_99999999UL && c <= '5')))))
            {
                // multiply by 10
                ulong tmpLow = (uint)low64 * 10UL;
                ulong tmp64 = ((uint)(low64 >> 32) * 10UL) + (tmpLow >> 32);
                low64 = (uint)tmpLow + (tmp64 << 32);
                high = (uint)(tmp64 >> 32) + (high * 10);
                if (c != 0)
                {
                    c -= '0';
                    low64 += c;
                    if (low64 < c) high++;
                    c = digits[++p];
                }
                e--;
            }
            if (c >= '5')
            {
                bool round = true;
                if (c == '5' && (low64 & 1) == 0)
                {
                    c = digits[++p];
                    bool hasZeroTail = !nonZeroTail;
                    while (c != 0 && hasZeroTail)
                    {
                        hasZeroTail &= c == '0';
                        c = digits[++p];
                    }
                    // Exactly half and even: no rounding.
                    if (hasZeroTail) round = false;
                }
                if (round && ++low64 == 0 && ++high == 0)
                {
                    low64 = 0x99999999_9999999AUL;
                    high = uint.MaxValue / 10;
                    e++;
                }
            }
            if (e > 0)
            {
                value = 0;
                return false;
            }
            if (e <= -DecimalPrecision)
            {
                // A zero (or a value rounding to zero) with more scale than a decimal holds.
                value = new decimal(0, 0, 0, sign, DecimalPrecision - 1);
            }
            else
            {
                value = new decimal((int)low64, (int)(low64 >> 32), (int)high, sign, (byte)-e);
            }
            return true;
        }
    }

    /// <summary>Utf8Parser.TryParse semantics (format 'G', whole span consumed) for the reader's numbers.</summary>
    internal static class JsonNumberParser
    {
        private static bool TryParseInteger(ReadOnlySpan<byte> span, bool allowNegative, out ulong magnitude, out bool negative)
        {
            magnitude = 0;
            negative = false;
            int i = 0;
            if (span.Length == 0) return false;
            if (span[0] == '-')
            {
                if (!allowNegative) return false;
                negative = true;
                i++;
            }
            else if (span[0] == '+') i++;
            if (i >= span.Length) return false;
            for (; i < span.Length; i++)
            {
                uint d = (uint)(span[i] - '0');
                if (d > 9) return false;
                if (magnitude > (ulong.MaxValue - d) / 10) return false;
                magnitude = magnitude * 10 + d;
            }
            return true;
        }

        private static bool TryParseSigned(ReadOnlySpan<byte> span, long min, long max, out long value)
        {
            value = 0;
            if (!TryParseInteger(span, true, out ulong magnitude, out bool negative)) return false;
            if (negative)
            {
                if (magnitude > (ulong)(-(min + 1)) + 1) return false;
                value = (long)(0 - magnitude);
            }
            else
            {
                if (magnitude > (ulong)max) return false;
                value = (long)magnitude;
            }
            return true;
        }

        private static bool TryParseUnsigned(ReadOnlySpan<byte> span, ulong max, out ulong value)
        {
            value = 0;
            if (!TryParseInteger(span, false, out ulong magnitude, out bool negative)) return false;
            if (magnitude > max) return false;
            value = magnitude;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out byte value)
        {
            bool ok = TryParseUnsigned(span, byte.MaxValue, out ulong v);
            value = (byte)v;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out sbyte value)
        {
            bool ok = TryParseSigned(span, sbyte.MinValue, sbyte.MaxValue, out long v);
            value = (sbyte)v;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out short value)
        {
            bool ok = TryParseSigned(span, short.MinValue, short.MaxValue, out long v);
            value = (short)v;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out int value)
        {
            bool ok = TryParseSigned(span, int.MinValue, int.MaxValue, out long v);
            value = (int)v;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out long value) => TryParseSigned(span, long.MinValue, long.MaxValue, out value);

        public static bool TryParse(ReadOnlySpan<byte> span, out ushort value)
        {
            bool ok = TryParseUnsigned(span, ushort.MaxValue, out ulong v);
            value = (ushort)v;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out uint value)
        {
            bool ok = TryParseUnsigned(span, uint.MaxValue, out ulong v);
            value = (uint)v;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out ulong value) => TryParseUnsigned(span, ulong.MaxValue, out value);

        private static string Ascii(ReadOnlySpan<byte> span)
        {
            char[] chars = new char[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                byte b = span[i];
                if (b >= 0x80) return null;
                chars[i] = (char)b;
            }
            return new string(chars);
        }

        /// <summary>Only JSON number syntax (optional '-', digits, fraction, exponent) — what Utf8Parser accepts here.</summary>
        private static bool IsNumberSyntax(ReadOnlySpan<byte> span)
        {
            int i = 0;
            if (i < span.Length && (span[i] == '-' || span[i] == '+')) i++;
            int digits = 0;
            while (i < span.Length && JsonHelpers.IsDigit(span[i])) { i++; digits++; }
            if (i < span.Length && span[i] == '.')
            {
                i++;
                while (i < span.Length && JsonHelpers.IsDigit(span[i])) { i++; digits++; }
            }
            if (digits == 0) return false;
            if (i < span.Length && (span[i] == 'e' || span[i] == 'E'))
            {
                i++;
                if (i < span.Length && (span[i] == '-' || span[i] == '+')) i++;
                int expDigits = 0;
                while (i < span.Length && JsonHelpers.IsDigit(span[i])) { i++; expDigits++; }
                if (expDigits == 0) return false;
            }
            return i == span.Length;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out double value)
        {
            value = 0;
            if (!IsNumberSyntax(span)) return false;
            string s = Ascii(span);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out float value)
        {
            value = 0;
            if (!IsNumberSyntax(span)) return false;
            string s = Ascii(span);
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
            value = (float)d;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out decimal value)
        {
            value = 0;
            if (!IsNumberSyntax(span)) return false;
            return JsonDecimalParser.TryParse(Ascii(span), out value);
        }
    }
}
