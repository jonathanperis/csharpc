// System.Text.Json writer: Utf8JsonWriter, JsonWriterOptions, JsonEncodedText.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Writer (release/11.0):
// the same validation state machine (enclosing container + token type + depth flag), indentation,
// escaping (through the inbox JavaScriptEncoder) and exception messages. The output is built in a
// byte[] and handed to the Stream / IBufferWriter on Flush.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.JsonWriterOptions")]
    public struct JsonWriterOptionsImpl
    {
        internal const int DefaultMaxDepth = 1000;
        private int _maxDepth;
        private int _optionsMask;
        private JavaScriptEncoderImpl _encoder;

        private const int OptionsBitCount = 4;
        private const int IndentBit = 1;
        private const int SkipValidationBit = 2;
        private const int NewLineBit = 4;
        private const int IndentCharacterBit = 8;
        private const int IndentSizeMask = 127 << OptionsBitCount;

        public JavaScriptEncoderImpl Encoder
        {
            get => _encoder;
            set => _encoder = value;
        }

        public bool Indented
        {
            get => (_optionsMask & IndentBit) != 0;
            set { if (value) _optionsMask |= IndentBit; else _optionsMask &= ~IndentBit; }
        }

        public char IndentCharacter
        {
            get => (_optionsMask & IndentCharacterBit) != 0 ? '\t' : ' ';
            set
            {
                if (value != ' ' && value != '\t') throw new ArgumentOutOfRangeException("value", JsonSR.InvalidIndentCharacter);
                if (value != ' ') _optionsMask |= IndentCharacterBit; else _optionsMask &= ~IndentCharacterBit;
            }
        }

        public int IndentSize
        {
            get => EncodeIndentSize((_optionsMask & IndentSizeMask) >> OptionsBitCount);
            set
            {
                if (value < 0 || value > 127) throw new ArgumentOutOfRangeException("value", JsonSR.Format(JsonSR.InvalidIndentSize, 0, 127));
                _optionsMask = (_optionsMask & ~IndentSizeMask) | (EncodeIndentSize(value) << OptionsBitCount);
            }
        }

        // 0 and the default (2) swap so that default(JsonWriterOptions) means 2.
        private static int EncodeIndentSize(int value) => value == 0 ? 2 : value == 2 ? 0 : value;

        public int MaxDepth
        {
            get => _maxDepth;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException("value", JsonSR.MaxDepthMustBePositive);
                _maxDepth = value;
            }
        }

        public bool SkipValidation
        {
            get => (_optionsMask & SkipValidationBit) != 0;
            set { if (value) _optionsMask |= SkipValidationBit; else _optionsMask &= ~SkipValidationBit; }
        }

        public string NewLine
        {
            get => (_optionsMask & NewLineBit) != 0 ? (Environment.NewLine.Length == 2 ? "\n" : "\r\n") : Environment.NewLine;
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                if (value != "\n" && value != "\r\n") throw new ArgumentOutOfRangeException("value", JsonSR.InvalidNewLine);
                if (value != Environment.NewLine) _optionsMask |= NewLineBit; else _optionsMask &= ~NewLineBit;
            }
        }
    }

    [Shadows("System.Text.Json.JsonEncodedText")]
    public readonly struct JsonEncodedTextImpl : IEquatable<JsonEncodedTextImpl>
    {
        internal readonly byte[] _utf8Value;
        internal readonly string _value;

        internal JsonEncodedTextImpl(byte[] utf8Value, string value)
        {
            _utf8Value = utf8Value;
            _value = value;
        }

        public ReadOnlySpan<byte> EncodedUtf8Bytes => _utf8Value;

        public string Value => _value ?? string.Empty;

        internal byte[] Utf8 => _utf8Value ?? Array.Empty<byte>();

        public static JsonEncodedTextImpl Encode(string value, JavaScriptEncoderImpl encoder = null)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length > JsonConstants.MaxCharacterTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.ValueTooLarge, value.Length));
            // TranscodeAndEncode: the text is transcoded to UTF-8 first, so lone surrogates are an error
            // here (the writer, by contrast, escapes them as U+FFFD).
            if (JsonUtf8.FromUtf16(value, out int bad) == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            string escaped = JsonWriterHelper.Escape(value, encoder);
            return new JsonEncodedTextImpl(JsonWriterHelper.ToUtf8Valid(escaped), escaped);
        }

        public static JsonEncodedTextImpl Encode(ReadOnlySpan<char> value, JavaScriptEncoderImpl encoder = null) =>
            Encode(new string(value), encoder);

        public static JsonEncodedTextImpl Encode(ReadOnlySpan<byte> utf8Value, JavaScriptEncoderImpl encoder = null)
        {
            byte[] bytes = utf8Value.ToArray();
            if (bytes.Length > JsonConstants.MaxUnescapedTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.ValueTooLarge, bytes.Length));
            string escaped = JsonWriterHelper.EscapeUtf8(bytes, 0, bytes.Length, encoder);
            return new JsonEncodedTextImpl(JsonWriterHelper.ToUtf8Valid(escaped), escaped);
        }

        public bool Equals(JsonEncodedTextImpl other) => _value == null ? other._value == null : _value.Equals(other._value);

        public override bool Equals(object obj) => obj is JsonEncodedTextImpl other && Equals(other);

        public override string ToString() => _value ?? string.Empty;

        public override int GetHashCode() => _value == null ? 0 : _value.GetHashCode();
    }

    internal static class JsonBase64
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        public static string Encode(byte[] data)
        {
            char[] result = new char[(data.Length + 2) / 3 * 4];
            int o = 0;
            int i = 0;
            for (; i + 2 < data.Length; i += 3)
            {
                int v = (data[i] << 16) | (data[i + 1] << 8) | data[i + 2];
                result[o++] = Alphabet[(v >> 18) & 63];
                result[o++] = Alphabet[(v >> 12) & 63];
                result[o++] = Alphabet[(v >> 6) & 63];
                result[o++] = Alphabet[v & 63];
            }
            int rest = data.Length - i;
            if (rest == 1)
            {
                int v = data[i] << 16;
                result[o++] = Alphabet[(v >> 18) & 63];
                result[o++] = Alphabet[(v >> 12) & 63];
                result[o++] = '=';
                result[o++] = '=';
            }
            else if (rest == 2)
            {
                int v = (data[i] << 16) | (data[i + 1] << 8);
                result[o++] = Alphabet[(v >> 18) & 63];
                result[o++] = Alphabet[(v >> 12) & 63];
                result[o++] = Alphabet[(v >> 6) & 63];
                result[o++] = '=';
            }
            return new string(result);
        }
    }

    internal static class JsonWriterHelper
    {
        /// <summary>The escaped form of value (value itself when nothing needs escaping).</summary>
        public static string Escape(string value, JavaScriptEncoderImpl encoder)
        {
            TextEncoderImpl e = encoder ?? JavaScriptEncoderImpl.Default;
            int first = e.FindFirstToEncode(value, 0);
            if (first < 0) return value;
            StringBuilder sb = new StringBuilder(value.Length + 16);
            sb.Append(value, 0, first);
            e.EncodeFrom(sb, value, first);
            return sb.ToString();
        }

        /// <summary>Escapes UTF-8 input; invalid sequences become an escaped U+FFFD (OptimizedInboxTextEncoder.EncodeUtf8).</summary>
        public static string EscapeUtf8(byte[] bytes, int start, int length, JavaScriptEncoderImpl encoder)
        {
            TextEncoderImpl e = encoder ?? JavaScriptEncoderImpl.Default;
            StringBuilder sb = new StringBuilder(length + 16);
            int end = start + length;
            int i = start;
            while (i < end)
            {
                int b = bytes[i];
                if (b < 0x80)
                {
                    if (e.IsScalarAllowed(b)) sb.Append((char)b); else e.AppendEscaped(sb, b);
                    i++;
                    continue;
                }
                int n = JsonUtf8.DecodeScalar(bytes, i, end, out int scalar);
                if (n < 0)
                {
                    e.AppendEscaped(sb, 0xFFFD);
                    i += -n;
                    continue;
                }
                if (e.IsScalarAllowed(scalar)) JsonUtf8.AppendScalar(sb, scalar); else e.AppendEscaped(sb, scalar);
                i += n;
            }
            return sb.ToString();
        }

        /// <summary>UTF-8 of a string known to be valid UTF-16 (escaping replaced the invalid parts).</summary>
        public static byte[] ToUtf8Valid(string s)
        {
            byte[] result = JsonUtf8.FromUtf16(s, out int bad);
            if (result == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            return result;
        }

        public static void ValidateValue(string value)
        {
            if (value.Length > JsonConstants.MaxCharacterTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.ValueTooLarge, value.Length));
        }

        public static void ValidateProperty(string propertyName)
        {
            if (propertyName.Length > JsonConstants.MaxCharacterTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.PropertyNameTooLarge, propertyName.Length));
        }

        /// <summary>DateTime "O" with the fraction trimmed (JsonWriterHelper.Date.TrimDateTimeOffset).</summary>
        public static string FormatDateTime(DateTime value) => TrimRoundTrip(value.ToString("O"));

        public static string FormatDateTimeOffset(DateTimeOffset value) => TrimRoundTrip(value.ToString("O"));

        public static string TrimRoundTrip(string o)
        {
            // o = yyyy-MM-ddTHH:mm:ss.fffffff[Z|+hh:mm|-hh:mm]
            const int fractionEnd = 27;
            if (o.Length < fractionEnd) return o;
            int cur;
            if (o[fractionEnd - 1] != '0') return o;
            cur = fractionEnd - 1;
            while (cur > 20 && o[cur - 1] == '0') cur--;
            if (cur == 20) cur = 19; // all zero: drop the '.' too
            return o.Substring(0, cur) + o.Substring(fractionEnd);
        }
    }

    [Shadows("System.Text.Json.Utf8JsonWriter")]
    public sealed class Utf8JsonWriterImpl : IDisposable, IAsyncDisposable
    {
        private const byte ContainerNone = 0;            // JsonTokenType.None
        private const byte ContainerObject = 5;          // JsonTokenType.PropertyName
        private const byte ContainerArray = 0x10;
        private const byte ContainerUtf8String = 0x20;
        private const byte ContainerUtf16String = 0x30;
        private const byte ContainerBase64String = 0x40;

        private IBufferWriter<byte> _output;
        private Stream _stream;
        private byte[] _buffer = new byte[256];
        private int _pending;
        private long _committed;

        private byte _enclosingContainer;
        private bool _commentAfterNoneOrPropertyName;
        private JsonTokenType _tokenType;
        private JsonBitStack _bitStack;
        private int _currentDepth;

        private JsonWriterOptionsImpl _options;
        private bool _indented;
        private bool _skipValidation;
        private byte _indentByte;
        private int _indentLength;
        private int _newLineLength;
        private int _maxDepth;
        private JavaScriptEncoderImpl _encoder;

        // Partial string segments (WriteStringValueSegment / WriteBase64StringSegment).
        private byte[] _partial = new byte[3];
        private int _partialLength;

        public Utf8JsonWriterImpl(IBufferWriter<byte> bufferWriter, JsonWriterOptionsImpl options = default)
        {
            if (bufferWriter == null) throw new ArgumentNullException("bufferWriter");
            _output = bufferWriter;
            SetOptions(options);
        }

        public Utf8JsonWriterImpl(Stream utf8Json, JsonWriterOptionsImpl options = default)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            if (!utf8Json.CanWrite) throw new ArgumentException(JsonSR.StreamNotWritable);
            _stream = utf8Json;
            SetOptions(options);
        }

        /// <summary>A writer that only accumulates (the serializer reads the bytes back).</summary>
        internal Utf8JsonWriterImpl(JsonWriterOptionsImpl options)
        {
            _output = null;
            _stream = null;
            _internalOnly = true;
            SetOptions(options);
        }

        private bool _internalOnly;

        private void SetOptions(JsonWriterOptionsImpl options)
        {
            _options = options;
            _indented = options.Indented;
            _skipValidation = options.SkipValidation;
            _indentByte = (byte)options.IndentCharacter;
            _indentLength = options.IndentSize;
            _newLineLength = options.NewLine.Length;
            _encoder = options.Encoder;
            if (_options.MaxDepth == 0) _options.MaxDepth = JsonWriterOptionsImpl.DefaultMaxDepth;
            _maxDepth = _options.MaxDepth;
        }

        public int BytesPending => _pending;

        public long BytesCommitted => _committed;

        public JsonWriterOptionsImpl Options => _options;

        public int CurrentDepth => _currentDepth & JsonConstants.RemoveFlagsBitMask;

        internal JsonTokenType TokenType => _tokenType;

        private int Indentation => CurrentDepth * _indentLength;

        /// <summary>The UTF-8 written so far (internal writers used by the serializer).</summary>
        internal byte[] WrittenBytes()
        {
            byte[] result = new byte[_pending];
            Array.Copy(_buffer, result, _pending);
            return result;
        }

        internal string WrittenString() => JsonUtf8.ToUtf16(_buffer, 0, _pending);

        public void Reset()
        {
            CheckNotDisposed();
            ResetHelper();
        }

        public void Reset(Stream utf8Json)
        {
            CheckNotDisposed();
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            if (!utf8Json.CanWrite) throw new ArgumentException(JsonSR.StreamNotWritable);
            _stream = utf8Json;
            _output = null;
            ResetHelper();
        }

        public void Reset(Stream utf8Json, JsonWriterOptionsImpl options)
        {
            Reset(utf8Json);
            SetOptions(options);
        }

        public void Reset(IBufferWriter<byte> bufferWriter)
        {
            CheckNotDisposed();
            if (bufferWriter == null) throw new ArgumentNullException("bufferWriter");
            _output = bufferWriter;
            _stream = null;
            ResetHelper();
        }

        public void Reset(IBufferWriter<byte> bufferWriter, JsonWriterOptionsImpl options)
        {
            Reset(bufferWriter);
            SetOptions(options);
        }

        /// <summary>A cached internal writer starts over with these options (Serializer's writer cache).</summary>
        internal void ResetForReuse(JsonWriterOptionsImpl options)
        {
            SetOptions(options);
            ResetHelper();
        }

        /// <summary>Whether the cached writer's buffer is small enough to keep for the next serialization.</summary>
        internal bool KeepForReuse => _buffer.Length <= 64 * 1024;

        private void ResetHelper()
        {
            _pending = 0;
            _committed = 0;
            _enclosingContainer = ContainerNone;
            _tokenType = JsonTokenType.None;
            _commentAfterNoneOrPropertyName = false;
            _currentDepth = 0;
            _bitStack = default;
            _partialLength = 0;
        }

        private void CheckNotDisposed()
        {
            if (_stream == null && _output == null && !_internalOnly)
                throw new ObjectDisposedException("Utf8JsonWriter", JsonSR.ObjectDisposed);
        }

        public void Flush()
        {
            CheckNotDisposed();
            if (_stream != null)
            {
                if (_pending != 0)
                {
                    _stream.Write(_buffer, 0, _pending);
                    _committed += _pending;
                    _pending = 0;
                }
                _stream.Flush();
            }
            else if (_output != null)
            {
                if (_pending != 0)
                {
                    Span<byte> span = _output.GetSpan(_pending);
                    if (span.Length < _pending) throw new InvalidOperationException(JsonSR.FailedToGetLargerSpan);
                    for (int i = 0; i < _pending; i++) span[i] = _buffer[i];
                    _output.Advance(_pending);
                    _committed += _pending;
                    _pending = 0;
                }
            }
        }

        public void Dispose()
        {
            if (_stream == null && _output == null) return;
            Flush();
            ResetHelper();
            _stream = null;
            _output = null;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            Flush();
            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------ buffer

        private void Ensure(int extra)
        {
            int needed = _pending + extra;
            if (needed <= _buffer.Length) return;
            int size = _buffer.Length * 2;
            if (size < needed) size = needed;
            byte[] grown = new byte[size];
            Array.Copy(_buffer, grown, _pending);
            _buffer = grown;
        }

        private void Put(byte b)
        {
            if (_pending == _buffer.Length) Ensure(1);
            _buffer[_pending++] = b;
        }

        private void PutBytes(byte[] bytes)
        {
            Ensure(bytes.Length);
            Array.Copy(bytes, 0, _buffer, _pending, bytes.Length);
            _pending += bytes.Length;
        }

        private void PutBytes(byte[] bytes, int start, int length)
        {
            Ensure(length);
            Array.Copy(bytes, start, _buffer, _pending, length);
            _pending += length;
        }

        /// <summary>Appends a string known to be valid UTF-16 as UTF-8.</summary>
        private void PutUtf16(string s)
        {
            Ensure(s.Length * 3);
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                if (c < 0x80) _buffer[_pending++] = (byte)c;
                else if (c < 0x800)
                {
                    _buffer[_pending++] = (byte)(0xC0 | (c >> 6));
                    _buffer[_pending++] = (byte)(0x80 | (c & 0x3F));
                }
                else if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                {
                    int scalar = ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00) + 0x10000;
                    i++;
                    _buffer[_pending++] = (byte)(0xF0 | (scalar >> 18));
                    _buffer[_pending++] = (byte)(0x80 | ((scalar >> 12) & 0x3F));
                    _buffer[_pending++] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
                    _buffer[_pending++] = (byte)(0x80 | (scalar & 0x3F));
                }
                else
                {
                    _buffer[_pending++] = (byte)(0xE0 | (c >> 12));
                    _buffer[_pending++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    _buffer[_pending++] = (byte)(0x80 | (c & 0x3F));
                }
            }
        }

        private void PutAscii(string s)
        {
            Ensure(s.Length);
            for (int i = 0; i < s.Length; i++) _buffer[_pending++] = (byte)s[i];
        }

        private void WriteNewLine()
        {
            if (_newLineLength == 2) Put(JsonConstants.CarriageReturn);
            Put(JsonConstants.LineFeed);
        }

        private void WriteIndentation(int indent)
        {
            Ensure(indent);
            for (int i = 0; i < indent; i++) _buffer[_pending++] = _indentByte;
        }

        private void SetFlagToAddListSeparatorBeforeNextItem() => _currentDepth |= 1 << 31;

        private bool CanWriteValue => _enclosingContainer == ContainerArray | _enclosingContainer == (byte)_tokenType;

        private bool IsWritingPartialString => _enclosingContainer >= ContainerUtf8String;

        // ------------------------------------------------------------------ validation

        private string TokenName() => _tokenType.ToString();

        private void ThrowInvalidOperation(string format) =>
            throw new InvalidOperationException(JsonSR.Format(format, TokenName()));

        private void ValidateWritingValue()
        {
            if (!CanWriteValue)
            {
                if (IsWritingPartialString) throw new InvalidOperationException(CannotWriteWithinString);
                if (_enclosingContainer == ContainerObject) ThrowInvalidOperation(JsonSR.CannotWriteValueWithinObject);
                ThrowInvalidOperation(JsonSR.CannotWriteValueAfterPrimitiveOrClose);
            }
        }

        private const string CannotWriteWithinString = "Writing a JSON property or value before writing the final string value segment is not supported.";

        private void ValidateStart()
        {
            if (!CanWriteValue)
            {
                if (IsWritingPartialString) throw new InvalidOperationException(CannotWriteWithinString);
                if (_enclosingContainer == ContainerObject) ThrowInvalidOperation(JsonSR.CannotStartObjectArrayWithoutProperty);
                ThrowInvalidOperation(JsonSR.CannotStartObjectArrayAfterPrimitiveOrClose);
            }
        }

        private void ValidateWritingProperty()
        {
            if (!_skipValidation)
            {
                if (_enclosingContainer != ContainerObject || _tokenType == JsonTokenType.PropertyName)
                {
                    if (IsWritingPartialString) throw new InvalidOperationException(CannotWriteWithinString);
                    if (_tokenType == JsonTokenType.PropertyName) throw new InvalidOperationException(JsonSR.CannotWritePropertyAfterProperty);
                    ThrowInvalidOperation(JsonSR.CannotWritePropertyWithinArray);
                }
            }
        }

        private void ValidateDepth()
        {
            if (CurrentDepth >= _maxDepth)
                throw new InvalidOperationException(JsonSR.Format(JsonSR.DepthTooLarge, CurrentDepth, _maxDepth));
        }

        private void ValidatePropertyNameAndDepth(int nameLength)
        {
            if (nameLength > JsonConstants.MaxCharacterTokenSize || CurrentDepth >= _maxDepth)
            {
                if (CurrentDepth >= _maxDepth) throw new InvalidOperationException(JsonSR.Format(JsonSR.DepthTooLarge, CurrentDepth, _maxDepth));
                throw new ArgumentException(JsonSR.Format(JsonSR.PropertyNameTooLarge, nameLength));
            }
        }

        private void UpdateBitStackOnStart(byte token)
        {
            if (token == JsonConstants.OpenBracket)
            {
                _bitStack.PushFalse();
                _enclosingContainer = ContainerArray;
            }
            else
            {
                _bitStack.PushTrue();
                _enclosingContainer = ContainerObject;
            }
        }

        // ------------------------------------------------------------------ start / end

        public void WriteStartArray()
        {
            WriteStart(JsonConstants.OpenBracket);
            _tokenType = JsonTokenType.StartArray;
        }

        public void WriteStartObject()
        {
            WriteStart(JsonConstants.OpenBrace);
            _tokenType = JsonTokenType.StartObject;
        }

        private void WriteStart(byte token)
        {
            if (CurrentDepth >= _maxDepth)
                throw new InvalidOperationException(JsonSR.Format(JsonSR.DepthTooLarge, _currentDepth & JsonConstants.RemoveFlagsBitMask, _maxDepth));
            if (_indented || !_skipValidation)
            {
                if (!_skipValidation)
                {
                    ValidateStart();
                    UpdateBitStackOnStart(token);
                }
                if (_indented) WriteStartIndented(token);
                else WriteStartMinimized(token);
            }
            else WriteStartMinimized(token);
            _currentDepth &= JsonConstants.RemoveFlagsBitMask;
            _currentDepth++;
        }

        private void WriteStartMinimized(byte token)
        {
            if (_currentDepth < 0) Put(JsonConstants.ListSeparator);
            Put(token);
        }

        private void WriteStartIndented(byte token)
        {
            int indent = Indentation;
            if (_currentDepth < 0) Put(JsonConstants.ListSeparator);
            if ((_tokenType != JsonTokenType.PropertyName && _tokenType != JsonTokenType.None) || _commentAfterNoneOrPropertyName)
            {
                WriteNewLine();
                WriteIndentation(indent);
            }
            Put(token);
        }

        public void WriteStartArray(JsonEncodedTextImpl propertyName)
        {
            WriteStartHelper(propertyName.Utf8, JsonConstants.OpenBracket);
            _tokenType = JsonTokenType.StartArray;
        }

        public void WriteStartObject(JsonEncodedTextImpl propertyName)
        {
            WriteStartHelper(propertyName.Utf8, JsonConstants.OpenBrace);
            _tokenType = JsonTokenType.StartObject;
        }

        public void WriteStartArray(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            WriteStartWithName(propertyName, JsonConstants.OpenBracket);
            _tokenType = JsonTokenType.StartArray;
        }

        public void WriteStartObject(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            WriteStartWithName(propertyName, JsonConstants.OpenBrace);
            _tokenType = JsonTokenType.StartObject;
        }

        public void WriteStartArray(ReadOnlySpan<char> propertyName)
        {
            WriteStartWithName(new string(propertyName), JsonConstants.OpenBracket);
            _tokenType = JsonTokenType.StartArray;
        }

        public void WriteStartObject(ReadOnlySpan<char> propertyName)
        {
            WriteStartWithName(new string(propertyName), JsonConstants.OpenBrace);
            _tokenType = JsonTokenType.StartObject;
        }

        public void WriteStartArray(ReadOnlySpan<byte> utf8PropertyName)
        {
            byte[] name = utf8PropertyName.ToArray();
            WriteStartWithUtf8Name(name, JsonConstants.OpenBracket);
            _tokenType = JsonTokenType.StartArray;
        }

        public void WriteStartObject(ReadOnlySpan<byte> utf8PropertyName)
        {
            byte[] name = utf8PropertyName.ToArray();
            WriteStartWithUtf8Name(name, JsonConstants.OpenBrace);
            _tokenType = JsonTokenType.StartObject;
        }

        private void WriteStartWithName(string propertyName, byte token)
        {
            ValidatePropertyNameAndDepth(propertyName.Length);
            byte[] escaped = JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.Escape(propertyName, _encoder));
            WriteStartByOptions(escaped, token);
            _currentDepth &= JsonConstants.RemoveFlagsBitMask;
            _currentDepth++;
        }

        private void WriteStartWithUtf8Name(byte[] utf8PropertyName, byte token)
        {
            ValidatePropertyNameAndDepth(utf8PropertyName.Length);
            byte[] escaped = JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.EscapeUtf8(utf8PropertyName, 0, utf8PropertyName.Length, _encoder));
            WriteStartByOptions(escaped, token);
            _currentDepth &= JsonConstants.RemoveFlagsBitMask;
            _currentDepth++;
        }

        private void WriteStartHelper(byte[] escapedName, byte token)
        {
            ValidateDepth();
            WriteStartByOptions(escapedName, token);
            _currentDepth &= JsonConstants.RemoveFlagsBitMask;
            _currentDepth++;
        }

        private void WriteStartByOptions(byte[] escapedName, byte token)
        {
            ValidateWritingProperty();
            if (!_skipValidation) UpdateBitStackOnStart(token);
            WritePropertyNamePrefix(escapedName);
            Put(token);
        }

        /// <summary>[,][newline indent]"name":[ ] — the part of every property write before its value.</summary>
        private void WritePropertyNamePrefix(byte[] escapedName)
        {
            if (_currentDepth < 0) Put(JsonConstants.ListSeparator);
            if (_indented)
            {
                if (_tokenType != JsonTokenType.None) WriteNewLine();
                WriteIndentation(Indentation);
            }
            Put(JsonConstants.Quote);
            PutBytes(escapedName);
            Put(JsonConstants.Quote);
            Put(JsonConstants.KeyValueSeparator);
            if (_indented) Put(JsonConstants.Space);
        }

        public void WriteEndArray()
        {
            WriteEnd(JsonConstants.CloseBracket);
            _tokenType = JsonTokenType.EndArray;
        }

        public void WriteEndObject()
        {
            WriteEnd(JsonConstants.CloseBrace);
            _tokenType = JsonTokenType.EndObject;
        }

        private void WriteEnd(byte token)
        {
            if (_indented || !_skipValidation)
            {
                if (!_skipValidation) ValidateEnd(token);
                if (_indented) WriteEndIndented(token);
                else Put(token);
            }
            else Put(token);
            SetFlagToAddListSeparatorBeforeNextItem();
            if (CurrentDepth != 0) _currentDepth--;
        }

        private void ValidateEnd(byte token)
        {
            if (_tokenType == JsonTokenType.PropertyName) ThrowMismatched(token);
            if (token == JsonConstants.CloseBracket)
            {
                if (_enclosingContainer != ContainerArray) ThrowMismatched(token);
            }
            else if (_enclosingContainer != ContainerObject) ThrowMismatched(token);
            bool inObject = _bitStack.Pop();
            _enclosingContainer = _bitStack.CurrentDepth == 0 ? ContainerNone : (inObject ? ContainerObject : ContainerArray);
        }

        private void ThrowMismatched(byte token)
        {
            if (IsWritingPartialString) throw new InvalidOperationException(CannotWriteWithinString);
            if (_tokenType == JsonTokenType.PropertyName)
                throw new InvalidOperationException(JsonSR.Format(JsonSR.CannotWriteEndAfterProperty, (char)token));
            throw new InvalidOperationException(JsonSR.Format(JsonSR.MismatchedObjectArray, (char)token));
        }

        private void WriteEndIndented(byte token)
        {
            if (_tokenType == JsonTokenType.StartObject || _tokenType == JsonTokenType.StartArray)
            {
                Put(token);
                return;
            }
            int indent = Indentation;
            if (indent != 0) indent -= _indentLength;
            WriteNewLine();
            WriteIndentation(indent);
            Put(token);
        }

        // ------------------------------------------------------------------ property names

        public void WritePropertyName(JsonEncodedTextImpl propertyName) => WritePropertyNameEscaped(propertyName.Utf8);

        public void WritePropertyName(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            JsonWriterHelper.ValidateProperty(propertyName);
            WritePropertyNameEscaped(JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.Escape(propertyName, _encoder)));
        }

        public void WritePropertyName(ReadOnlySpan<char> propertyName) => WritePropertyName(new string(propertyName));

        public void WritePropertyName(ReadOnlySpan<byte> utf8PropertyName)
        {
            byte[] name = utf8PropertyName.ToArray();
            if (name.Length > JsonConstants.MaxUnescapedTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.PropertyNameTooLarge, name.Length));
            WritePropertyNameEscaped(JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.EscapeUtf8(name, 0, name.Length, _encoder)));
        }

        private void WritePropertyNameEscaped(byte[] escapedName)
        {
            ValidateWritingProperty();
            WritePropertyNamePrefix(escapedName);
            _currentDepth &= JsonConstants.RemoveFlagsBitMask;
            _tokenType = JsonTokenType.PropertyName;
            _commentAfterNoneOrPropertyName = false;
        }

        private byte[] EscapedName(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            JsonWriterHelper.ValidateProperty(propertyName);
            return JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.Escape(propertyName, _encoder));
        }

        private byte[] EscapedName(ReadOnlySpan<byte> utf8PropertyName)
        {
            byte[] name = utf8PropertyName.ToArray();
            if (name.Length > JsonConstants.MaxUnescapedTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.PropertyNameTooLarge, name.Length));
            return JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.EscapeUtf8(name, 0, name.Length, _encoder));
        }

        /// <summary>A property followed by a raw (already formatted) value: "name":value.</summary>
        private void WritePropertyAndRaw(byte[] escapedName, byte[] raw, JsonTokenType tokenType)
        {
            ValidateWritingProperty();
            WritePropertyNamePrefix(escapedName);
            PutBytes(raw);
            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = tokenType;
        }

        private void WritePropertyAndAscii(byte[] escapedName, string ascii, JsonTokenType tokenType)
        {
            ValidateWritingProperty();
            WritePropertyNamePrefix(escapedName);
            PutAscii(ascii);
            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = tokenType;
        }

        private void WritePropertyAndString(byte[] escapedName, byte[] escapedValue)
        {
            ValidateWritingProperty();
            WritePropertyNamePrefix(escapedName);
            Put(JsonConstants.Quote);
            PutBytes(escapedValue);
            Put(JsonConstants.Quote);
            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = JsonTokenType.String;
        }

        // ------------------------------------------------------------------ values

        /// <summary>[,][newline indent] before a value (no indentation after a property name).</summary>
        private void WriteValuePrefix()
        {
            if (!_skipValidation) ValidateWritingValue();
            if (_currentDepth < 0) Put(JsonConstants.ListSeparator);
            if (_indented && _tokenType != JsonTokenType.PropertyName)
            {
                if (_tokenType != JsonTokenType.None) WriteNewLine();
                WriteIndentation(Indentation);
            }
        }

        private void WriteAsciiValue(string ascii, JsonTokenType tokenType)
        {
            WriteValuePrefix();
            PutAscii(ascii);
            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = tokenType;
        }

        private void WriteEscapedStringValue(byte[] escapedValue)
        {
            WriteValuePrefix();
            Put(JsonConstants.Quote);
            PutBytes(escapedValue);
            Put(JsonConstants.Quote);
            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = JsonTokenType.String;
        }

        public void WriteNullValue() => WriteAsciiValue("null", JsonTokenType.Null);

        public void WriteBooleanValue(bool value)
        {
            if (value) WriteAsciiValue("true", JsonTokenType.True);
            else WriteAsciiValue("false", JsonTokenType.False);
        }

        public void WriteStringValue(JsonEncodedTextImpl value) => WriteEscapedStringValue(value.Utf8);

        public void WriteStringValue(string value)
        {
            if (value == null) { WriteNullValue(); return; }
            JsonWriterHelper.ValidateValue(value);
            WriteEscapedStringValue(JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.Escape(value, _encoder)));
        }

        public void WriteStringValue(ReadOnlySpan<char> value)
        {
            string s = new string(value);
            JsonWriterHelper.ValidateValue(s);
            WriteEscapedStringValue(JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.Escape(s, _encoder)));
        }

        public void WriteStringValue(ReadOnlySpan<byte> utf8Value)
        {
            byte[] bytes = utf8Value.ToArray();
            if (bytes.Length > JsonConstants.MaxUnescapedTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.ValueTooLarge, bytes.Length));
            WriteEscapedStringValue(JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.EscapeUtf8(bytes, 0, bytes.Length, _encoder)));
        }

        public void WriteStringValue(DateTime value) => WriteEscapedStringValue(Ascii(JsonWriterHelper.FormatDateTime(value)));

        public void WriteStringValue(DateTimeOffset value) => WriteEscapedStringValue(Ascii(JsonWriterHelper.FormatDateTimeOffset(value)));

        public void WriteStringValue(Guid value) => WriteEscapedStringValue(Ascii(value.ToString()));

        private static byte[] Ascii(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        public void WriteNumberValue(int value) => WriteAsciiValue(value.ToString(), JsonTokenType.Number);
        public void WriteNumberValue(long value) => WriteAsciiValue(value.ToString(), JsonTokenType.Number);
        public void WriteNumberValue(uint value) => WriteAsciiValue(value.ToString(), JsonTokenType.Number);
        public void WriteNumberValue(ulong value) => WriteAsciiValue(value.ToString(), JsonTokenType.Number);
        public void WriteNumberValue(decimal value) => WriteAsciiValue(value.ToString(), JsonTokenType.Number);

        public void WriteNumberValue(double value)
        {
            ValidateFinite(value);
            WriteAsciiValue(value.ToString(), JsonTokenType.Number);
        }

        public void WriteNumberValue(float value)
        {
            ValidateFinite(value);
            WriteAsciiValue(value.ToString(), JsonTokenType.Number);
        }

        internal static void ValidateFinite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException(JsonSR.SpecialNumberValuesNotSupported);
        }

        internal static void ValidateFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(JsonSR.SpecialNumberValuesNotSupported);
        }

        /// <summary>A number already formatted by the caller (serializer number handling, JsonElement.WriteTo).</summary>
        internal void WriteNumberValueRaw(string ascii) => WriteAsciiValue(ascii, JsonTokenType.Number);

        internal void WriteNumberValueRaw(byte[] utf8, int start, int length)
        {
            WriteValuePrefix();
            PutBytes(utf8, start, length);
            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = JsonTokenType.Number;
        }

        /// <summary>A string value whose UTF-8 is already escaped (JsonElement/JsonDocument round trips).</summary>
        internal void WriteStringValueEscapedRaw(byte[] utf8, int start, int length)
        {
            WriteValuePrefix();
            Put(JsonConstants.Quote);
            PutBytes(utf8, start, length);
            Put(JsonConstants.Quote);
            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = JsonTokenType.String;
        }

        internal void WritePropertyNameEscapedRaw(byte[] utf8, int start, int length) =>
            WritePropertyNameEscaped(JsonUtf8.Slice(utf8, start, length));

        public void WriteBase64StringValue(ReadOnlySpan<byte> bytes)
        {
            // Base64 text is written as is: its alphabet needs no JSON escaping (even '+').
            WriteEscapedStringValue(JsonWriterHelper.ToUtf8Valid(JsonBase64.Encode(bytes.ToArray())));
        }

        public void WriteCommentValue(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            WriteCommentValue(value.AsSpan());
        }

        public void WriteCommentValue(ReadOnlySpan<char> value)
        {
            string s = new string(value);
            JsonWriterHelper.ValidateValue(s);
            if (s.Contains("*/")) throw new ArgumentException(JsonSR.CannotWriteCommentWithEmbeddedDelimiter);
            byte[] utf8 = JsonUtf8.FromUtf16(s, out int bad);
            if (utf8 == null)
            {
                // ThrowArgumentException_InvalidUTF16(value[written]): .NET indexes the text with the number of
                // UTF-8 bytes written before the bad surrogate.
                int written = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { written += 4; i++; }
                    else if (char.IsSurrogate(c)) break;
                    else written += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                }
                throw new ArgumentException("Cannot encode invalid UTF-16 text as JSON. Invalid surrogate value: '0x" + ((int)s[written]).ToString("X2") + "'.");
            }
            WriteCommentBytes(utf8);
        }

        public void WriteCommentValue(ReadOnlySpan<byte> utf8Value)
        {
            byte[] bytes = utf8Value.ToArray();
            if (bytes.Length > JsonConstants.MaxUnescapedTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.ValueTooLarge, bytes.Length));
            for (int i = 0; i + 1 < bytes.Length; i++)
                if (bytes[i] == '*' && bytes[i + 1] == '/') throw new ArgumentException(JsonSR.CannotWriteCommentWithEmbeddedDelimiter);
            if (JsonUtf8.ToUtf16(bytes, 0, bytes.Length) == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf8);
            WriteCommentBytes(bytes);
        }

        private void WriteCommentBytes(byte[] utf8)
        {
            if (IsWritingPartialString) throw new InvalidOperationException(CannotWriteWithinString);
            if (_indented)
            {
                if (_tokenType != JsonTokenType.None || _commentAfterNoneOrPropertyName)
                {
                    WriteNewLine();
                    WriteIndentation(Indentation);
                }
            }
            Put((byte)'/');
            Put((byte)'*');
            PutBytes(utf8);
            Put((byte)'*');
            Put((byte)'/');
            if (_tokenType == JsonTokenType.None || _tokenType == JsonTokenType.PropertyName) _commentAfterNoneOrPropertyName = true;
        }

        public void WriteRawValue(string json, bool skipInputValidation = false)
        {
            if (!_skipValidation) ValidateWritingValue();
            if (json == null) throw new ArgumentNullException("json");
            TranscodeAndWriteRawValue(json, skipInputValidation);
        }

        public void WriteRawValue(ReadOnlySpan<char> json, bool skipInputValidation = false)
        {
            if (!_skipValidation) ValidateWritingValue();
            TranscodeAndWriteRawValue(new string(json), skipInputValidation);
        }

        public void WriteRawValue(ReadOnlySpan<byte> utf8Json, bool skipInputValidation = false)
        {
            if (!_skipValidation) ValidateWritingValue();
            WriteRawValueCore(utf8Json.ToArray(), skipInputValidation);
        }

        private void TranscodeAndWriteRawValue(string json, bool skipInputValidation)
        {
            if (json.Length > JsonConstants.MaxUtf16RawValueLength) throw new ArgumentException(JsonSR.Format(JsonSR.ValueTooLarge, json.Length));
            byte[] utf8 = JsonUtf8.FromUtf16(json, out int bad);
            if (utf8 == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            WriteRawValueCore(utf8, skipInputValidation);
        }

        private void WriteRawValueCore(byte[] utf8Json, bool skipInputValidation)
        {
            if (utf8Json.Length == 0) throw new ArgumentException(JsonSR.ExpectedJsonTokens);
            if (skipInputValidation)
            {
                // Unvalidated raw values count as a string: equivalent to any complete value for structural validation.
                _tokenType = JsonTokenType.String;
            }
            else
            {
                Utf8JsonReaderImpl reader = new Utf8JsonReaderImpl(utf8Json, new JsonReaderOptionsImpl());
                while (reader.Read()) { }
                _tokenType = reader.TokenType;
            }
            if (_currentDepth < 0) Put(JsonConstants.ListSeparator);
            PutBytes(utf8Json);
            SetFlagToAddListSeparatorBeforeNextItem();
        }

        // ------------------------------------------------------------------ name + value

        public void WriteNull(JsonEncodedTextImpl propertyName) => WritePropertyAndAscii(propertyName.Utf8, "null", JsonTokenType.Null);
        public void WriteNull(string propertyName) => WritePropertyAndAscii(EscapedName(propertyName), "null", JsonTokenType.Null);
        public void WriteNull(ReadOnlySpan<char> propertyName) => WriteNull(new string(propertyName));
        public void WriteNull(ReadOnlySpan<byte> utf8PropertyName) => WritePropertyAndAscii(EscapedName(utf8PropertyName), "null", JsonTokenType.Null);

        public void WriteBoolean(JsonEncodedTextImpl propertyName, bool value) => WritePropertyAndAscii(propertyName.Utf8, value ? "true" : "false", value ? JsonTokenType.True : JsonTokenType.False);
        public void WriteBoolean(string propertyName, bool value) => WritePropertyAndAscii(EscapedName(propertyName), value ? "true" : "false", value ? JsonTokenType.True : JsonTokenType.False);
        public void WriteBoolean(ReadOnlySpan<char> propertyName, bool value) => WriteBoolean(new string(propertyName), value);
        public void WriteBoolean(ReadOnlySpan<byte> utf8PropertyName, bool value) => WritePropertyAndAscii(EscapedName(utf8PropertyName), value ? "true" : "false", value ? JsonTokenType.True : JsonTokenType.False);

        public void WriteNumber(JsonEncodedTextImpl propertyName, int value) => WritePropertyAndAscii(propertyName.Utf8, value.ToString(), JsonTokenType.Number);
        public void WriteNumber(string propertyName, int value) => WritePropertyAndAscii(EscapedName(propertyName), value.ToString(), JsonTokenType.Number);
        public void WriteNumber(ReadOnlySpan<char> propertyName, int value) => WriteNumber(new string(propertyName), value);
        public void WriteNumber(ReadOnlySpan<byte> utf8PropertyName, int value) => WritePropertyAndAscii(EscapedName(utf8PropertyName), value.ToString(), JsonTokenType.Number);

        public void WriteNumber(JsonEncodedTextImpl propertyName, long value) => WritePropertyAndAscii(propertyName.Utf8, value.ToString(), JsonTokenType.Number);
        public void WriteNumber(string propertyName, long value) => WritePropertyAndAscii(EscapedName(propertyName), value.ToString(), JsonTokenType.Number);
        public void WriteNumber(ReadOnlySpan<char> propertyName, long value) => WriteNumber(new string(propertyName), value);
        public void WriteNumber(ReadOnlySpan<byte> utf8PropertyName, long value) => WritePropertyAndAscii(EscapedName(utf8PropertyName), value.ToString(), JsonTokenType.Number);

        public void WriteNumber(JsonEncodedTextImpl propertyName, uint value) => WritePropertyAndAscii(propertyName.Utf8, value.ToString(), JsonTokenType.Number);
        public void WriteNumber(string propertyName, uint value) => WritePropertyAndAscii(EscapedName(propertyName), value.ToString(), JsonTokenType.Number);
        public void WriteNumber(ReadOnlySpan<char> propertyName, uint value) => WriteNumber(new string(propertyName), value);
        public void WriteNumber(ReadOnlySpan<byte> utf8PropertyName, uint value) => WritePropertyAndAscii(EscapedName(utf8PropertyName), value.ToString(), JsonTokenType.Number);

        public void WriteNumber(JsonEncodedTextImpl propertyName, ulong value) => WritePropertyAndAscii(propertyName.Utf8, value.ToString(), JsonTokenType.Number);
        public void WriteNumber(string propertyName, ulong value) => WritePropertyAndAscii(EscapedName(propertyName), value.ToString(), JsonTokenType.Number);
        public void WriteNumber(ReadOnlySpan<char> propertyName, ulong value) => WriteNumber(new string(propertyName), value);
        public void WriteNumber(ReadOnlySpan<byte> utf8PropertyName, ulong value) => WritePropertyAndAscii(EscapedName(utf8PropertyName), value.ToString(), JsonTokenType.Number);

        public void WriteNumber(JsonEncodedTextImpl propertyName, decimal value) => WritePropertyAndAscii(propertyName.Utf8, value.ToString(), JsonTokenType.Number);
        public void WriteNumber(string propertyName, decimal value) => WritePropertyAndAscii(EscapedName(propertyName), value.ToString(), JsonTokenType.Number);
        public void WriteNumber(ReadOnlySpan<char> propertyName, decimal value) => WriteNumber(new string(propertyName), value);
        public void WriteNumber(ReadOnlySpan<byte> utf8PropertyName, decimal value) => WritePropertyAndAscii(EscapedName(utf8PropertyName), value.ToString(), JsonTokenType.Number);

        public void WriteNumber(JsonEncodedTextImpl propertyName, double value) { ValidateFinite(value); WritePropertyAndAscii(propertyName.Utf8, value.ToString(), JsonTokenType.Number); }
        public void WriteNumber(string propertyName, double value) { byte[] n = EscapedName(propertyName); ValidateFinite(value); WritePropertyAndAscii(n, value.ToString(), JsonTokenType.Number); }
        public void WriteNumber(ReadOnlySpan<char> propertyName, double value) => WriteNumber(new string(propertyName), value);
        public void WriteNumber(ReadOnlySpan<byte> utf8PropertyName, double value) { byte[] n = EscapedName(utf8PropertyName); ValidateFinite(value); WritePropertyAndAscii(n, value.ToString(), JsonTokenType.Number); }

        public void WriteNumber(JsonEncodedTextImpl propertyName, float value) { ValidateFinite(value); WritePropertyAndAscii(propertyName.Utf8, value.ToString(), JsonTokenType.Number); }
        public void WriteNumber(string propertyName, float value) { byte[] n = EscapedName(propertyName); ValidateFinite(value); WritePropertyAndAscii(n, value.ToString(), JsonTokenType.Number); }
        public void WriteNumber(ReadOnlySpan<char> propertyName, float value) => WriteNumber(new string(propertyName), value);
        public void WriteNumber(ReadOnlySpan<byte> utf8PropertyName, float value) { byte[] n = EscapedName(utf8PropertyName); ValidateFinite(value); WritePropertyAndAscii(n, value.ToString(), JsonTokenType.Number); }

        public void WriteString(JsonEncodedTextImpl propertyName, JsonEncodedTextImpl value) => WritePropertyAndString(propertyName.Utf8, value.Utf8);
        public void WriteString(string propertyName, JsonEncodedTextImpl value) => WritePropertyAndString(EscapedName(propertyName), value.Utf8);
        public void WriteString(ReadOnlySpan<char> propertyName, JsonEncodedTextImpl value) => WriteString(new string(propertyName), value);
        public void WriteString(ReadOnlySpan<byte> utf8PropertyName, JsonEncodedTextImpl value) => WritePropertyAndString(EscapedName(utf8PropertyName), value.Utf8);

        public void WriteString(JsonEncodedTextImpl propertyName, string value)
        {
            if (value == null) { WriteNull(propertyName); return; }
            WritePropertyAndString(propertyName.Utf8, EscapedValue(value));
        }

        public void WriteString(string propertyName, string value)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            if (value == null) { WriteNull(propertyName); return; }
            byte[] name = EscapedName(propertyName);
            WritePropertyAndString(name, EscapedValue(value));
        }

        public void WriteString(ReadOnlySpan<char> propertyName, string value) => WriteString(new string(propertyName), value);

        public void WriteString(ReadOnlySpan<byte> utf8PropertyName, string value)
        {
            if (value == null) { WriteNull(utf8PropertyName); return; }
            byte[] name = EscapedName(utf8PropertyName);
            WritePropertyAndString(name, EscapedValue(value));
        }

        public void WriteString(JsonEncodedTextImpl propertyName, ReadOnlySpan<char> value) => WritePropertyAndString(propertyName.Utf8, EscapedValue(new string(value)));
        public void WriteString(string propertyName, ReadOnlySpan<char> value) { byte[] n = EscapedName(propertyName); WritePropertyAndString(n, EscapedValue(new string(value))); }
        public void WriteString(ReadOnlySpan<char> propertyName, ReadOnlySpan<char> value) => WriteString(new string(propertyName), value);
        public void WriteString(ReadOnlySpan<byte> utf8PropertyName, ReadOnlySpan<char> value) { byte[] n = EscapedName(utf8PropertyName); WritePropertyAndString(n, EscapedValue(new string(value))); }

        public void WriteString(JsonEncodedTextImpl propertyName, ReadOnlySpan<byte> utf8Value) => WritePropertyAndString(propertyName.Utf8, EscapedValue(utf8Value));
        public void WriteString(string propertyName, ReadOnlySpan<byte> utf8Value) { byte[] n = EscapedName(propertyName); WritePropertyAndString(n, EscapedValue(utf8Value)); }
        public void WriteString(ReadOnlySpan<char> propertyName, ReadOnlySpan<byte> utf8Value) => WriteString(new string(propertyName), utf8Value);
        public void WriteString(ReadOnlySpan<byte> utf8PropertyName, ReadOnlySpan<byte> utf8Value) { byte[] n = EscapedName(utf8PropertyName); WritePropertyAndString(n, EscapedValue(utf8Value)); }

        public void WriteString(JsonEncodedTextImpl propertyName, DateTime value) => WritePropertyAndString(propertyName.Utf8, Ascii(JsonWriterHelper.FormatDateTime(value)));
        public void WriteString(string propertyName, DateTime value) => WritePropertyAndString(EscapedName(propertyName), Ascii(JsonWriterHelper.FormatDateTime(value)));
        public void WriteString(ReadOnlySpan<char> propertyName, DateTime value) => WriteString(new string(propertyName), value);
        public void WriteString(ReadOnlySpan<byte> utf8PropertyName, DateTime value) => WritePropertyAndString(EscapedName(utf8PropertyName), Ascii(JsonWriterHelper.FormatDateTime(value)));

        public void WriteString(JsonEncodedTextImpl propertyName, DateTimeOffset value) => WritePropertyAndString(propertyName.Utf8, Ascii(JsonWriterHelper.FormatDateTimeOffset(value)));
        public void WriteString(string propertyName, DateTimeOffset value) => WritePropertyAndString(EscapedName(propertyName), Ascii(JsonWriterHelper.FormatDateTimeOffset(value)));
        public void WriteString(ReadOnlySpan<char> propertyName, DateTimeOffset value) => WriteString(new string(propertyName), value);
        public void WriteString(ReadOnlySpan<byte> utf8PropertyName, DateTimeOffset value) => WritePropertyAndString(EscapedName(utf8PropertyName), Ascii(JsonWriterHelper.FormatDateTimeOffset(value)));

        public void WriteString(JsonEncodedTextImpl propertyName, Guid value) => WritePropertyAndString(propertyName.Utf8, Ascii(value.ToString()));
        public void WriteString(string propertyName, Guid value) => WritePropertyAndString(EscapedName(propertyName), Ascii(value.ToString()));
        public void WriteString(ReadOnlySpan<char> propertyName, Guid value) => WriteString(new string(propertyName), value);
        public void WriteString(ReadOnlySpan<byte> utf8PropertyName, Guid value) => WritePropertyAndString(EscapedName(utf8PropertyName), Ascii(value.ToString()));

        public void WriteBase64String(JsonEncodedTextImpl propertyName, ReadOnlySpan<byte> bytes) => WritePropertyAndString(propertyName.Utf8, Base64Escaped(bytes));
        public void WriteBase64String(string propertyName, ReadOnlySpan<byte> bytes) { byte[] n = EscapedName(propertyName); WritePropertyAndString(n, Base64Escaped(bytes)); }
        public void WriteBase64String(ReadOnlySpan<char> propertyName, ReadOnlySpan<byte> bytes) => WriteBase64String(new string(propertyName), bytes);
        public void WriteBase64String(ReadOnlySpan<byte> utf8PropertyName, ReadOnlySpan<byte> bytes) { byte[] n = EscapedName(utf8PropertyName); WritePropertyAndString(n, Base64Escaped(bytes)); }

        private byte[] Base64Escaped(ReadOnlySpan<byte> bytes) =>
            JsonWriterHelper.ToUtf8Valid(JsonBase64.Encode(bytes.ToArray()));

        private byte[] EscapedValue(string value)
        {
            JsonWriterHelper.ValidateValue(value);
            return JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.Escape(value, _encoder));
        }

        private byte[] EscapedValue(ReadOnlySpan<byte> utf8Value)
        {
            byte[] bytes = utf8Value.ToArray();
            if (bytes.Length > JsonConstants.MaxUnescapedTokenSize) throw new ArgumentException(JsonSR.Format(JsonSR.ValueTooLarge, bytes.Length));
            return JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.EscapeUtf8(bytes, 0, bytes.Length, _encoder));
        }

        /// <summary>Serializer helper: "name": with a name that is already escaped UTF-8.</summary>
        internal void WritePropertyNameUtf8Escaped(byte[] escapedName) => WritePropertyNameEscaped(escapedName);

        // Dates as property names (dictionary keys) are written unescaped, as in .NET: their '+' stays as is.
        internal void WritePropertyName(DateTime value) => WritePropertyNameEscaped(Ascii(JsonWriterHelper.FormatDateTime(value)));
        internal void WritePropertyName(DateTimeOffset value) => WritePropertyNameEscaped(Ascii(JsonWriterHelper.FormatDateTimeOffset(value)));
    }
}
