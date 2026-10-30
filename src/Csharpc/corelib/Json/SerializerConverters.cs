// System.Text.Json converters: JsonConverter / JsonConverter<T> / JsonConverterFactory, the built-in value
// converters, enums (JsonStringEnumConverter), Nullable<T>, object, JsonElement/JsonDocument.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Serialization (release/11.0):
// JsonConverter*.cs and Converters/Value/*.cs. csharpc's converters are not resumable (whole payloads are
// read before deserializing), so the ReadStack/WriteStack machinery reduces to a path stack for messages.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.Serialization.JsonConverter")]
    public abstract class JsonConverterImpl
    {
        internal JsonConverterImpl() { }

        public abstract Type Type { get; }

        public abstract bool CanConvert(Type typeToConvert);

        /// <summary>Built-in converters skip the read/write verification user converters get.</summary>
        internal virtual bool IsInternalConverter => false;

        internal virtual JsonTypeInfoKind Kind => JsonTypeInfoKind.None;

        /// <summary>Number handling applies (numeric primitives, their collections, object).</summary>
        internal virtual bool IsNumberHandlingApplicable => false;

        internal virtual void WriteAsObjectWithState(Utf8JsonWriterImpl writer, object value, JsonSerializerOptionsImpl options, JsonWriteState state) =>
            throw new InvalidOperationException("The converter '" + GetType().ToString() + "' cannot write values.");

        internal virtual object ReadAsObjectWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state) =>
            throw new InvalidOperationException("The converter '" + GetType().ToString() + "' cannot read values.");
    }

    [Shadows("System.Text.Json.Serialization.JsonConverterFactory")]
    public abstract class JsonConverterFactoryImpl : JsonConverterImpl
    {
        protected JsonConverterFactoryImpl() { }

        public sealed override Type Type => null;

        public abstract JsonConverterImpl CreateConverter(Type typeToConvert, JsonSerializerOptionsImpl options);
    }

    [Shadows("System.Text.Json.Serialization.JsonConverter`1")]
    public abstract class JsonConverterImpl<T> : JsonConverterImpl
    {
        /// <summary>JsonObjectCreationHandling.Populate support: reading into an existing instance.</summary>
        internal virtual bool CanPopulate => false;

        internal virtual T ReadInto(ref Utf8JsonReaderImpl reader, T existing, JsonSerializerOptionsImpl options, JsonReadState state) =>
            throw new InvalidOperationException("csharpc: converter cannot populate");

        private bool _nullHandlingResolved;
        private bool _usesDefaultHandleNull;
        internal bool HandleNullOnRead;
        internal bool HandleNullOnWrite;

        protected internal JsonConverterImpl() { }

        public virtual bool HandleNull
        {
            get
            {
                _usesDefaultHandleNull = true;
                return false;
            }
        }

        private void ResolveNullHandling()
        {
            if (_nullHandlingResolved) return;
            if (HandleNull)
            {
                HandleNullOnRead = true;
                HandleNullOnWrite = true;
            }
            else if (_usesDefaultHandleNull)
            {
                // A type that can't be null lets its converter see null tokens (backwards compatible with 3.0).
                HandleNullOnRead = default(T) != null;
                HandleNullOnWrite = false;
            }
            _nullHandlingResolved = true;
        }

        public sealed override Type Type => typeof(T);

        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

        public abstract T Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options);

        public abstract void Write(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options);

        public virtual T ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            JsonConverterImpl<T> fallback = JsonBuiltInTypeInfos.DictionaryKeyConverter<T>(options);
            if (fallback == null || ReferenceEquals(fallback, this))
                throw new NotSupportedException("The type '" + typeof(T).ToString() + "' is not a supported dictionary key using converter of type '" + GetType().ToString() + "'.");
            return fallback.ReadAsPropertyName(ref reader, typeToConvert, options);
        }

        public virtual void WriteAsPropertyName(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options)
        {
            JsonConverterImpl<T> fallback = JsonBuiltInTypeInfos.DictionaryKeyConverter<T>(options);
            if (fallback == null || ReferenceEquals(fallback, this))
                throw new NotSupportedException("The type '" + typeof(T).ToString() + "' is not a supported dictionary key using converter of type '" + GetType().ToString() + "'.");
            fallback.WriteAsPropertyName(writer, value, options);
        }

        /// <summary>Numbers read under JsonNumberHandling (overridden by the numeric converters).</summary>
        internal virtual T ReadNumberWithCustomHandling(ref Utf8JsonReaderImpl reader, JsonNumberHandling handling, JsonSerializerOptionsImpl options) =>
            Read(ref reader, typeof(T), options);

        internal virtual void WriteNumberWithCustomHandling(Utf8JsonWriterImpl writer, T value, JsonNumberHandling handling, JsonSerializerOptionsImpl options) =>
            Write(writer, value, options);

        internal virtual bool IsNumberConverter => false;

        internal override bool IsNumberHandlingApplicable => IsNumberConverter;

        /// <summary>JsonConverter&lt;T&gt;.TryRead for value converters: null handling, number handling, read verification.</summary>
        internal virtual T ReadWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state)
        {
            ResolveNullHandling();
            if (reader.TokenType == JsonTokenType.Null && !HandleNullOnRead)
            {
                if (default(T) != null) throw JsonSerializerHelpers.UnableToConvert(typeof(T));
                return default(T);
            }
            if (IsInternalConverter)
            {
                if (state.NumberHandling.HasValue && IsNumberConverter)
                    return ReadNumberWithCustomHandling(ref reader, state.NumberHandling.Value, options);
                return Read(ref reader, typeToConvert, options);
            }
            JsonTokenType tokenType = reader.TokenType;
            int depth = reader.CurrentDepth;
            long bytesConsumed = reader.BytesConsumed;
            T value = Read(ref reader, typeToConvert, options);
            VerifyRead(tokenType, depth, bytesConsumed, ref reader);
            return value;
        }

        private void VerifyRead(JsonTokenType tokenType, int depth, long bytesConsumed, ref Utf8JsonReaderImpl reader)
        {
            switch (tokenType)
            {
                case JsonTokenType.StartArray:
                    if (reader.TokenType != JsonTokenType.EndArray || reader.CurrentDepth != depth) ThrowReadTooMuch();
                    break;
                case JsonTokenType.StartObject:
                    if (reader.TokenType != JsonTokenType.EndObject || reader.CurrentDepth != depth) ThrowReadTooMuch();
                    break;
                default:
                    if (reader.BytesConsumed != bytesConsumed) ThrowReadTooMuch();
                    break;
            }
        }

        private void ThrowReadTooMuch() =>
            throw new JsonExceptionImpl("The converter '" + GetType().ToString() + "' read too much or not enough.") { AppendPathInformation = true };

        internal virtual void WriteWithState(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (writer.CurrentDepth >= options.EffectiveMaxDepth)
                throw new JsonExceptionImpl(JsonSerializerHelpers.CycleDetected(options.EffectiveMaxDepth)) { AppendPathInformation = true };
            ResolveNullHandling();
            if (value == null && !HandleNullOnWrite)
            {
                writer.WriteNullValue();
                return;
            }
            if (IsInternalConverter)
            {
                if (state.NumberHandling.HasValue && IsNumberConverter) WriteNumberWithCustomHandling(writer, value, state.NumberHandling.Value, options);
                else Write(writer, value, options);
                return;
            }
            int depth = writer.CurrentDepth;
            Write(writer, value, options);
            if (depth != writer.CurrentDepth)
                throw new JsonExceptionImpl("The converter '" + GetType().ToString() + "' wrote too much or not enough.") { AppendPathInformation = true };
        }

        internal override void WriteAsObjectWithState(Utf8JsonWriterImpl writer, object value, JsonSerializerOptionsImpl options, JsonWriteState state) =>
            WriteWithState(writer, value == null ? default(T) : (T)value, options, state);

        internal override object ReadAsObjectWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state) =>
            ReadWithState(ref reader, typeToConvert, options, state);
    }

    internal static class JsonSerializerHelpers
    {
        internal static string CycleDetected(int maxDepth) =>
            "A possible object cycle was detected. This can either be due to a cycle or if the object depth is larger than the maximum allowed depth of "
            + maxDepth.ToString() + ". Consider using ReferenceHandler.Preserve on JsonSerializerOptions to support cycles.";

        internal static JsonExceptionImpl UnableToConvert(Type type) =>
            new JsonExceptionImpl(JsonSR.Format(JsonSR.DeserializeUnableToConvertValue, type.ToString())) { AppendPathInformation = true };

        /// <summary>A JsonException whose message the serializer fills in (with the type and path).</summary>
        internal static JsonExceptionImpl DefaultJsonException() => new JsonExceptionImpl() { AppendPathInformation = true };

        internal static bool IsValidNumberHandlingValue(JsonNumberHandling handling) =>
            (int)handling >= 0 && (int)handling <= 7;

        /// <summary>JsonSerializerOptions.ExpandConverterFactory + CheckConverterNullabilityIsSameAsPropertyType.</summary>
        internal static JsonConverterImpl ExpandConverter(Type type, JsonConverterImpl converter, JsonSerializerOptionsImpl options)
        {
            if (converter is JsonConverterFactoryImpl factory)
            {
                JsonConverterImpl created = factory.CreateConverter(type, options);
                if (created == null || created is JsonConverterFactoryImpl)
                    throw new InvalidOperationException("The converter '" + factory.GetType().ToString() + "' cannot return null or a JsonConverterFactory instance.");
                converter = created;
            }
            return converter;
        }

        internal static bool IsIntegerString(string s)
        {
            // JsonHelpers.IntegerRegex: ^\s*(?:\+|\-)?[0-9]+\s*$
            int i = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            int digits = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return digits > 0 && i == s.Length;
        }
    }

    /// <summary>The path stack behind exception messages ("Path: $.Items[0].Name"), and per-call settings.</summary>
    internal sealed class JsonReadState
    {
        private readonly List<string> _names = new List<string>();
        private readonly List<int> _indexes = new List<int>();
        internal JsonNumberHandling? NumberHandling;
        internal Type CurrentValueType;
        internal JsonSerializerOptionsImpl Options;
        internal Dictionary<string, object> References;

        public JsonReadState(JsonSerializerOptionsImpl options) { Options = options; }

        public void PushProperty(string name)
        {
            _names.Add(name);
            _indexes.Add(-1);
        }

        public void PushArray()
        {
            _names.Add(null);
            _indexes.Add(0);
        }

        public void SetIndex(int index) => _indexes[_indexes.Count - 1] = index;

        public void Pop()
        {
            _names.RemoveAt(_names.Count - 1);
            _indexes.RemoveAt(_indexes.Count - 1);
        }

        public int Depth => _names.Count;

        public void Truncate(int depth)
        {
            while (_names.Count > depth) Pop();
        }

        public string JsonPath()
        {
            StringBuilder sb = new StringBuilder("$");
            for (int i = 0; i < _names.Count; i++)
            {
                if (_names[i] != null) JsonPathHelpers.AppendPropertyName(sb, _names[i]);
                if (_indexes[i] >= 0) sb.Append('[').Append(_indexes[i]).Append(']');
            }
            return sb.ToString();
        }
    }

    internal sealed class JsonWriteState
    {
        private readonly List<string> _names = new List<string>();
        internal JsonNumberHandling? NumberHandling;
        internal JsonSerializerOptionsImpl Options;
        internal ReferenceResolverImpl ReferenceResolver;
        internal List<object> Ancestors; // IgnoreCycles

        public JsonWriteState(JsonSerializerOptionsImpl options) { Options = options; }

        /// <summary>A cached state starts over (Serializer.WriteCore's state cache).</summary>
        internal void Reset(JsonSerializerOptionsImpl options)
        {
            _names.Clear();
            NumberHandling = null;
            Options = options;
            ReferenceResolver = null;
            Ancestors = null;
        }

        public void Push(string name) => _names.Add(name);

        public void Pop() => _names.RemoveAt(_names.Count - 1);

        public string PropertyPath()
        {
            StringBuilder sb = new StringBuilder("$");
            for (int i = 0; i < _names.Count; i++)
                if (_names[i] != null) JsonPathHelpers.AppendPropertyName(sb, _names[i]);
            return sb.ToString();
        }
    }

    /// <summary>Utf8Parser's TimeSpan "c" format: [-][d.]hh:mm:ss[.fffffff] (hh &lt; 24, mm/ss &lt; 60).</summary>
    internal static class JsonTimeSpanParser
    {
        public static bool TryParseConstant(string s, out TimeSpan value)
        {
            value = default;
            int i = 0;
            bool negative = false;
            if (i < s.Length && s[i] == '-') { negative = true; i++; }
            if (!ReadNumber(s, ref i, 8, out long first)) return false;
            long days = 0;
            long hours;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                days = first;
                if (!ReadNumber(s, ref i, 2, out hours)) return false;
            }
            else hours = first;
            if (i >= s.Length || s[i] != ':') return false;
            i++;
            if (!ReadNumber(s, ref i, 2, out long minutes)) return false;
            if (i >= s.Length || s[i] != ':') return false;
            i++;
            if (!ReadNumber(s, ref i, 2, out long seconds)) return false;
            long fraction = 0;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                int digits = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                {
                    if (digits < 7) fraction = fraction * 10 + (s[i] - '0');
                    digits++;
                    i++;
                }
                if (digits == 0 || digits > 7) return false;
                for (; digits < 7; digits++) fraction *= 10;
            }
            if (i != s.Length) return false;
            if (hours > 23 || minutes > 59 || seconds > 59 || days > 10675199) return false;
            long ticks = ((days * 24 + hours) * 60 + minutes) * 60 * TimeSpan.TicksPerSecond + seconds * TimeSpan.TicksPerSecond + fraction;
            if (ticks < 0) return false;
            value = new TimeSpan(negative ? -ticks : ticks);
            return true;
        }

        private static bool ReadNumber(string s, ref int i, int maxDigits, out long value)
        {
            value = 0;
            int start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                value = value * 10 + (s[i] - '0');
                i++;
                if (i - start > maxDigits) return false;
            }
            return i > start;
        }
    }

    internal static class JsonPathHelpers
    {
        private const string SpecialCharacters = "$. '/\"[]()\t\n\r\f\b\\\u0085\u2028\u2029";

        public static void AppendPropertyName(StringBuilder sb, string propertyName)
        {
            bool special = false;
            for (int i = 0; i < propertyName.Length && !special; i++)
                if (SpecialCharacters.IndexOf(propertyName[i]) >= 0) special = true;
            if (!special)
            {
                sb.Append('.').Append(propertyName);
                return;
            }
            sb.Append("['");
            for (int i = 0; i < propertyName.Length; i++)
            {
                char c = propertyName[i];
                if (c == '\\' || c == '\'') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append("']");
        }
    }

    // ------------------------------------------------------------------------------------------
    // Built-in value converters
    // ------------------------------------------------------------------------------------------

    internal abstract class JsonPrimitiveConverter<T> : JsonConverterImpl<T>
    {
        internal override bool IsInternalConverter => true;

        public override T ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            throw new NotSupportedException("The type '" + typeof(T).ToString() + "' is not a supported dictionary key using converter of type '" + GetType().ToString() + "'.");

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options) =>
            throw new NotSupportedException("The type '" + typeof(T).ToString() + "' is not a supported dictionary key using converter of type '" + GetType().ToString() + "'.");

        internal static string PropertyNameText(ref Utf8JsonReaderImpl reader) => reader.GetString();
    }

    internal sealed class BooleanConverter : JsonPrimitiveConverter<bool>
    {
        public override bool Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => reader.GetBoolean();
        public override void Write(Utf8JsonWriterImpl writer, bool value, JsonSerializerOptionsImpl options) => writer.WriteBooleanValue(value);

        public override bool ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            string s = reader.GetString().Trim();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new FormatException("The JSON value is not in a supported Boolean format.");
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, bool value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value ? "true" : "false");
    }

    /// <summary>Integers: number tokens, or strings under AllowReadingFromString/WriteAsString.</summary>
    internal abstract class IntegerConverter<T> : JsonPrimitiveConverter<T>
    {
        internal override bool IsNumberConverter => true;

        protected abstract bool TryGet(ref Utf8JsonReaderImpl reader, out T value);
        protected abstract bool TryParse(ReadOnlySpan<byte> span, out T value);
        protected abstract string Format(T value);
        protected abstract string TypeName { get; }

        public override T Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType != JsonTokenType.Number) JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(reader.TokenType);
            if (!TryGet(ref reader, out T value)) JsonThrowHelper.ThrowFormatException(TypeNumeric);
            return value;
        }

        protected abstract NumericType TypeNumeric { get; }

        public override void Write(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options) => writer.WriteNumberValueRaw(Format(value));

        internal override T ReadNumberWithCustomHandling(ref Utf8JsonReaderImpl reader, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.String && (handling & JsonNumberHandling.AllowReadingFromString) != 0)
            {
                byte[] text = reader.ValueIsEscaped ? JsonReaderHelper.GetUnescaped(reader.ValueSpan) : reader.ValueSpan.ToArray();
                if (TryParse(text, out T parsed)) return parsed;
                JsonThrowHelper.ThrowFormatException(TypeNumeric);
            }
            return Read(ref reader, typeof(T), options);
        }

        internal override void WriteNumberWithCustomHandling(Utf8JsonWriterImpl writer, T value, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if ((handling & JsonNumberHandling.WriteAsString) != 0) writer.WriteStringValue(Format(value));
            else writer.WriteNumberValueRaw(Format(value));
        }

        public override T ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            byte[] text = reader.ValueIsEscaped ? JsonReaderHelper.GetUnescaped(reader.ValueSpan) : reader.ValueSpan.ToArray();
            if (TryParse(text, out T value)) return value;
            JsonThrowHelper.ThrowFormatException(TypeNumeric);
            return default(T);
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(Format(value));
    }

    internal sealed class Int32Converter : IntegerConverter<int>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out int value) => reader.TryGetInt32(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out int value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(int value) => value.ToString();
        protected override string TypeName => "Int32";
        protected override NumericType TypeNumeric => NumericType.Int32;
    }

    internal sealed class Int64Converter : IntegerConverter<long>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out long value) => reader.TryGetInt64(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out long value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(long value) => value.ToString();
        protected override string TypeName => "Int64";
        protected override NumericType TypeNumeric => NumericType.Int64;
    }

    internal sealed class Int16Converter : IntegerConverter<short>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out short value) => reader.TryGetInt16(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out short value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(short value) => value.ToString();
        protected override string TypeName => "Int16";
        protected override NumericType TypeNumeric => NumericType.Int16;
    }

    internal sealed class SByteConverter : IntegerConverter<sbyte>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out sbyte value) => reader.TryGetSByte(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out sbyte value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(sbyte value) => value.ToString();
        protected override string TypeName => "SByte";
        protected override NumericType TypeNumeric => NumericType.SByte;
    }

    internal sealed class ByteConverter : IntegerConverter<byte>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out byte value) => reader.TryGetByte(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out byte value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(byte value) => value.ToString();
        protected override string TypeName => "Byte";
        protected override NumericType TypeNumeric => NumericType.Byte;
    }

    internal sealed class UInt16Converter : IntegerConverter<ushort>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out ushort value) => reader.TryGetUInt16(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out ushort value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(ushort value) => value.ToString();
        protected override string TypeName => "UInt16";
        protected override NumericType TypeNumeric => NumericType.UInt16;
    }

    internal sealed class UInt32Converter : IntegerConverter<uint>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out uint value) => reader.TryGetUInt32(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out uint value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(uint value) => value.ToString();
        protected override string TypeName => "UInt32";
        protected override NumericType TypeNumeric => NumericType.UInt32;
    }

    internal sealed class UInt64Converter : IntegerConverter<ulong>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out ulong value) => reader.TryGetUInt64(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out ulong value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(ulong value) => value.ToString();
        protected override string TypeName => "UInt64";
        protected override NumericType TypeNumeric => NumericType.UInt64;
    }

    internal sealed class DecimalConverter : IntegerConverter<decimal>
    {
        protected override bool TryGet(ref Utf8JsonReaderImpl reader, out decimal value) => reader.TryGetDecimal(out value);
        protected override bool TryParse(ReadOnlySpan<byte> span, out decimal value) => JsonNumberParser.TryParse(span, out value);
        protected override string Format(decimal value) => value.ToString();
        protected override string TypeName => "Decimal";
        protected override NumericType TypeNumeric => NumericType.Decimal;
    }

    /// <summary>double/float: finite numbers only, unless AllowNamedFloatingPointLiterals ("NaN", "Infinity", "-Infinity").</summary>
    internal sealed class DoubleConverter : JsonPrimitiveConverter<double>
    {
        internal override bool IsNumberConverter => true;

        public override double Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => reader.GetDouble();

        public override void Write(Utf8JsonWriterImpl writer, double value, JsonSerializerOptionsImpl options) => writer.WriteNumberValue(value);

        internal override double ReadNumberWithCustomHandling(ref Utf8JsonReaderImpl reader, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                byte[] text = reader.ValueIsEscaped ? JsonReaderHelper.GetUnescaped(reader.ValueSpan) : reader.ValueSpan.ToArray();
                if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && JsonReaderHelper.TryGetFloatingPointConstant(text, out double constant))
                    return constant;
                if ((handling & JsonNumberHandling.AllowReadingFromString) != 0)
                {
                    if (JsonNumberParser.TryParse(text, out double parsed) && !double.IsInfinity(parsed)) return parsed;
                    JsonThrowHelper.ThrowFormatException(NumericType.Double);
                }
            }
            return reader.GetDouble();
        }

        internal override void WriteNumberWithCustomHandling(Utf8JsonWriterImpl writer, double value, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if ((handling & JsonNumberHandling.WriteAsString) != 0)
            {
                if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && !double.IsFinite(value)) writer.WriteStringValue(NamedLiteral(value));
                else
                {
                    Utf8JsonWriterImpl.ValidateFinite(value);
                    writer.WriteStringValue(value.ToString());
                }
            }
            else if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && !double.IsFinite(value)) writer.WriteStringValue(NamedLiteral(value));
            else writer.WriteNumberValue(value);
        }

        internal static string NamedLiteral(double value) =>
            double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity";

        public override double ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            byte[] text = reader.ValueIsEscaped ? JsonReaderHelper.GetUnescaped(reader.ValueSpan) : reader.ValueSpan.ToArray();
            if (JsonReaderHelper.TryGetFloatingPointConstant(text, out double constant)) return constant;
            if (JsonNumberParser.TryParse(text, out double parsed)) return parsed;
            JsonThrowHelper.ThrowFormatException(NumericType.Double);
            return 0;
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, double value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(double.IsFinite(value) ? value.ToString() : NamedLiteral(value));
    }

    internal sealed class SingleConverter : JsonPrimitiveConverter<float>
    {
        internal override bool IsNumberConverter => true;

        public override float Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => reader.GetSingle();

        public override void Write(Utf8JsonWriterImpl writer, float value, JsonSerializerOptionsImpl options) => writer.WriteNumberValue(value);

        internal override float ReadNumberWithCustomHandling(ref Utf8JsonReaderImpl reader, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                byte[] text = reader.ValueIsEscaped ? JsonReaderHelper.GetUnescaped(reader.ValueSpan) : reader.ValueSpan.ToArray();
                if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && JsonReaderHelper.TryGetFloatingPointConstant(text, out float constant))
                    return constant;
                if ((handling & JsonNumberHandling.AllowReadingFromString) != 0)
                {
                    if (JsonNumberParser.TryParse(text, out float parsed) && !float.IsInfinity(parsed)) return parsed;
                    JsonThrowHelper.ThrowFormatException(NumericType.Single);
                }
            }
            return reader.GetSingle();
        }

        internal override void WriteNumberWithCustomHandling(Utf8JsonWriterImpl writer, float value, JsonNumberHandling handling, JsonSerializerOptionsImpl options)
        {
            if ((handling & JsonNumberHandling.WriteAsString) != 0)
            {
                if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && !float.IsFinite(value)) writer.WriteStringValue(DoubleConverter.NamedLiteral(value));
                else
                {
                    Utf8JsonWriterImpl.ValidateFinite(value);
                    writer.WriteStringValue(value.ToString());
                }
            }
            else if ((handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && !float.IsFinite(value)) writer.WriteStringValue(DoubleConverter.NamedLiteral(value));
            else writer.WriteNumberValue(value);
        }

        public override float ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            byte[] text = reader.ValueIsEscaped ? JsonReaderHelper.GetUnescaped(reader.ValueSpan) : reader.ValueSpan.ToArray();
            if (JsonReaderHelper.TryGetFloatingPointConstant(text, out float constant)) return constant;
            if (JsonNumberParser.TryParse(text, out float parsed)) return parsed;
            JsonThrowHelper.ThrowFormatException(NumericType.Single);
            return 0;
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, float value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(float.IsFinite(value) ? value.ToString() : DoubleConverter.NamedLiteral(value));
    }

    internal sealed class StringConverter : JsonPrimitiveConverter<string>
    {
        public override string Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (options != null && reader.TokenType == JsonTokenType.Null) return null;
            return reader.GetString();
        }

        public override void Write(Utf8JsonWriterImpl writer, string value, JsonSerializerOptionsImpl options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }

        public override string ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => reader.GetString();

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, string value, JsonSerializerOptionsImpl options)
        {
            if (value == null) throw new ArgumentNullException("value");
            writer.WritePropertyName(value);
        }
    }

    internal sealed class CharConverter : JsonPrimitiveConverter<char>
    {
        public override char Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType != JsonTokenType.String && reader.TokenType != JsonTokenType.PropertyName)
                throw new InvalidOperationException(JsonSR.Format(JsonSR.InvalidCast, JsonThrowHelper.TokenName(reader.TokenType), "char"));
            string s = reader.GetString();
            if (s.Length != 1) throw new InvalidOperationException(JsonSR.Format(JsonSR.InvalidCast, JsonThrowHelper.TokenName(reader.TokenType), "char"));
            return s[0];
        }

        public override void Write(Utf8JsonWriterImpl writer, char value, JsonSerializerOptionsImpl options) => writer.WriteStringValue(value.ToString());

        public override char ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            Read(ref reader, typeToConvert, options);

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, char value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value.ToString());
    }

    internal sealed class DateTimeConverter : JsonPrimitiveConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType != JsonTokenType.String) JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(reader.TokenType);
            if (!reader.TryGetDateTime(out DateTime value)) JsonThrowHelper.ThrowFormatException(DataType.DateTime);
            return value;
        }

        public override void Write(Utf8JsonWriterImpl writer, DateTime value, JsonSerializerOptionsImpl options) => writer.WriteStringValue(value);

        public override DateTime ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (!reader.TryGetDateTime(out DateTime value)) JsonThrowHelper.ThrowFormatException(DataType.DateTime);
            return value;
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, DateTime value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value);
    }

    internal sealed class DateTimeOffsetConverter : JsonPrimitiveConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriterImpl writer, DateTimeOffset value, JsonSerializerOptionsImpl options) => writer.WriteStringValue(value);

        public override DateTimeOffset ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            reader.GetDateTimeOffsetNoValidation();

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, DateTimeOffset value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value);
    }

    internal sealed class GuidConverter : JsonPrimitiveConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => reader.GetGuid();

        public override void Write(Utf8JsonWriterImpl writer, Guid value, JsonSerializerOptionsImpl options) => writer.WriteStringValue(value);

        public override Guid ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (!JsonReaderHelper.TryGetValue(reader.ValueSpan, reader.ValueIsEscaped, out Guid value)) JsonThrowHelper.ThrowFormatException("Guid");
            return value;
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, Guid value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value);
    }

    internal sealed class TimeSpanConverter : JsonPrimitiveConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType != JsonTokenType.String) JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(reader.TokenType);
            return ParseCore(ref reader);
        }

        private static TimeSpan ParseCore(ref Utf8JsonReaderImpl reader)
        {
            // TimeSpanConverter: the constant ("c") format, invariant culture; anything else is a FormatException.
            string s = reader.GetString();
            if (s.Length < 8 || s.Length > 26 || (!char.IsDigit(s[0]) && s[0] != '-') || !char.IsDigit(s[s.Length - 1])
                || !JsonTimeSpanParser.TryParseConstant(s, out TimeSpan value))
                JsonThrowHelper.ThrowFormatException("TimeSpan");
            JsonTimeSpanParser.TryParseConstant(s, out TimeSpan result);
            return result;
        }

        public override void Write(Utf8JsonWriterImpl writer, TimeSpan value, JsonSerializerOptionsImpl options) => writer.WriteStringValue(value.ToString("c"));

        public override TimeSpan ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => ParseCore(ref reader);

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, TimeSpan value, JsonSerializerOptionsImpl options) =>
            writer.WritePropertyName(value.ToString("c"));
    }

    internal sealed class ByteArrayConverter : JsonConverterImpl<byte[]>
    {
        internal override bool IsInternalConverter => true;

        public override byte[] Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return reader.GetBytesFromBase64();
        }

        public override void Write(Utf8JsonWriterImpl writer, byte[] value, JsonSerializerOptionsImpl options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteBase64StringValue(value);
        }
    }

    internal sealed class JsonElementConverter : JsonConverterImpl<JsonElement>
    {
        internal override bool IsInternalConverter => true;

        public override JsonElement Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            (JsonElement)(object)JsonElementImpl.ParseValue(ref reader);

        public override void Write(Utf8JsonWriterImpl writer, JsonElement value, JsonSerializerOptionsImpl options) =>
            ((JsonElementImpl)(object)value).WriteTo(writer);

        internal override JsonElement ReadWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state) =>
            Read(ref reader, typeToConvert, options);
    }

    internal sealed class JsonDocumentConverter : JsonConverterImpl<JsonDocument>
    {
        internal override bool IsInternalConverter => true;

        public override bool HandleNull => true;

        public override JsonDocument Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            (JsonDocument)(object)JsonDocumentImpl.ParseValue(ref reader);

        public override void Write(Utf8JsonWriterImpl writer, JsonDocument value, JsonSerializerOptionsImpl options)
        {
            if (value == null) writer.WriteNullValue();
            else ((JsonDocumentImpl)(object)value).WriteTo(writer);
        }
    }

    /// <summary>object: the run-time type's metadata when writing; JsonElement (or JsonNode) when reading.</summary>
    internal sealed class ObjectConverter : JsonConverterImpl<object>
    {
        internal override bool IsInternalConverter => true;

        internal override bool IsNumberHandlingApplicable => true;

        public override object Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (options.UnknownTypeHandling == JsonUnknownTypeHandling.JsonElement)
                return (JsonElement)(object)JsonElementImpl.ParseValue(ref reader);
            return JsonNodeImpl.ParseValue(ref reader, options);
        }

        public override void Write(Utf8JsonWriterImpl writer, object value, JsonSerializerOptionsImpl options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStartObject();
            writer.WriteEndObject();
        }

        internal override object ReadWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state)
        {
            if (reader.TokenType == JsonTokenType.Null && options.UnknownTypeHandling == JsonUnknownTypeHandling.JsonElement) return null;
            return Read(ref reader, typeToConvert, options);
        }

        internal override void WriteWithState(Utf8JsonWriterImpl writer, object value, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (writer.CurrentDepth >= options.EffectiveMaxDepth)
                throw new JsonExceptionImpl(JsonSerializerHelpers.CycleDetected(options.EffectiveMaxDepth)) { AppendPathInformation = true };
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            Type runtimeType = value.GetType();
            if (runtimeType == typeof(object))
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
                return;
            }
            JsonTypeInfoImpl info = options.GetTypeInfoInternal(runtimeType);
            if (info == null)
            {
                // A run-time type no compile-time metadata covers: collections are written element by
                // element (as their metadata would), anything else can't be serialized.
                if (WriteUntypedCollection(writer, value, options, state)) return;
                options.ThrowNoMetadata(runtimeType);
            }
            info.EnsureConfigured();
            info.WriteAsObject(writer, value, state);
        }

        private bool WriteUntypedCollection(Utf8JsonWriterImpl writer, object value, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (value is IDictionary dictionary)
            {
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    string key;
                    if (entry.Key is string text)
                    {
                        key = options.DictionaryKeyPolicy != null ? options.DictionaryKeyPolicy.ConvertName(text) : text;
                    }
                    else if (entry.Key is DateTime date) key = JsonWriterHelper.FormatDateTime(date);
                    else if (entry.Key is DateTimeOffset dateOffset) key = JsonWriterHelper.FormatDateTimeOffset(dateOffset);
                    else if (entry.Key is bool flag) key = flag ? "true" : "false";
                    else key = entry.Key.ToString();
                    // Date keys are written unescaped, like their converters' WriteAsPropertyName.
                    if (entry.Key is DateTime || entry.Key is DateTimeOffset) writer.WritePropertyNameUtf8Escaped(Encoding.UTF8.GetBytes(key));
                    else writer.WritePropertyName(key);
                    state.Push(key);
                    WriteWithState(writer, entry.Value, options, state);
                    state.Pop();
                }
                writer.WriteEndObject();
                return true;
            }
            if (value is IEnumerable enumerable && !(value is string))
            {
                writer.WriteStartArray();
                foreach (object item in enumerable) WriteWithState(writer, item, options, state);
                writer.WriteEndArray();
                return true;
            }
            return false;
        }
    }

    internal sealed class UnsupportedTypeConverter<T> : JsonConverterImpl<T>
    {
        internal override bool IsInternalConverter => true;

        private NotSupportedException Unsupported() =>
            new NotSupportedException("Serialization and deserialization of '" + typeof(T).ToString() + "' instances is not supported.");

        public override T Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => throw Unsupported();

        public override void Write(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options) => throw Unsupported();
    }

    /// <summary>A shape csharpc's serializer doesn't implement yet: an explicit NotSupportedException when used.</summary>
    internal sealed class CsharpcUnsupportedConverter<T> : JsonConverterImpl<T>
    {
        private readonly string _what;
        public CsharpcUnsupportedConverter(string what) { _what = what; }
        internal override bool IsInternalConverter => true;
        private NotSupportedException Unsupported() =>
            new NotSupportedException("Serializing '" + typeof(T).ToString() + "' as " + _what + " is not supported by csharpc yet.");
        public override T Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) => throw Unsupported();
        public override void Write(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options) => throw Unsupported();
    }

    /// <summary>A converter for a type assignable to T, used where a JsonConverter&lt;T&gt; is required.</summary>
    internal sealed class CastingConverter<T> : JsonConverterImpl<T>
    {
        private readonly JsonConverterImpl _source;

        public CastingConverter(JsonConverterImpl source) { _source = source; }

        internal override bool IsInternalConverter => _source.IsInternalConverter;

        public override T Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            (T)_source.ReadAsObjectWithState(ref reader, typeToConvert, options, new JsonReadState(options));

        public override void Write(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options) =>
            _source.WriteAsObjectWithState(writer, value, options, new JsonWriteState(options));
    }

    internal sealed class NullableConverter<T> : JsonConverterImpl<T?> where T : struct
    {
        private readonly JsonTypeInfoImpl<T> _elementInfo;
        private JsonConverterImpl<T> _elementConverter;
        private readonly JsonSerializerOptionsImpl _options;

        public NullableConverter(JsonTypeInfoImpl<T> elementInfo, JsonSerializerOptionsImpl options)
        {
            _elementInfo = elementInfo;
            _options = options;
        }

        internal override bool IsInternalConverter => true;

        private JsonConverterImpl<T> Element
        {
            get
            {
                if (_elementConverter == null)
                {
                    JsonTypeInfoImpl<T> info = _elementInfo ?? (JsonTypeInfoImpl<T>)_options.GetTypeInfoForRuntimeType(typeof(T));
                    info.EnsureConfigured();
                    _elementConverter = info.TypedConverter;
                }
                return _elementConverter;
            }
        }

        internal override bool IsNumberConverter => Element.IsNumberConverter;

        internal override bool IsNumberHandlingApplicable => Element.IsNumberHandlingApplicable;

        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return Element.Read(ref reader, typeof(T), options);
        }

        public override void Write(Utf8JsonWriterImpl writer, T? value, JsonSerializerOptionsImpl options)
        {
            if (!value.HasValue) writer.WriteNullValue();
            else Element.Write(writer, value.Value, options);
        }

        internal override T? ReadWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return Element.ReadWithState(ref reader, typeof(T), options, state);
        }

        internal override void WriteWithState(Utf8JsonWriterImpl writer, T? value, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (!value.HasValue) writer.WriteNullValue();
            else Element.WriteWithState(writer, value.Value, options, state);
        }

        public override T? ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            Element.ReadAsPropertyName(ref reader, typeof(T), options);

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, T? value, JsonSerializerOptionsImpl options) =>
            Element.WriteAsPropertyName(writer, value.Value, options);
    }

    // ------------------------------------------------------------------------------------------
    // Enums
    // ------------------------------------------------------------------------------------------

    internal enum EnumConverterOptions
    {
        AllowStrings = 1,
        AllowNumbers = 2,
    }

    internal sealed class EnumConverter<T> : JsonPrimitiveConverter<T> where T : struct, Enum
    {
        private const int KindDefault = 0, KindNamingPolicy = 1, KindAttribute = 2;

        private sealed class EnumFieldInfo
        {
            public ulong Key;
            public int Kind;
            public string OriginalName;
            public string JsonName;
            public List<EnumFieldInfo> Conflicting;

            public EnumFieldInfo GetMatchingField(string input)
            {
                if (Kind == KindDefault || input == JsonName) return this;
                if (Conflicting != null)
                    foreach (EnumFieldInfo f in Conflicting)
                        if (f.Kind == KindDefault || input == f.JsonName) return f;
                return null;
            }

            public void AppendConflictingField(EnumFieldInfo other)
            {
                if (ConflictsWith(this, other)) return;
                Conflicting ??= new List<EnumFieldInfo>();
                foreach (EnumFieldInfo f in Conflicting) if (ConflictsWith(f, other)) return;
                Conflicting.Add(other);
            }

            private static bool ConflictsWith(EnumFieldInfo current, EnumFieldInfo other)
            {
                if (current.Kind == KindDefault) return true;
                if (other.Kind == KindDefault) return false;
                return current.JsonName == other.JsonName;
            }
        }

        private readonly EnumConverterOptions _converterOptions;
        private readonly JsonNamingPolicyImpl _namingPolicy;
        private readonly EnumFieldInfo[] _fields;
        private readonly Dictionary<string, EnumFieldInfo> _index;
        private readonly Dictionary<ulong, string> _namesForWriting = new Dictionary<ulong, string>();
        private readonly Dictionary<string, ulong> _namesForReading = new Dictionary<string, ulong>();
        private readonly bool _isFlags;
        private readonly bool _isSigned;

        public EnumConverter(EnumConverterOptions converterOptions, JsonNamingPolicyImpl namingPolicy, JsonSerializerOptionsImpl options)
        {
            _converterOptions = converterOptions;
            _namingPolicy = namingPolicy;
            _isFlags = CoreIntrinsics.IsFlagsEnum<T>();
            _isSigned = CoreIntrinsics.IsSignedEnum<T>();
            _fields = ResolveEnumFields(namingPolicy);
            _index = new Dictionary<string, EnumFieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (EnumFieldInfo field in _fields)
            {
                AddToIndex(field);
                _namesForWriting.TryAdd(field.Key, field.JsonName);
                _namesForReading.TryAdd(field.JsonName, field.Key);
            }
            if (namingPolicy != null)
            {
                foreach (EnumFieldInfo field in _fields)
                    if (field.Kind == KindNamingPolicy)
                        AddToIndex(new EnumFieldInfo { Key = field.Key, Kind = KindDefault, OriginalName = field.OriginalName, JsonName = field.OriginalName });
            }
        }

        private void AddToIndex(EnumFieldInfo field)
        {
            if (!_index.TryAdd(field.JsonName, field)) _index[field.JsonName].AppendConflictingField(field);
        }

        private EnumFieldInfo[] ResolveEnumFields(JsonNamingPolicyImpl namingPolicy)
        {
            string[] names = Enum.GetNames<T>();
            T[] values = Enum.GetValues<T>();
            string[] attributeNames = CoreIntrinsics.EnumMemberJsonNames<T>();
            EnumFieldInfo[] fields = new EnumFieldInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string originalName = names[i];
                int kind;
                if (attributeNames[i] != null)
                {
                    originalName = attributeNames[i];
                    kind = KindAttribute;
                }
                else kind = namingPolicy != null ? KindNamingPolicy : KindDefault;
                fields[i] = new EnumFieldInfo
                {
                    Key = CoreIntrinsics.EnumToUInt64(values[i]),
                    Kind = kind,
                    OriginalName = originalName,
                    JsonName = ResolveAndValidateJsonName(originalName, namingPolicy, kind),
                };
            }
            if (_isFlags && fields.Length > 1)
            {
                // Supersets first (more bits set), stable otherwise: proper flags parsing and formatting.
                EnumFieldInfo[] sorted = new EnumFieldInfo[fields.Length];
                int[] counts = new int[fields.Length];
                for (int i = 0; i < fields.Length; i++) counts[i] = PopCount(fields[i].Key);
                bool[] used = new bool[fields.Length];
                for (int k = 0; k < fields.Length; k++)
                {
                    int best = -1;
                    for (int i = 0; i < fields.Length; i++)
                        if (!used[i] && (best < 0 || counts[i] > counts[best])) best = i;
                    used[best] = true;
                    sorted[k] = fields[best];
                }
                fields = sorted;
            }
            return fields;
        }

        private static int PopCount(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        private string ResolveAndValidateJsonName(string name, JsonNamingPolicyImpl namingPolicy, int kind)
        {
            if (kind != KindAttribute && namingPolicy != null) name = namingPolicy.ConvertName(name);
            if (name == null || (name.Length > 0 && (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[name.Length - 1])))
                || (_isFlags && (name.Length == 0 || name.Contains(','))))
                throw new InvalidOperationException("Enum type '" + typeof(T).Name + "' uses unsupported identifier '" + name + "'. It must not be null nor contain leading or trailing whitespace. Flags enums must additionally not be empty nor contain commas.");
            return name;
        }

        public override T Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.String && (_converterOptions & EnumConverterOptions.AllowStrings) != 0)
            {
                if (TryParseEnumFromString(ref reader, out T result)) return result;
            }
            else if (reader.TokenType == JsonTokenType.Number && (_converterOptions & EnumConverterOptions.AllowNumbers) != 0)
            {
                if (_isSigned)
                {
                    if (reader.TryGetInt64(out long l) && FitsSigned(l)) return CoreIntrinsics.EnumFromUInt64<T>((ulong)l);
                }
                else if (reader.TryGetUInt64(out ulong u) && FitsUnsigned(u)) return CoreIntrinsics.EnumFromUInt64<T>(u);
            }
            throw JsonSerializerHelpers.DefaultJsonException();
        }

        // The reader's TryGet for the underlying type: values out of its range don't convert.
        private static bool FitsSigned(long value)
        {
            T roundTrip = CoreIntrinsics.EnumFromUInt64<T>((ulong)value);
            return (long)CoreIntrinsics.EnumToUInt64(roundTrip) == value;
        }

        private static bool FitsUnsigned(ulong value) => CoreIntrinsics.EnumToUInt64(CoreIntrinsics.EnumFromUInt64<T>(value)) == value;

        public override void Write(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options)
        {
            if ((_converterOptions & EnumConverterOptions.AllowStrings) != 0)
            {
                ulong key = CoreIntrinsics.EnumToUInt64(value);
                if (_namesForWriting.TryGetValue(key, out string name))
                {
                    writer.WriteStringValue(name);
                    return;
                }
                if (IsDefinedValueOrCombinationOfValues(key))
                {
                    string formatted = FormatEnumAsString(key, null);
                    _namesForWriting.TryAdd(key, formatted);
                    writer.WriteStringValue(formatted);
                    return;
                }
            }
            if ((_converterOptions & EnumConverterOptions.AllowNumbers) == 0) throw JsonSerializerHelpers.DefaultJsonException();
            ulong raw = CoreIntrinsics.EnumToUInt64(value);
            if (_isSigned) writer.WriteNumberValue((long)raw);
            else writer.WriteNumberValue(raw);
        }

        public override T ReadAsPropertyName(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (!TryParseEnumFromString(ref reader, out T result)) throw JsonSerializerHelpers.DefaultJsonException();
            return result;
        }

        public override void WriteAsPropertyName(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options)
        {
            JsonNamingPolicyImpl dictionaryKeyPolicy = options.DictionaryKeyPolicy != null && options.DictionaryKeyPolicy != _namingPolicy ? options.DictionaryKeyPolicy : null;
            ulong key = CoreIntrinsics.EnumToUInt64(value);
            if (dictionaryKeyPolicy == null && _namesForWriting.TryGetValue(key, out string name))
            {
                writer.WritePropertyName(name);
                return;
            }
            if (IsDefinedValueOrCombinationOfValues(key))
            {
                writer.WritePropertyName(FormatEnumAsString(key, dictionaryKeyPolicy));
                return;
            }
            if (_isSigned) writer.WritePropertyName(((long)key).ToString());
            else writer.WritePropertyName(key.ToString());
        }

        private bool TryParseEnumFromString(ref Utf8JsonReaderImpl reader, out T result)
        {
            string source = reader.GetString().Trim();
            if (_namesForReading.TryGetValue(source, out ulong key))
            {
                result = CoreIntrinsics.EnumFromUInt64<T>(key);
                return true;
            }
            if (JsonSerializerHelpers.IsIntegerString(source))
            {
                if ((_converterOptions & EnumConverterOptions.AllowNumbers) != 0) return Enum.TryParse<T>(source, out result);
                result = default(T);
                return false;
            }
            return TryParseNamedEnum(source, out result);
        }

        private bool TryParseNamedEnum(string source, out T result)
        {
            ulong key = 0;
            string rest = source;
            do
            {
                string next;
                int i = rest.IndexOf(',');
                if (i == -1)
                {
                    next = rest;
                    rest = "";
                }
                else
                {
                    next = rest.Substring(0, i).TrimEnd();
                    rest = rest.Substring(i + 1).TrimStart();
                }
                if (_index.TryGetValue(next, out EnumFieldInfo first) && first.GetMatchingField(next) is EnumFieldInfo match)
                {
                    key |= match.Key;
                    continue;
                }
                result = default(T);
                return false;
            } while (rest.Length > 0);
            result = CoreIntrinsics.EnumFromUInt64<T>(key);
            return true;
        }

        private string FormatEnumAsString(ulong key, JsonNamingPolicyImpl dictionaryKeyPolicy)
        {
            if (_isFlags)
            {
                StringBuilder sb = new StringBuilder();
                ulong remainingBits = key;
                foreach (EnumFieldInfo field in _fields)
                {
                    ulong fieldKey = field.Key;
                    if (fieldKey == 0 ? key == 0 : (remainingBits & fieldKey) == fieldKey)
                    {
                        remainingBits &= ~fieldKey;
                        string name = dictionaryKeyPolicy != null ? ResolveAndValidateJsonName(field.OriginalName, dictionaryKeyPolicy, field.Kind) : field.JsonName;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(name);
                        if (remainingBits == 0) break;
                    }
                }
                return sb.ToString();
            }
            foreach (EnumFieldInfo field in _fields)
                if (field.Key == key) return ResolveAndValidateJsonName(field.OriginalName, dictionaryKeyPolicy, field.Kind);
            return null;
        }

        private bool IsDefinedValueOrCombinationOfValues(ulong key)
        {
            if (_isFlags)
            {
                ulong remainingBits = key;
                foreach (EnumFieldInfo field in _fields)
                {
                    ulong fieldKey = field.Key;
                    if (fieldKey == 0 ? key == 0 : (remainingBits & fieldKey) == fieldKey)
                    {
                        remainingBits &= ~fieldKey;
                        if (remainingBits == 0) return true;
                    }
                }
                return false;
            }
            foreach (EnumFieldInfo field in _fields)
                if (field.Key == key) return true;
            return false;
        }
    }

    [Shadows("System.Text.Json.Serialization.JsonStringEnumConverter")]
    public class JsonStringEnumConverterImpl : JsonConverterFactoryImpl
    {
        private readonly JsonNamingPolicyImpl _namingPolicy;
        private readonly EnumConverterOptions _converterOptions;

        public JsonStringEnumConverterImpl() : this(null, true) { }

        public JsonStringEnumConverterImpl(JsonNamingPolicyImpl namingPolicy = null, bool allowIntegerValues = true)
        {
            _namingPolicy = namingPolicy;
            _converterOptions = allowIntegerValues ? EnumConverterOptions.AllowNumbers | EnumConverterOptions.AllowStrings : EnumConverterOptions.AllowStrings;
        }

        public sealed override bool CanConvert(Type typeToConvert) => JsonBuiltInTypeInfos.IsEnum(typeToConvert);

        public sealed override JsonConverterImpl CreateConverter(Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            JsonConverterImpl converter = JsonBuiltInTypeInfos.CreateEnumConverter(typeToConvert, _converterOptions, _namingPolicy, options);
            if (converter == null)
                throw new NotSupportedException("csharpc: JsonStringEnumConverter for enum type '" + typeToConvert.ToString() + "' that the program never serializes statically is not supported yet.");
            return converter;
        }
    }

    [Shadows("System.Text.Json.Serialization.JsonStringEnumConverter`1")]
    public class JsonStringEnumConverterImpl<TEnum> : JsonConverterFactoryImpl where TEnum : struct, Enum
    {
        private readonly JsonNamingPolicyImpl _namingPolicy;
        private readonly EnumConverterOptions _converterOptions;

        public JsonStringEnumConverterImpl() : this(null, true) { }

        public JsonStringEnumConverterImpl(JsonNamingPolicyImpl namingPolicy = null, bool allowIntegerValues = true)
        {
            _namingPolicy = namingPolicy;
            _converterOptions = allowIntegerValues ? EnumConverterOptions.AllowNumbers | EnumConverterOptions.AllowStrings : EnumConverterOptions.AllowStrings;
        }

        public sealed override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(TEnum);

        public sealed override JsonConverterImpl CreateConverter(Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (typeToConvert != typeof(TEnum))
                throw new ArgumentOutOfRangeException("typeToConvert", "The converter does not support type '" + typeToConvert.ToString() + "'.");
            return new EnumConverter<TEnum>(_converterOptions, _namingPolicy, options);
        }
    }

    [Shadows("System.Text.Json.Serialization.JsonNumberEnumConverter`1")]
    public sealed class JsonNumberEnumConverterImpl<TEnum> : JsonConverterFactoryImpl where TEnum : struct, Enum
    {
        public JsonNumberEnumConverterImpl() { }

        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(TEnum);

        public override JsonConverterImpl CreateConverter(Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (typeToConvert != typeof(TEnum))
                throw new ArgumentOutOfRangeException("typeToConvert", "The converter does not support type '" + typeToConvert.ToString() + "'.");
            return new EnumConverter<TEnum>(EnumConverterOptions.AllowNumbers, null, options);
        }
    }
}
