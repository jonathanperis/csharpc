// System.Text.Json.JsonSerializer: the entry points, and the root-level exception handling that adds
// "Path: $.x | LineNumber: n | BytePositionInLine: m" to exceptions like .NET's ReadCore/WriteCore.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Serialization
// (release/11.0): JsonSerializer.Read.*.cs, JsonSerializer.Write.*.cs, ThrowHelper.Serialization.cs.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.JsonSerializer")]
    public static class JsonSerializerImpl
    {
        public static bool IsReflectionEnabledByDefault => JsonReflection.IsReflectionEnabledByDefault;

        // ---------------------------------------------------------------- metadata lookup

        private static JsonTypeInfoImpl GetTypeInfo(JsonSerializerOptionsImpl options, Type type)
        {
            options ??= JsonSerializerOptionsImpl.Default;
            options.ConfigureForJsonSerializer();
            JsonTypeInfoImpl info = options.GetTypeInfoInternal(type);
            if (info == null) options.ThrowNoMetadata(type);
            info.EnsureConfigured();
            return info;
        }

        private static JsonTypeInfoImpl<T> GetTypeInfo<T>(JsonSerializerOptionsImpl options) => (JsonTypeInfoImpl<T>)GetTypeInfo(options, typeof(T));

        private static JsonTypeInfoImpl GetTypeInfo(JsonSerializerContextImpl context, Type type)
        {
            if (context == null) throw new ArgumentNullException("context");
            JsonTypeInfoImpl info = context.GetTypeInfo(type);
            // JsonSerializer.GetTypeInfo(JsonSerializerContext, Type): InvalidOperationException, unlike the options path.
            if (info == null) throw new InvalidOperationException(JsonSerializerOptionsImpl.NoMetadataMessage(type, context));
            info.EnsureConfigured();
            return info;
        }

        private static JsonTypeInfoImpl Checked(JsonTypeInfoImpl jsonTypeInfo)
        {
            if (jsonTypeInfo == null) throw new ArgumentNullException("jsonTypeInfo");
            jsonTypeInfo.EnsureConfigured();
            return jsonTypeInfo;
        }

        private static void ValidateInputType(object value, Type inputType)
        {
            if (inputType == null) throw new ArgumentNullException("inputType");
            if (value != null && value.GetType() != inputType && !JsonSerializerTypeChecks.IsAssignable(value, inputType))
                throw new ArgumentException("The specified type " + inputType.ToString() + " must derive from the specific value's type " + value.GetType().ToString() + ".");
        }

        // ---------------------------------------------------------------- core

        internal static void WriteCore(Utf8JsonWriterImpl writer, object value, JsonTypeInfoImpl info)
        {
            JsonSerializerOptionsImpl options = info._options;
            JsonWriteState state = new JsonWriteState(options);
            if (options.ReferenceHandler != null)
            {
                if (options.ReferenceHandler.HandlingStrategy == 2) state.Ancestors = new List<object>();
                else state.ReferenceResolver = options.ReferenceHandler.CreateResolver(true);
            }
            try
            {
                info.WriteAsObject(writer, value, state);
            }
            catch (JsonExceptionImpl ex)
            {
                if (ex.Path != null) throw ex;
                string path = state.PropertyPath();
                ex.Path = path;
                string message = ex._message;
                if (string.IsNullOrEmpty(message))
                {
                    message = "The object or value could not be serialized.";
                    ex.AppendPathInformation = true;
                }
                if (ex.AppendPathInformation)
                {
                    message += " Path: " + path + ".";
                    ex.SetMessage(message);
                }
                throw ex;
            }
            catch (NotSupportedException ex)
            {
                if (ex.Message.Contains(" Path: ")) throw ex;
                throw new NotSupportedException(ex.Message + " Path: " + state.PropertyPath() + ".", ex);
            }
            writer.Flush();
        }

        /// <summary>
        /// Reads one value; with <paramref name="wholePayload"/> also the first token and the trailing
        /// whitespace (JsonConverter.ReadCore), so reader errors there get the "$" path too.
        /// </summary>
        internal static object ReadCore(ref Utf8JsonReaderImpl reader, JsonTypeInfoImpl info, bool wholePayload = false)
        {
            JsonReadState state = new JsonReadState(info._options);
            state.CurrentValueType = info.Type;
            if (info._options.ReferenceHandler != null && info._options.ReferenceHandler.HandlingStrategy == 1)
                state.References = new Dictionary<string, object>();
            try
            {
                if (wholePayload) reader.Read();
                object result = info.ReadAsObject(ref reader, state);
                if (wholePayload) reader.Read();
                return result;
            }
            catch (JsonExceptionImpl ex)
            {
                if (ex is JsonReaderExceptionImpl)
                {
                    // ThrowHelper.ReThrowWithPath: insert the path before the reader's position.
                    string path = state.JsonPath();
                    string message = ex.Message;
                    int iPos = message.LastIndexOf(" LineNumber: ", StringComparison.Ordinal);
                    if (iPos >= 0) message = message.Substring(0, iPos) + " Path: " + path + " |" + message.Substring(iPos);
                    else message += " Path: " + path + ".";
                    throw new JsonExceptionImpl(message, path, ex.LineNumber, ex.BytePositionInLine, ex);
                }
                if (ex.Path == null) AddExceptionInformation(ref reader, state, ex);
                throw ex;
            }
            catch (InvalidOperationException ex)
            {
                if (!JsonSerializerTypeChecks.IsRethrowable(ex)) throw ex;
                JsonExceptionImpl wrapped = new JsonExceptionImpl(null, ex);
                AddExceptionInformation(ref reader, state, wrapped);
                throw wrapped;
            }
            catch (FormatException ex)
            {
                if (!JsonSerializerTypeChecks.IsRethrowable(ex)) throw ex;
                JsonExceptionImpl wrapped = new JsonExceptionImpl(null, ex);
                AddExceptionInformation(ref reader, state, wrapped);
                throw wrapped;
            }
            catch (NotSupportedException ex)
            {
                if (ex.Message.Contains(" Path: ")) throw ex;
                JsonReaderStateImpl readerState = reader.CurrentState;
                throw new NotSupportedException(ex.Message + " Path: " + state.JsonPath() + " | LineNumber: " + readerState._lineNumber.ToString()
                    + " | BytePositionInLine: " + readerState._bytePositionInLine.ToString() + ".", ex);
            }
        }

        private static void AddExceptionInformation(ref Utf8JsonReaderImpl reader, JsonReadState state, JsonExceptionImpl ex)
        {
            JsonReaderStateImpl readerState = reader.CurrentState;
            long lineNumber = readerState._lineNumber;
            long bytePositionInLine = readerState._bytePositionInLine;
            ex.LineNumber = lineNumber;
            ex.BytePositionInLine = bytePositionInLine;
            string path = state.JsonPath();
            ex.Path = path;
            string message = ex._message;
            if (string.IsNullOrEmpty(message))
            {
                Type type = state.CurrentValueType ?? typeof(object);
                message = JsonSR.Format(JsonSR.DeserializeUnableToConvertValue, type.ToString());
                ex.AppendPathInformation = true;
            }
            if (ex.AppendPathInformation)
            {
                message += " Path: " + path + " | LineNumber: " + lineNumber.ToString() + " | BytePositionInLine: " + bytePositionInLine.ToString() + ".";
                ex.SetMessage(message);
            }
        }

        /// <summary>Reads exactly one value from a complete payload (ReadFromSpan): no trailing tokens.</summary>
        private static object ReadFromBytes(byte[] utf8, JsonTypeInfoImpl info)
        {
            JsonReaderOptionsImpl readerOptions = info._options.GetReaderOptions();
            Utf8JsonReaderImpl reader = new Utf8JsonReaderImpl(utf8, readerOptions);
            return ReadCore(ref reader, info, wholePayload: true);
        }

        private static byte[] Utf8(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            byte[] utf8 = JsonUtf8.FromUtf16(json, out int bad);
            if (utf8 == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            return utf8;
        }

        private static string ToJsonString(object value, JsonTypeInfoImpl info)
        {
            Utf8JsonWriterImpl writer = new Utf8JsonWriterImpl(info._options.GetWriterOptions());
            WriteCore(writer, value, info);
            return writer.WrittenString();
        }

        private static byte[] ToUtf8Bytes(object value, JsonTypeInfoImpl info)
        {
            Utf8JsonWriterImpl writer = new Utf8JsonWriterImpl(info._options.GetWriterOptions());
            WriteCore(writer, value, info);
            return writer.WrittenBytes();
        }

        // ---------------------------------------------------------------- Serialize

        public static string Serialize<TValue>(TValue value, JsonSerializerOptionsImpl options = null) => ToJsonString(value, GetTypeInfo<TValue>(options));

        public static string Serialize<TValue>(TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo) => ToJsonString(value, Checked(jsonTypeInfo));

        public static string Serialize(object value, JsonTypeInfoImpl jsonTypeInfo) => ToJsonString(value, Checked(jsonTypeInfo));

        public static string Serialize(object value, Type inputType, JsonSerializerOptionsImpl options = null)
        {
            ValidateInputType(value, inputType);
            return ToJsonString(value, GetTypeInfo(options, inputType));
        }

        public static string Serialize(object value, Type inputType, JsonSerializerContextImpl context)
        {
            ValidateInputType(value, inputType);
            return ToJsonString(value, GetTypeInfo(context, inputType));
        }

        public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, JsonSerializerOptionsImpl options = null) => ToUtf8Bytes(value, GetTypeInfo<TValue>(options));

        public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo) => ToUtf8Bytes(value, Checked(jsonTypeInfo));

        public static byte[] SerializeToUtf8Bytes(object value, JsonTypeInfoImpl jsonTypeInfo) => ToUtf8Bytes(value, Checked(jsonTypeInfo));

        public static byte[] SerializeToUtf8Bytes(object value, Type inputType, JsonSerializerOptionsImpl options = null)
        {
            ValidateInputType(value, inputType);
            return ToUtf8Bytes(value, GetTypeInfo(options, inputType));
        }

        public static byte[] SerializeToUtf8Bytes(object value, Type inputType, JsonSerializerContextImpl context)
        {
            ValidateInputType(value, inputType);
            return ToUtf8Bytes(value, GetTypeInfo(context, inputType));
        }

        public static void Serialize<TValue>(Utf8JsonWriterImpl writer, TValue value, JsonSerializerOptionsImpl options = null)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            WriteCore(writer, value, GetTypeInfo<TValue>(options));
        }

        public static void Serialize<TValue>(Utf8JsonWriterImpl writer, TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            WriteCore(writer, value, Checked(jsonTypeInfo));
        }

        public static void Serialize(Utf8JsonWriterImpl writer, object value, JsonTypeInfoImpl jsonTypeInfo)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            WriteCore(writer, value, Checked(jsonTypeInfo));
        }

        public static void Serialize(Utf8JsonWriterImpl writer, object value, Type inputType, JsonSerializerOptionsImpl options = null)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            ValidateInputType(value, inputType);
            WriteCore(writer, value, GetTypeInfo(options, inputType));
        }

        public static void Serialize(Utf8JsonWriterImpl writer, object value, Type inputType, JsonSerializerContextImpl context)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            ValidateInputType(value, inputType);
            WriteCore(writer, value, GetTypeInfo(context, inputType));
        }

        private static void WriteToStream(Stream utf8Json, object value, JsonTypeInfoImpl info)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            byte[] bytes = ToUtf8Bytes(value, info);
            utf8Json.Write(bytes, 0, bytes.Length);
        }

        public static void Serialize<TValue>(Stream utf8Json, TValue value, JsonSerializerOptionsImpl options = null) => WriteToStream(utf8Json, value, GetTypeInfo<TValue>(options));

        public static void Serialize<TValue>(Stream utf8Json, TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo) => WriteToStream(utf8Json, value, Checked(jsonTypeInfo));

        public static void Serialize(Stream utf8Json, object value, JsonTypeInfoImpl jsonTypeInfo) => WriteToStream(utf8Json, value, Checked(jsonTypeInfo));

        public static void Serialize(Stream utf8Json, object value, Type inputType, JsonSerializerOptionsImpl options = null)
        {
            ValidateInputType(value, inputType);
            WriteToStream(utf8Json, value, GetTypeInfo(options, inputType));
        }

        // The async APIs complete synchronously (the payload is buffered), but like .NET's async methods
        // they report failures through the returned task rather than by throwing.
        public static Task SerializeAsync<TValue>(Stream utf8Json, TValue value, JsonSerializerOptionsImpl options = null, CancellationToken cancellationToken = default)
        {
            try
            {
                WriteToStream(utf8Json, value, GetTypeInfo<TValue>(options));
                return Task.CompletedTask;
            }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        public static Task SerializeAsync<TValue>(Stream utf8Json, TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo, CancellationToken cancellationToken = default)
        {
            try
            {
                WriteToStream(utf8Json, value, Checked(jsonTypeInfo));
                return Task.CompletedTask;
            }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        public static Task SerializeAsync(Stream utf8Json, object value, Type inputType, JsonSerializerOptionsImpl options = null, CancellationToken cancellationToken = default)
        {
            try
            {
                ValidateInputType(value, inputType);
                WriteToStream(utf8Json, value, GetTypeInfo(options, inputType));
                return Task.CompletedTask;
            }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        public static JsonDocumentImpl SerializeToDocument<TValue>(TValue value, JsonSerializerOptionsImpl options = null)
        {
            JsonTypeInfoImpl info = GetTypeInfo<TValue>(options);
            return JsonDocumentImpl.Parse(ToUtf8Bytes(value, info), new JsonReaderOptionsImpl(), true);
        }

        public static JsonDocumentImpl SerializeToDocument<TValue>(TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo) =>
            JsonDocumentImpl.Parse(ToUtf8Bytes(value, Checked(jsonTypeInfo)), new JsonReaderOptionsImpl(), true);

        public static JsonElementImpl SerializeToElement<TValue>(TValue value, JsonSerializerOptionsImpl options = null)
        {
            JsonTypeInfoImpl info = GetTypeInfo<TValue>(options);
            return JsonDocumentImpl.Parse(ToUtf8Bytes(value, info), new JsonReaderOptionsImpl(), true, isDisposable: false).RootElement;
        }

        public static JsonElementImpl SerializeToElement<TValue>(TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo) =>
            JsonDocumentImpl.Parse(ToUtf8Bytes(value, Checked(jsonTypeInfo)), new JsonReaderOptionsImpl(), true, isDisposable: false).RootElement;

        public static JsonNodeImpl SerializeToNode<TValue>(TValue value, JsonSerializerOptionsImpl options = null)
        {
            JsonTypeInfoImpl info = GetTypeInfo<TValue>(options);
            return JsonNodeImpl.Parse(ToUtf8Bytes(value, info), null);
        }

        public static JsonNodeImpl SerializeToNode<TValue>(TValue value, JsonTypeInfoImpl<TValue> jsonTypeInfo) =>
            JsonNodeImpl.Parse(ToUtf8Bytes(value, Checked(jsonTypeInfo)), null);

        // ---------------------------------------------------------------- Deserialize

        public static TValue Deserialize<TValue>(string json, JsonSerializerOptionsImpl options = null)
        {
            byte[] utf8 = Utf8(json);
            return (TValue)ReadFromBytes(utf8, GetTypeInfo<TValue>(options));
        }

        public static TValue Deserialize<TValue>(string json, JsonTypeInfoImpl<TValue> jsonTypeInfo)
        {
            byte[] utf8 = Utf8(json);
            return (TValue)ReadFromBytes(utf8, Checked(jsonTypeInfo));
        }

        public static object Deserialize(string json, JsonTypeInfoImpl jsonTypeInfo)
        {
            byte[] utf8 = Utf8(json);
            return ReadFromBytes(utf8, Checked(jsonTypeInfo));
        }

        public static object Deserialize(string json, Type returnType, JsonSerializerOptionsImpl options = null)
        {
            if (returnType == null) throw new ArgumentNullException("returnType");
            byte[] utf8 = Utf8(json);
            return ReadFromBytes(utf8, GetTypeInfo(options, returnType));
        }

        public static object Deserialize(string json, Type returnType, JsonSerializerContextImpl context)
        {
            if (returnType == null) throw new ArgumentNullException("returnType");
            byte[] utf8 = Utf8(json);
            return ReadFromBytes(utf8, GetTypeInfo(context, returnType));
        }

        public static TValue Deserialize<TValue>(ReadOnlySpan<char> json, JsonSerializerOptionsImpl options = null) => Deserialize<TValue>(new string(json), options);

        public static TValue Deserialize<TValue>(ReadOnlySpan<char> json, JsonTypeInfoImpl<TValue> jsonTypeInfo) => Deserialize<TValue>(new string(json), jsonTypeInfo);

        public static TValue Deserialize<TValue>(ReadOnlySpan<byte> utf8Json, JsonSerializerOptionsImpl options = null) =>
            (TValue)ReadFromBytes(utf8Json.ToArray(), GetTypeInfo<TValue>(options));

        public static TValue Deserialize<TValue>(ReadOnlySpan<byte> utf8Json, JsonTypeInfoImpl<TValue> jsonTypeInfo) =>
            (TValue)ReadFromBytes(utf8Json.ToArray(), Checked(jsonTypeInfo));

        public static object Deserialize(ReadOnlySpan<byte> utf8Json, Type returnType, JsonSerializerOptionsImpl options = null)
        {
            if (returnType == null) throw new ArgumentNullException("returnType");
            return ReadFromBytes(utf8Json.ToArray(), GetTypeInfo(options, returnType));
        }

        public static TValue Deserialize<TValue>(Stream utf8Json, JsonSerializerOptionsImpl options = null)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            return (TValue)ReadFromBytes(JsonDocumentImpl.ReadToEnd(utf8Json), GetTypeInfo<TValue>(options));
        }

        public static TValue Deserialize<TValue>(Stream utf8Json, JsonTypeInfoImpl<TValue> jsonTypeInfo)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            return (TValue)ReadFromBytes(JsonDocumentImpl.ReadToEnd(utf8Json), Checked(jsonTypeInfo));
        }

        public static object Deserialize(Stream utf8Json, Type returnType, JsonSerializerOptionsImpl options = null)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            if (returnType == null) throw new ArgumentNullException("returnType");
            return ReadFromBytes(JsonDocumentImpl.ReadToEnd(utf8Json), GetTypeInfo(options, returnType));
        }

        public static ValueTask<TValue> DeserializeAsync<TValue>(Stream utf8Json, JsonSerializerOptionsImpl options = null, CancellationToken cancellationToken = default)
        {
            try { return ValueTask.FromResult(Deserialize<TValue>(utf8Json, options)); }
            catch (Exception ex) { return ValueTask.FromException<TValue>(ex); }
        }

        public static ValueTask<TValue> DeserializeAsync<TValue>(Stream utf8Json, JsonTypeInfoImpl<TValue> jsonTypeInfo, CancellationToken cancellationToken = default)
        {
            try { return ValueTask.FromResult(Deserialize<TValue>(utf8Json, jsonTypeInfo)); }
            catch (Exception ex) { return ValueTask.FromException<TValue>(ex); }
        }

        public static ValueTask<object> DeserializeAsync(Stream utf8Json, Type returnType, JsonSerializerOptionsImpl options = null, CancellationToken cancellationToken = default)
        {
            try { return ValueTask.FromResult(Deserialize(utf8Json, returnType, options)); }
            catch (Exception ex) { return ValueTask.FromException<object>(ex); }
        }

        public static IAsyncEnumerable<TValue> DeserializeAsyncEnumerable<TValue>(Stream utf8Json, JsonSerializerOptionsImpl options = null, CancellationToken cancellationToken = default) =>
            DeserializeAsyncEnumerable<TValue>(utf8Json, false, options, cancellationToken);

        public static IAsyncEnumerable<TValue> DeserializeAsyncEnumerable<TValue>(Stream utf8Json, bool topLevelValues, JsonSerializerOptionsImpl options = null, CancellationToken cancellationToken = default)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            return AsyncValues<TValue>(utf8Json, (JsonTypeInfoImpl<TValue>)GetTypeInfo<TValue>(options), topLevelValues);
        }

        public static IAsyncEnumerable<TValue> DeserializeAsyncEnumerable<TValue>(Stream utf8Json, JsonTypeInfoImpl<TValue> jsonTypeInfo, CancellationToken cancellationToken = default) =>
            DeserializeAsyncEnumerable<TValue>(utf8Json, jsonTypeInfo, false, cancellationToken);

        public static IAsyncEnumerable<TValue> DeserializeAsyncEnumerable<TValue>(Stream utf8Json, JsonTypeInfoImpl<TValue> jsonTypeInfo, bool topLevelValues, CancellationToken cancellationToken = default)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            return AsyncValues<TValue>(utf8Json, Checked(jsonTypeInfo), topLevelValues);
        }

        /// <summary>
        /// The elements of a root array (or the top-level values), parsed from the buffered stream when
        /// enumeration starts; values before a malformed one are still produced, as .NET streams them.
        /// </summary>
        private static async IAsyncEnumerable<TValue> AsyncValues<TValue>(Stream utf8Json, JsonTypeInfoImpl info, bool topLevelValues)
        {
            List<TValue> values = new List<TValue>();
            Exception failure = null;
            try { ReadSequence(JsonDocumentImpl.ReadToEnd(utf8Json), info, topLevelValues, values); }
            catch (Exception ex) { failure = ex; }
            foreach (TValue value in values)
            {
                await Task.CompletedTask;
                yield return value;
            }
            if (failure != null) throw failure;
        }

        private static void ReadSequence<TValue>(byte[] utf8, JsonTypeInfoImpl info, bool topLevelValues, List<TValue> into)
        {
            JsonReaderOptionsImpl readerOptions = info._options.GetReaderOptions();
            if (topLevelValues) readerOptions.AllowMultipleValues = true;
            Utf8JsonReaderImpl reader = new Utf8JsonReaderImpl(utf8, readerOptions);
            if (topLevelValues)
            {
                while (reader.Read()) into.Add((TValue)ReadCore(ref reader, info));
                return;
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                throw new JsonExceptionImpl(JsonSR.Format(JsonSR.DeserializeUnableToConvertValue, typeof(IAsyncEnumerable<TValue>).ToString())
                    + " Path: $ | LineNumber: 0 | BytePositionInLine: " + reader.CurrentState._bytePositionInLine.ToString() + ".");
            while (true)
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.EndArray) break;
                into.Add((TValue)ReadCore(ref reader, info));
            }
            reader.Read();
        }

        /// <summary>Reads the next value of a reader (ReadValueCore): the reader ends on the value's last token.</summary>
        private static object ReadFromReader(ref Utf8JsonReaderImpl reader, JsonTypeInfoImpl info)
        {
            JsonReaderStateImpl state = reader.CurrentState;
            if (state.Options.CommentHandling == JsonCommentHandling.Allow)
                throw new ArgumentException("Comments cannot be stored when deserializing objects, only the Skip and Disallow comment handling modes are supported.", "reader");
            JsonElementImpl element = JsonElementImpl.ParseValue(ref reader);
            byte[] utf8 = element.GetRawValue().ToArray();
            return ReadFromBytes(utf8, info);
        }

        public static TValue Deserialize<TValue>(ref Utf8JsonReaderImpl reader, JsonSerializerOptionsImpl options = null) =>
            (TValue)ReadFromReader(ref reader, GetTypeInfo<TValue>(options));

        public static TValue Deserialize<TValue>(ref Utf8JsonReaderImpl reader, JsonTypeInfoImpl<TValue> jsonTypeInfo) =>
            (TValue)ReadFromReader(ref reader, Checked(jsonTypeInfo));

        public static object Deserialize(ref Utf8JsonReaderImpl reader, Type returnType, JsonSerializerOptionsImpl options = null)
        {
            if (returnType == null) throw new ArgumentNullException("returnType");
            return ReadFromReader(ref reader, GetTypeInfo(options, returnType));
        }

        public static TValue Deserialize<TValue>(JsonElementImpl element, JsonSerializerOptionsImpl options = null) =>
            (TValue)ReadFromBytes(element.GetRawValue().ToArray(), GetTypeInfo<TValue>(options));

        public static TValue Deserialize<TValue>(JsonElementImpl element, JsonTypeInfoImpl<TValue> jsonTypeInfo) =>
            (TValue)ReadFromBytes(element.GetRawValue().ToArray(), Checked(jsonTypeInfo));

        public static object Deserialize(JsonElementImpl element, Type returnType, JsonSerializerOptionsImpl options = null)
        {
            if (returnType == null) throw new ArgumentNullException("returnType");
            return ReadFromBytes(element.GetRawValue().ToArray(), GetTypeInfo(options, returnType));
        }

        public static TValue Deserialize<TValue>(JsonDocumentImpl document, JsonSerializerOptionsImpl options = null)
        {
            if (document == null) throw new ArgumentNullException("document");
            return Deserialize<TValue>(document.RootElement, options);
        }

        public static TValue Deserialize<TValue>(JsonDocumentImpl document, JsonTypeInfoImpl<TValue> jsonTypeInfo)
        {
            if (document == null) throw new ArgumentNullException("document");
            return Deserialize<TValue>(document.RootElement, jsonTypeInfo);
        }

        public static TValue Deserialize<TValue>(JsonNodeImpl node, JsonSerializerOptionsImpl options = null)
        {
            string json = node == null ? "null" : node.ToJsonString(null);
            return Deserialize<TValue>(json, options);
        }

        public static TValue Deserialize<TValue>(JsonNodeImpl node, JsonTypeInfoImpl<TValue> jsonTypeInfo)
        {
            string json = node == null ? "null" : node.ToJsonString(null);
            return Deserialize<TValue>(json, jsonTypeInfo);
        }
    }

    internal static class JsonSerializerTypeChecks
    {
        /// <summary>value is assignable to inputType: the run-time type or one of its bases (reflection-lite).</summary>
        public static bool IsAssignable(object value, Type inputType) => CoreIntrinsics.IsInstanceOfType(inputType, value);

        /// <summary>
        /// .NET rethrows InvalidOperationException/FormatException raised by System.Text.Json itself (marked
        /// by Exception.Source) as JsonException with path information; csharpc's messages say which ones.
        /// </summary>
        public static bool IsRethrowable(Exception ex)
        {
            string m = ex.Message;
            return m.StartsWith("Cannot get the value of a token type", StringComparison.Ordinal)
                || m.StartsWith("Either the JSON value is not in a supported format", StringComparison.Ordinal)
                || m.StartsWith("The JSON value is not in a supported", StringComparison.Ordinal)
                || m.StartsWith("Cannot decode JSON text that is not encoded as valid Base64", StringComparison.Ordinal)
                || m.StartsWith("Cannot read invalid UTF-16 JSON text", StringComparison.Ordinal)
                || m.StartsWith("Cannot read incomplete UTF-16 JSON text", StringComparison.Ordinal)
                || m.StartsWith("Cannot transcode invalid UTF-8 JSON text", StringComparison.Ordinal)
                || m.StartsWith("Cannot compare the value of a token type", StringComparison.Ordinal);
        }
    }

    /// <summary>Generic factory the codegen's enum switch calls (CoreIntrinsics.CreateEnumConverter).</summary>
    internal static class JsonEnumFactory
    {
        public static object Create<T>(int converterOptions, object namingPolicy, object options) where T : struct, Enum =>
            new EnumConverter<T>((EnumConverterOptions)converterOptions, (JsonNamingPolicyImpl)namingPolicy, (JsonSerializerOptionsImpl)options);
    }

    /// <summary>The corelib entry points of the code generated into programs (Frontend/JsonReflectionContexts.cs).</summary>
    public static class JsonProgramSupport
    {
        public static object CreateBuiltIn(Type type, object options) => JsonBuiltInTypeInfos.TryCreate(type, (JsonSerializerOptionsImpl)options);
    }

    /// <summary>Metadata for types every program can serialize without generated code (primitives, JSON DOM types).</summary>
    internal static class JsonBuiltInTypeInfos
    {
        public static bool IsEnum(Type type) => CoreIntrinsics.IsEnumType(type);

        public static JsonConverterImpl CreateEnumConverter(Type type, EnumConverterOptions converterOptions, JsonNamingPolicyImpl namingPolicy, JsonSerializerOptionsImpl options) =>
            (JsonConverterImpl)CoreIntrinsics.CreateEnumConverter(type, (int)converterOptions, namingPolicy, options);

        public static JsonTypeInfoImpl TryCreate(Type type, JsonSerializerOptionsImpl options)
        {
            JsonConverterImpl converter = BuiltInConverter(type);
            if (converter == null) return null;
            if (type == typeof(int)) return Value<int>(converter, options);
            if (type == typeof(string)) return Value<string>(converter, options);
            if (type == typeof(long)) return Value<long>(converter, options);
            if (type == typeof(bool)) return Value<bool>(converter, options);
            if (type == typeof(double)) return Value<double>(converter, options);
            if (type == typeof(decimal)) return Value<decimal>(converter, options);
            if (type == typeof(float)) return Value<float>(converter, options);
            if (type == typeof(short)) return Value<short>(converter, options);
            if (type == typeof(byte)) return Value<byte>(converter, options);
            if (type == typeof(sbyte)) return Value<sbyte>(converter, options);
            if (type == typeof(ushort)) return Value<ushort>(converter, options);
            if (type == typeof(uint)) return Value<uint>(converter, options);
            if (type == typeof(ulong)) return Value<ulong>(converter, options);
            if (type == typeof(char)) return Value<char>(converter, options);
            if (type == typeof(DateTime)) return Value<DateTime>(converter, options);
            if (type == typeof(TimeSpan)) return Value<TimeSpan>(converter, options);
            if (type == typeof(byte[])) return Value<byte[]>(converter, options);
            if (type == typeof(object)) return Value<object>(converter, options);
            if (type == typeof(JsonElement)) return Value<JsonElement>(converter, options);
            if (type == typeof(JsonDocument)) return Value<JsonDocument>(converter, options);
            if (type == typeof(JsonNode)) return Value<JsonNode>(converter, options);
            if (type == typeof(JsonObject)) return Value<JsonObject>(converter, options);
            if (type == typeof(JsonArray)) return Value<JsonArray>(converter, options);
            if (type == typeof(JsonValue)) return Value<JsonValue>(converter, options);
            return null;
        }

        private static JsonTypeInfoImpl Value<T>(JsonConverterImpl converter, JsonSerializerOptionsImpl options)
        {
            JsonConverterImpl effective = converter;
            if (options.HasConverters)
            {
                foreach (JsonConverter c in options.Converters)
                {
                    JsonConverterImpl candidate = (JsonConverterImpl)(object)c;
                    if (candidate.CanConvert(typeof(T)))
                    {
                        effective = JsonSerializerHelpers.ExpandConverter(typeof(T), candidate, options);
                        break;
                    }
                }
            }
            return JsonMetadataServicesImpl.CreateValueInfo<T>(options, effective);
        }

        private static JsonConverterImpl BuiltInConverter(Type type)
        {
            if (type == typeof(int)) return JsonMetadataServicesImpl.Int32Converter;
            if (type == typeof(string)) return JsonMetadataServicesImpl.StringConverter;
            if (type == typeof(long)) return JsonMetadataServicesImpl.Int64Converter;
            if (type == typeof(bool)) return JsonMetadataServicesImpl.BooleanConverter;
            if (type == typeof(double)) return JsonMetadataServicesImpl.DoubleConverter;
            if (type == typeof(decimal)) return JsonMetadataServicesImpl.DecimalConverter;
            if (type == typeof(float)) return JsonMetadataServicesImpl.SingleConverter;
            if (type == typeof(short)) return JsonMetadataServicesImpl.Int16Converter;
            if (type == typeof(byte)) return JsonMetadataServicesImpl.ByteConverter;
            if (type == typeof(sbyte)) return JsonMetadataServicesImpl.SByteConverter;
            if (type == typeof(ushort)) return JsonMetadataServicesImpl.UInt16Converter;
            if (type == typeof(uint)) return JsonMetadataServicesImpl.UInt32Converter;
            if (type == typeof(ulong)) return JsonMetadataServicesImpl.UInt64Converter;
            if (type == typeof(char)) return JsonMetadataServicesImpl.CharConverter;
            if (type == typeof(DateTime)) return JsonMetadataServicesImpl.DateTimeConverter;
            if (type == typeof(TimeSpan)) return JsonMetadataServicesImpl.TimeSpanConverter;
            if (type == typeof(byte[])) return JsonMetadataServicesImpl.ByteArrayConverter;
            if (type == typeof(object)) return JsonMetadataServicesImpl.ObjectConverter;
            if (type == typeof(JsonElement)) return JsonMetadataServicesImpl.JsonElementConverter;
            if (type == typeof(JsonDocument)) return JsonMetadataServicesImpl.JsonDocumentConverter;
            if (type == typeof(JsonNode)) return JsonMetadataServicesImpl.JsonNodeConverter;
            if (type == typeof(JsonObject)) return JsonMetadataServicesImpl.JsonObjectConverter;
            if (type == typeof(JsonArray)) return JsonMetadataServicesImpl.JsonArrayConverter;
            if (type == typeof(JsonValue)) return JsonMetadataServicesImpl.JsonValueConverter;
            return null;
        }

        /// <summary>The built-in dictionary-key converter for T (string, numbers, bool, char, DateTime...), or null.</summary>
        public static JsonConverterImpl<T> DictionaryKeyConverter<T>(JsonSerializerOptionsImpl options) =>
            CoreIntrinsics.BuiltInJsonConverter<T>() as JsonConverterImpl<T>;
    }
}
