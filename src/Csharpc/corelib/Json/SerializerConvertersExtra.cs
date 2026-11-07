// JsonMetadataServices' converters for the types System.Text.Json's generated contexts name beyond the numeric
// primitives: Int128, UInt128, Half, DateOnly, TimeOnly, Uri, Version and (Read)Memory<byte>, with the text
// forms, strictness and errors of dotnet/runtime's converters (release/11.0).
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsharpcCoreLib
{
    internal sealed class Int128Converter : IntegerConverter<Int128>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out Int128 value) => TryParse(reader.ValueSpan, out value);

        protected override bool TryParse(ReadOnlySpan<byte> span, out Int128 value) =>
            Int128Parser.ParseSigned(Ascii(span), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) == Int128ParseStatus.Ok;

        internal static string Ascii(ReadOnlySpan<byte> span)
        {
            char[] chars = new char[span.Length];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)span[i];
            return new string(chars);
        }

        protected override string Format(Int128 value) => value.ToString();
        protected override string TypeName => "Int128";
        protected override NumericType TypeNumeric => NumericType.Int128;
    }

    internal sealed class UInt128Converter : IntegerConverter<UInt128>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out UInt128 value) => TryParse(reader.ValueSpan, out value);

        protected override bool TryParse(ReadOnlySpan<byte> span, out UInt128 value) =>
            Int128Parser.ParseUnsigned(Int128Converter.Ascii(span), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) == Int128ParseStatus.Ok;

        protected override string Format(UInt128 value) => value.ToString();
        protected override string TypeName => "UInt128";
        protected override NumericType TypeNumeric => NumericType.UInt128;
    }

    internal sealed class HalfConverter : JsonPrimitiveConverter<Half>
    {
        internal override JsonSchema GetSchema(JsonNumberHandling numberHandling) => GetSchemaForNumericType(JsonSchemaType.Number, numberHandling, isIeeeFloatingPoint: true);

        internal override bool IsNumberConverter => true;

        public override Half Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType != JsonTokenType.Number) JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(reader.TokenType);
            return ParseNumber(ref reader);
        }

        private static Half ParseNumber(ref Utf8JsonReaderImpl reader)
        {
            if (!Half.TryParse(Int128Converter.Ascii(reader.ValueSpan), NumberStyles.Float, CultureInfo.InvariantCulture, out Half value) || !Half.IsFinite(value))
                JsonThrowHelper.ThrowFormatException(NumericType.Single);
            return value;
        }

        // The raw number text, validated as JSON: NaN and the infinities are not JSON numbers.
        public override void Write(Utf8JsonWriterImpl writer, Half value, JsonSerializerOptionsImpl options) =>
            writer.WriteRawValue(value.ToString("G", CultureInfo.InvariantCulture));

        internal override Half ReadNumberWithCustomHandling(ref Utf8JsonReaderImpl reader, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string text = reader.GetString();
                if ((handling & JsonNumberHandling.AllowReadingFromString) != 0
                    && Half.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out Half parsed)) return parsed;
                if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
                {
                    if (text == "NaN") return Half.NaN;
                    if (text == "Infinity") return Half.PositiveInfinity;
                    if (text == "-Infinity") return Half.NegativeInfinity;
                }
                JsonThrowHelper.ThrowFormatException(NumericType.Single);
            }
            return Read(ref reader, typeof(Half), options);
        }

        internal override void WriteNumberWithCustomHandling(Utf8JsonWriterImpl writer, Half value, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if (!Half.IsFinite(value) && (handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
                writer.WriteStringValue(Half.IsNaN(value) ? "NaN" : Half.IsPositiveInfinity(value) ? "Infinity" : "-Infinity");
            else if ((handling & JsonNumberHandling.WriteAsString) != 0) writer.WriteStringValue(value.ToString("G", CultureInfo.InvariantCulture));
            else Write(writer, value, options);
        }

        public override Half ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (!Half.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out Half value)) JsonThrowHelper.ThrowFormatException(NumericType.Single);
            return value;
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, Half value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value.ToString("G", CultureInfo.InvariantCulture));
    }

    internal sealed class DateOnlyConverter : JsonPrimitiveConverter<DateOnly>
    {
        internal override JsonSchema GetSchema(JsonNumberHandling _) => new JsonSchema { Type = JsonSchemaType.String, Format = "date" };

        public override DateOnly Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType != JsonTokenType.String) JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(reader.TokenType);
            return Parse(reader.GetString());
        }

        // yyyy-MM-dd and nothing else
        private static DateOnly Parse(string s)
        {
            if (s.Length == 10 && s[4] == '-' && s[7] == '-' && Digits(s, 0, 4, out int year) && Digits(s, 5, 2, out int month) && Digits(s, 8, 2, out int day)
                && year >= 1 && month >= 1 && month <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                return new DateOnly(year, month, day);
            JsonThrowHelper.ThrowFormatException("DateOnly");
            return default;
        }

        internal static bool Digits(string s, int start, int count, out int value)
        {
            value = 0;
            for (int i = start; i < start + count; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
                value = value * 10 + (s[i] - '0');
            }
            return true;
        }

        public override void Write(Utf8JsonWriterImpl writer, DateOnly value, JsonSerializerOptionsImpl options) => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));

        public override DateOnly ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => Parse(reader.GetString());

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, DateOnly value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value.ToString("O", CultureInfo.InvariantCulture));
    }

    internal sealed class TimeOnlyConverter : JsonPrimitiveConverter<TimeOnly>
    {
        internal override JsonSchema GetSchema(JsonNumberHandling _) => new JsonSchema { Type = JsonSchemaType.String, Format = "time" };

        public override TimeOnly Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType != JsonTokenType.String) JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(reader.TokenType);
            return Parse(reader.GetString());
        }

        // H:m, H:m:s and H:m:s.f to H:m:s.fffffff: one or two digits per field, at most seven fraction digits, 3-16 chars.
        private static TimeOnly Parse(string s)
        {
            int at = 0;
            if (s.Length >= 3 && s.Length <= 16 && Field(s, ref at, 23, out int hour) && At(s, ref at, ':') && Field(s, ref at, 59, out int minute))
            {
                int second = 0;
                long fraction = 0;
                bool ok = true;
                if (at < s.Length)
                {
                    ok = At(s, ref at, ':') && Field(s, ref at, 59, out second);
                    if (ok && at < s.Length)
                    {
                        int digits = s.Length - at - 1;
                        ok = s[at] == '.' && digits >= 1 && digits <= 7;
                        for (int i = 0; ok && i < 7; i++)
                        {
                            int d = i < digits ? s[at + 1 + i] - '0' : 0;
                            if (d < 0 || d > 9) ok = false;
                            fraction = fraction * 10 + d;
                        }
                    }
                }
                if (ok) return new TimeOnly(((hour * 60L + minute) * 60 + second) * 10_000_000L + fraction);
            }
            JsonThrowHelper.ThrowFormatException("TimeOnly");
            return default;
        }

        private static bool At(string s, ref int at, char c) => at < s.Length && s[at++] == c;

        // one or two digits, at most `max`
        private static bool Field(string s, ref int at, int max, out int value)
        {
            value = 0;
            int start = at;
            while (at < s.Length && at - start < 2 && s[at] >= '0' && s[at] <= '9') value = value * 10 + (s[at++] - '0');
            return at > start && value <= max;
        }

        private static string Text(TimeOnly value)
        {
            string text = value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            long fraction = value.Ticks % 10_000_000L;
            return fraction == 0 ? text : text + "." + fraction.ToString("D7", CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriterImpl writer, TimeOnly value, JsonSerializerOptionsImpl options) => writer.WriteStringValue(Text(value));

        public override TimeOnly ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => Parse(reader.GetString());

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, TimeOnly value, JsonSerializerOptionsImpl options) => writer.WritePropertyName(Text(value));
    }

    internal sealed class UriConverter : JsonPrimitiveConverter<Uri>
    {
        internal override JsonSchema GetSchema(JsonNumberHandling _) => new JsonSchema { Type = JsonSchemaType.String, Format = "uri" };

        public override Uri Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.String) JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(reader.TokenType);
            return Parse(reader.GetString());
        }

        private static Uri Parse(string s)
        {
            if (!Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out Uri uri)) JsonThrowHelper.ThrowFormatException("Uri");
            return uri;
        }

        public override void Write(Utf8JsonWriterImpl writer, Uri value, JsonSerializerOptionsImpl options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value.OriginalString);
        }

        public override Uri ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => Parse(reader.GetString());

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, Uri value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value.OriginalString);
    }

    internal sealed class VersionConverter : JsonPrimitiveConverter<Version>
    {
        internal override JsonSchema GetSchema(JsonNumberHandling _) => new JsonSchema
        {
            Type = JsonSchemaType.String,
            Comment = "Represents a version string.",
            Pattern = @"^\d+(\.\d+){1,3}$",
        };

        public override Version Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.String) JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(reader.TokenType);
            return Parse(reader.GetString());
        }

        private static Version Parse(string s)
        {
            if (!Version.TryParse(s, out Version version)) JsonThrowHelper.ThrowFormatException("Version");
            return version;
        }

        public override void Write(Utf8JsonWriterImpl writer, Version value, JsonSerializerOptionsImpl options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value.ToString());
        }

        public override Version ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => Parse(reader.GetString());

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, Version value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value.ToString());
    }

    internal sealed class ReadOnlyMemoryByteConverter : JsonConverterImpl<ReadOnlyMemory<byte>>
    {
        internal override JsonSchema GetSchema(JsonNumberHandling _) => new JsonSchema { Type = JsonSchemaType.String, ContentEncoding = "base64" };

        internal override bool IsInternalConverter => true;

        public override ReadOnlyMemory<byte> Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            reader.TokenType == JsonTokenType.Null ? default : new ReadOnlyMemory<byte>(reader.GetBytesFromBase64());

        public override void Write(Utf8JsonWriterImpl writer, ReadOnlyMemory<byte> value, JsonSerializerOptionsImpl options) =>
            writer.WriteBase64StringValue(value.Span);
    }

    internal sealed class MemoryByteConverter : JsonConverterImpl<Memory<byte>>
    {
        internal override JsonSchema GetSchema(JsonNumberHandling _) => new JsonSchema { Type = JsonSchemaType.String, ContentEncoding = "base64" };

        internal override bool IsInternalConverter => true;

        public override Memory<byte> Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            reader.TokenType == JsonTokenType.Null ? default : new Memory<byte>(reader.GetBytesFromBase64());

        public override void Write(Utf8JsonWriterImpl writer, Memory<byte> value, JsonSerializerOptionsImpl options) =>
            writer.WriteBase64StringValue(value.Span);
    }

    internal sealed class ReadOnlyMemoryConverter<TElement> : CollectionConverter<ReadOnlyMemory<TElement>, TElement>
    {
        protected override bool NullIsDefault => true;
        protected override IEnumerable<TElement> Elements(ReadOnlyMemory<TElement> collection) => collection.ToArray();
        protected override ReadOnlyMemory<TElement> FromElements(List<TElement> elements, JsonReadState state) => new ReadOnlyMemory<TElement>(elements.ToArray());
    }

    internal sealed class MemoryConverter<TElement> : CollectionConverter<Memory<TElement>, TElement>
    {
        protected override bool NullIsDefault => true;
        protected override IEnumerable<TElement> Elements(Memory<TElement> collection) => collection.ToArray();
        protected override Memory<TElement> FromElements(List<TElement> elements, JsonReadState state) => new Memory<TElement>(elements.ToArray());
    }
}
