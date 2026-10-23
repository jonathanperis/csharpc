// System.Text.Json DOM: JsonDocument, JsonElement (+ enumerators), JsonProperty, JsonDocumentOptions.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Document (release/11.0):
// the same metadata database (one 12-byte row per token, indexes are byte offsets into it), so
// property lookup (last duplicate wins), enumeration, raw text and WriteTo behave like .NET.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.JsonDocumentOptions")]
    public struct JsonDocumentOptionsImpl
    {
        internal const int DefaultMaxDepth = 64;
        private int _maxDepth;
        private JsonCommentHandling _commentHandling;
        private bool _disallowDuplicateProperties;

        public JsonCommentHandling CommentHandling
        {
            get => _commentHandling;
            set
            {
                if (value > JsonCommentHandling.Skip) throw new ArgumentOutOfRangeException("value", JsonSR.JsonDocumentDoesNotSupportComments);
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

        public bool AllowDuplicateProperties
        {
            get => !_disallowDuplicateProperties;
            set => _disallowDuplicateProperties = !value;
        }

        internal JsonReaderOptionsImpl GetReaderOptions()
        {
            JsonReaderOptionsImpl options = new JsonReaderOptionsImpl();
            options.AllowTrailingCommas = AllowTrailingCommas;
            options.CommentHandling = CommentHandling;
            options.MaxDepth = MaxDepth;
            return options;
        }
    }

    [Shadows("System.Text.Json.JsonDocument")]
    public sealed class JsonDocumentImpl : IDisposable
    {
        internal const int RowSize = 12;

        private byte[] _utf8Json;
        private int[] _db;          // 3 ints per row: location, sizeOrLength, numberOfRows | tokenType << 28
        private int _dbLength;      // in bytes (rows * 12), like MetadataDb.Length
        internal bool IsDisposable { get; }

        private JsonDocumentImpl(byte[] utf8Json, int[] db, int dbLength, bool isDisposable)
        {
            _utf8Json = utf8Json;
            _db = db;
            _dbLength = dbLength;
            IsDisposable = isDisposable;
        }

        public JsonElementImpl RootElement => new JsonElementImpl(this, 0);

        public void Dispose()
        {
            if (_utf8Json == null || !IsDisposable) return;
            _utf8Json = null;
            _db = null;
        }

        public void WriteTo(Utf8JsonWriterImpl writer)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            RootElement.WriteTo(writer);
        }

        // ---------------------------------------------------------------- rows

        private int Location(int index) => _db[index / 4];
        private int RawSize(int index) => _db[index / 4 + 1];
        private int SizeOrLength(int index) => _db[index / 4 + 1] & int.MaxValue;
        private bool HasComplexChildren(int index) => _db[index / 4 + 1] < 0;
        private bool IsUnknownSize(int index) => _db[index / 4 + 1] == -1;
        private int NumberOfRows(int index) => _db[index / 4 + 2] & 0x0FFFFFFF;
        private JsonTokenType RowTokenType(int index) => (JsonTokenType)((uint)_db[index / 4 + 2] >> 28);
        private bool IsSimpleValue(int index) => RowTokenType(index) >= JsonTokenType.PropertyName;

        private static void Append(ref int[] db, ref int length, JsonTokenType tokenType, int location, int sizeOrLength)
        {
            int i = length / 4;
            if (i + 3 > db.Length)
            {
                int[] grown = new int[db.Length * 2 + 3];
                Array.Copy(db, grown, db.Length);
                db = grown;
            }
            db[i] = location;
            db[i + 1] = sizeOrLength;
            db[i + 2] = (int)tokenType << 28;
            length += RowSize;
        }

        private static int FindOpenElement(int[] db, int length, JsonTokenType lookupType)
        {
            for (int i = length - RowSize; i >= 0; i -= RowSize)
            {
                if (db[i / 4 + 1] == -1 && (JsonTokenType)((uint)db[i / 4 + 2] >> 28) == lookupType) return i;
            }
            return -1;
        }

        private static void SetNumberOfRows(int[] db, int index, int numberOfRows) =>
            db[index / 4 + 2] = (db[index / 4 + 2] & unchecked((int)0xF0000000)) | numberOfRows;

        private static void SetHasComplexChildren(int[] db, int index) => db[index / 4 + 1] |= unchecked((int)0x80000000);

        // ---------------------------------------------------------------- element accessors

        internal JsonTokenType GetJsonTokenType(int index)
        {
            CheckNotDisposed();
            return RowTokenType(index);
        }

        internal bool ValueIsEscaped(int index, bool isPropertyName)
        {
            CheckNotDisposed();
            return HasComplexChildren(isPropertyName ? index - RowSize : index);
        }

        internal int GetArrayLength(int index)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.StartArray, RowTokenType(index));
            return SizeOrLength(index);
        }

        internal int GetPropertyCount(int index)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.StartObject, RowTokenType(index));
            return SizeOrLength(index);
        }

        internal JsonElementImpl GetArrayIndexElement(int currentIndex, int arrayIndex)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.StartArray, RowTokenType(currentIndex));
            int arrayLength = SizeOrLength(currentIndex);
            if ((uint)arrayIndex >= (uint)arrayLength) throw new IndexOutOfRangeException();
            if (!HasComplexChildren(currentIndex)) return new JsonElementImpl(this, currentIndex + ((arrayIndex + 1) * RowSize));
            int elementCount = 0;
            for (int objectOffset = currentIndex + RowSize; objectOffset < _dbLength; objectOffset += RowSize)
            {
                if (arrayIndex == elementCount) return new JsonElementImpl(this, objectOffset);
                if (!IsSimpleValue(objectOffset)) objectOffset += RowSize * NumberOfRows(objectOffset);
                elementCount++;
            }
            throw new IndexOutOfRangeException();
        }

        internal int GetEndIndex(int index, bool includeEndElement)
        {
            CheckNotDisposed();
            if (IsSimpleValue(index)) return index + RowSize;
            int endIndex = index + RowSize * NumberOfRows(index);
            if (includeEndElement) endIndex += RowSize;
            return endIndex;
        }

        /// <summary>(start, length) of an element's raw UTF-8 in the document.</summary>
        internal void GetRawValue(int index, bool includeQuotes, out int start, out int length)
        {
            CheckNotDisposed();
            if (IsSimpleValue(index))
            {
                if (includeQuotes && RowTokenType(index) == JsonTokenType.String)
                {
                    start = Location(index) - 1;
                    length = SizeOrLength(index) + 2;
                    return;
                }
                start = Location(index);
                length = SizeOrLength(index);
                return;
            }
            int endElementIdx = GetEndIndex(index, includeEndElement: false);
            start = Location(index);
            length = Location(endElementIdx) - start + SizeOrLength(endElementIdx);
        }

        internal ReadOnlySpan<byte> GetRawValueSpan(int index, bool includeQuotes)
        {
            GetRawValue(index, includeQuotes, out int start, out int length);
            return new ReadOnlySpan<byte>(_utf8Json, start, length);
        }

        internal byte[] Utf8 => _utf8Json;

        private void GetPropertyRawValue(int valueIndex, out int start, out int length)
        {
            CheckNotDisposed();
            start = Location(valueIndex - RowSize) - 1;
            int end;
            if (IsSimpleValue(valueIndex))
            {
                end = Location(valueIndex) + SizeOrLength(valueIndex);
                if (RowTokenType(valueIndex) == JsonTokenType.String) end++;
                length = end - start;
                return;
            }
            int endElementIdx = GetEndIndex(valueIndex, includeEndElement: false);
            end = Location(endElementIdx) + SizeOrLength(endElementIdx);
            length = end - start;
        }

        private ReadOnlySpan<byte> Segment(int index) => new ReadOnlySpan<byte>(_utf8Json, Location(index), SizeOrLength(index));

        internal string GetString(int index, JsonTokenType expectedType)
        {
            CheckNotDisposed();
            JsonTokenType tokenType = RowTokenType(index);
            if (tokenType == JsonTokenType.Null) return null;
            CheckExpectedType(expectedType, tokenType);
            ReadOnlySpan<byte> segment = Segment(index);
            return HasComplexChildren(index) ? JsonReaderHelper.GetUnescapedString(segment) : JsonReaderHelper.TranscodeHelper(segment);
        }

        internal bool TextEquals(int index, ReadOnlySpan<char> otherText, bool isPropertyName)
        {
            CheckNotDisposed();
            byte[] otherUtf8 = JsonUtf8.FromUtf16(new string(otherText), out int bad);
            if (otherUtf8 == null) return false;
            return TextEquals(index, otherUtf8, isPropertyName, shouldUnescape: true);
        }

        internal bool TextEquals(int index, ReadOnlySpan<byte> otherUtf8Text, bool isPropertyName, bool shouldUnescape)
        {
            CheckNotDisposed();
            int matchIndex = isPropertyName ? index - RowSize : index;
            CheckExpectedType(isPropertyName ? JsonTokenType.PropertyName : JsonTokenType.String, RowTokenType(matchIndex));
            ReadOnlySpan<byte> segment = Segment(matchIndex);
            if (otherUtf8Text.Length > segment.Length || (!shouldUnescape && otherUtf8Text.Length != segment.Length)) return false;
            if (HasComplexChildren(matchIndex) && shouldUnescape)
            {
                if (otherUtf8Text.Length < segment.Length / JsonConstants.MaxExpansionFactorWhileEscaping) return false;
                int idx = segment.IndexOf(JsonConstants.BackSlash);
                if (!otherUtf8Text.StartsWith(segment.Slice(0, idx))) return false;
                return JsonReaderHelper.UnescapeAndCompare(segment.Slice(idx), otherUtf8Text.Slice(idx));
            }
            return segment.SequenceEqual(otherUtf8Text);
        }

        internal string GetNameOfPropertyValue(int index) => GetString(index - RowSize, JsonTokenType.PropertyName);

        internal ReadOnlySpan<byte> GetPropertyNameRaw(int index)
        {
            CheckNotDisposed();
            return Segment(index - RowSize);
        }

        internal bool TryGetValue(int index, out byte[] value)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.String, RowTokenType(index));
            ReadOnlySpan<byte> segment = Segment(index);
            if (HasComplexChildren(index)) return JsonReaderHelper.TryGetUnescapedBase64Bytes(segment, out value);
            return JsonReaderHelper.TryDecodeBase64(segment, out value);
        }

        private ReadOnlySpan<byte> NumberSegment(int index)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.Number, RowTokenType(index));
            return Segment(index);
        }

        internal bool TryGetValue(int index, out sbyte value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out byte value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out short value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out ushort value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out int value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out uint value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out long value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out ulong value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out double value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out float value) => JsonNumberParser.TryParse(NumberSegment(index), out value);
        internal bool TryGetValue(int index, out decimal value) => JsonNumberParser.TryParse(NumberSegment(index), out value);

        internal bool TryGetValue(int index, out DateTime value)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.String, RowTokenType(index));
            return JsonReaderHelper.TryGetValue(Segment(index), HasComplexChildren(index), out value);
        }

        internal string GetRawValueAsString(int index)
        {
            GetRawValue(index, true, out int start, out int length);
            return JsonReaderHelper.TranscodeHelper(new ReadOnlySpan<byte>(_utf8Json, start, length));
        }

        internal string GetPropertyRawValueAsString(int valueIndex)
        {
            GetPropertyRawValue(valueIndex, out int start, out int length);
            return JsonReaderHelper.TranscodeHelper(new ReadOnlySpan<byte>(_utf8Json, start, length));
        }

        internal JsonElementImpl CloneElement(int index)
        {
            int endIndex = GetEndIndex(index, true);
            // CopySegment: rows rebased to the copied text.
            int length = endIndex - index;
            int[] newDb = new int[length / 4];
            Array.Copy(_db, index / 4, newDb, 0, length / 4);
            int locationOffset = newDb[0];
            if (RowTokenType(index) == JsonTokenType.String) locationOffset--;
            for (int i = 0; i < newDb.Length; i += 3) newDb[i] -= locationOffset;
            GetRawValue(index, true, out int start, out int rawLength);
            byte[] segmentCopy = JsonUtf8.Slice(_utf8Json, start, rawLength);
            JsonDocumentImpl newDocument = new JsonDocumentImpl(segmentCopy, newDb, length, isDisposable: false);
            return newDocument.RootElement;
        }

        // ---------------------------------------------------------------- WriteTo

        internal void WriteElementTo(int index, Utf8JsonWriterImpl writer)
        {
            CheckNotDisposed();
            switch (RowTokenType(index))
            {
                case JsonTokenType.StartObject:
                    writer.WriteStartObject();
                    WriteComplexElement(index, writer);
                    return;
                case JsonTokenType.StartArray:
                    writer.WriteStartArray();
                    WriteComplexElement(index, writer);
                    return;
                case JsonTokenType.String:
                    WriteString(index, writer);
                    return;
                case JsonTokenType.Number:
                    writer.WriteNumberValueRaw(_utf8Json, Location(index), SizeOrLength(index));
                    return;
                case JsonTokenType.True:
                    writer.WriteBooleanValue(true);
                    return;
                case JsonTokenType.False:
                    writer.WriteBooleanValue(false);
                    return;
                case JsonTokenType.Null:
                    writer.WriteNullValue();
                    return;
            }
        }

        private void WriteComplexElement(int index, Utf8JsonWriterImpl writer)
        {
            int endIndex = GetEndIndex(index, true);
            for (int i = index + RowSize; i < endIndex; i += RowSize)
            {
                switch (RowTokenType(i))
                {
                    case JsonTokenType.String: WriteString(i, writer); continue;
                    case JsonTokenType.Number: writer.WriteNumberValueRaw(_utf8Json, Location(i), SizeOrLength(i)); continue;
                    case JsonTokenType.True: writer.WriteBooleanValue(true); continue;
                    case JsonTokenType.False: writer.WriteBooleanValue(false); continue;
                    case JsonTokenType.Null: writer.WriteNullValue(); continue;
                    case JsonTokenType.StartObject: writer.WriteStartObject(); continue;
                    case JsonTokenType.EndObject: writer.WriteEndObject(); continue;
                    case JsonTokenType.StartArray: writer.WriteStartArray(); continue;
                    case JsonTokenType.EndArray: writer.WriteEndArray(); continue;
                    case JsonTokenType.PropertyName: WritePropertyNameRow(i, writer); continue;
                }
            }
        }

        private ReadOnlySpan<byte> UnescapeString(int index)
        {
            ReadOnlySpan<byte> text = Segment(index);
            if (!HasComplexChildren(index)) return text;
            return JsonReaderHelper.GetUnescaped(text);
        }

        internal void WritePropertyName(int index, Utf8JsonWriterImpl writer)
        {
            CheckNotDisposed();
            WritePropertyNameRow(index - RowSize, writer);
        }

        private void WritePropertyNameRow(int row, Utf8JsonWriterImpl writer) => writer.WritePropertyName(UnescapeString(row));

        private void WriteString(int row, Utf8JsonWriterImpl writer) => writer.WriteStringValue(UnescapeString(row));

        // ---------------------------------------------------------------- property lookup (last duplicate wins)

        internal bool TryGetNamedPropertyValue(int index, ReadOnlySpan<char> propertyName, out JsonElementImpl value)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.StartObject, RowTokenType(index));
            if (NumberOfRows(index) == 1)
            {
                value = default;
                return false;
            }
            byte[] utf8Name = JsonUtf8.FromUtf16(new string(propertyName), out int bad);
            if (utf8Name == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            int endIndex = checked(NumberOfRows(index) * RowSize + index);
            return TryGetNamedPropertyValue(index + RowSize, endIndex, utf8Name, out value);
        }

        internal bool TryGetNamedPropertyValue(int index, ReadOnlySpan<byte> propertyName, out JsonElementImpl value)
        {
            CheckNotDisposed();
            CheckExpectedType(JsonTokenType.StartObject, RowTokenType(index));
            if (NumberOfRows(index) == 1)
            {
                value = default;
                return false;
            }
            int endIndex = checked(NumberOfRows(index) * RowSize + index);
            return TryGetNamedPropertyValue(index + RowSize, endIndex, propertyName, out value);
        }

        private bool TryGetNamedPropertyValue(int startIndex, int endIndex, ReadOnlySpan<byte> propertyName, out JsonElementImpl value)
        {
            int index = endIndex - RowSize;
            while (index > startIndex)
            {
                if (IsSimpleValue(index)) index -= RowSize;
                else index -= RowSize * (NumberOfRows(index) + 1);

                ReadOnlySpan<byte> currentPropertyName = Segment(index);
                if (HasComplexChildren(index))
                {
                    if (currentPropertyName.Length > propertyName.Length)
                    {
                        int idx = currentPropertyName.IndexOf(JsonConstants.BackSlash);
                        if (propertyName.Length > idx && currentPropertyName.Slice(0, idx).SequenceEqual(propertyName.Slice(0, idx)))
                        {
                            byte[] unescaped = JsonReaderHelper.GetUnescaped(currentPropertyName.Slice(idx));
                            if (propertyName.Slice(idx).SequenceEqual(unescaped))
                            {
                                value = new JsonElementImpl(this, index + RowSize);
                                return true;
                            }
                        }
                    }
                }
                else if (currentPropertyName.SequenceEqual(propertyName))
                {
                    value = new JsonElementImpl(this, index + RowSize);
                    return true;
                }
                index -= RowSize;
            }
            value = default;
            return false;
        }

        // ---------------------------------------------------------------- parsing

        public static JsonDocumentImpl Parse(ReadOnlyMemory<byte> utf8Json, JsonDocumentOptionsImpl options = default) =>
            Parse(utf8Json.ToArray(), options.GetReaderOptions(), options.AllowDuplicateProperties);

        public static JsonDocumentImpl Parse(ReadOnlyMemory<char> json, JsonDocumentOptionsImpl options = default) =>
            Parse(new string(json.Span), options);

        public static JsonDocumentImpl Parse(string json, JsonDocumentOptionsImpl options = default)
        {
            if (json == null) throw new ArgumentNullException("json");
            byte[] utf8 = JsonUtf8.FromUtf16(json, out int bad);
            if (utf8 == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            return Parse(utf8, options.GetReaderOptions(), options.AllowDuplicateProperties);
        }

        public static JsonDocumentImpl Parse(Stream utf8Json, JsonDocumentOptionsImpl options = default)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            byte[] bytes = ReadToEnd(utf8Json);
            return Parse(bytes, options.GetReaderOptions(), options.AllowDuplicateProperties);
        }

        public static Task<JsonDocumentImpl> ParseAsync(Stream utf8Json, JsonDocumentOptionsImpl options = default, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Parse(utf8Json, options));
        }

        internal static byte[] ReadToEnd(Stream stream)
        {
            MemoryStream ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            // A UTF-8 BOM is skipped, as in .NET.
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                byte[] trimmed = new byte[bytes.Length - 3];
                Array.Copy(bytes, 3, trimmed, 0, trimmed.Length);
                bytes = trimmed;
            }
            return bytes;
        }

        internal static JsonDocumentImpl ParseValue(byte[] utf8Json, JsonDocumentOptionsImpl options) =>
            Parse(utf8Json, options.GetReaderOptions(), options.AllowDuplicateProperties, isDisposable: false);

        public static JsonDocumentImpl ParseValue(ref Utf8JsonReaderImpl reader)
        {
            TryParseValue(ref reader, out JsonDocumentImpl document, shouldThrow: true, isDisposable: true);
            return document;
        }

        public static bool TryParseValue(ref Utf8JsonReaderImpl reader, out JsonDocumentImpl document) =>
            TryParseValue(ref reader, out document, shouldThrow: false, isDisposable: true);

        internal static bool TryParseValue(ref Utf8JsonReaderImpl reader, out JsonDocumentImpl document, bool shouldThrow, bool isDisposable)
        {
            JsonReaderStateImpl state = reader.CurrentState;
            if (state.Options.CommentHandling == JsonCommentHandling.Allow)
                throw new ArgumentException(JsonSR.JsonDocumentDoesNotSupportComments, "reader");

            Utf8JsonReaderImpl restore = reader;
            ReadOnlySpan<byte> valueSpan = default;
            try
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.None:
                    case JsonTokenType.PropertyName:
                        if (!reader.Read())
                        {
                            if (shouldThrow) JsonThrowHelper.ThrowJsonReaderException(ref reader, ExceptionResource.ExpectedJsonTokens);
                            reader = restore;
                            document = null;
                            return false;
                        }
                        break;
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                    {
                        long startingOffset = reader.TokenStartIndex;
                        if (!reader.TrySkip())
                        {
                            if (shouldThrow) JsonThrowHelper.ThrowJsonReaderException(ref reader, ExceptionResource.ExpectedJsonTokens);
                            reader = restore;
                            document = null;
                            return false;
                        }
                        long totalLength = reader.BytesConsumed - startingOffset;
                        valueSpan = reader.OriginalSpan.Slice(checked((int)startingOffset), checked((int)totalLength));
                        break;
                    }
                    case JsonTokenType.False:
                    case JsonTokenType.True:
                    case JsonTokenType.Null:
                    case JsonTokenType.Number:
                        valueSpan = reader.ValueSpan;
                        break;
                    case JsonTokenType.String:
                    {
                        int payloadLength = reader.ValueSpan.Length + 2;
                        valueSpan = reader.OriginalSpan.Slice((int)reader.TokenStartIndex, payloadLength);
                        break;
                    }
                    default:
                    {
                        if (shouldThrow)
                        {
                            byte displayByte = reader.ValueSpan[0];
                            JsonThrowHelper.ThrowJsonReaderException(ref reader, ExceptionResource.ExpectedStartOfValueNotFound, displayByte);
                        }
                        reader = restore;
                        document = null;
                        return false;
                    }
                }
            }
            catch
            {
                reader = restore;
                throw;
            }
            document = Parse(valueSpan.ToArray(), state.Options, true, isDisposable);
            return true;
        }

        internal static JsonDocumentImpl Parse(byte[] utf8Json, JsonReaderOptionsImpl readerOptions, bool allowDuplicateProperties, bool isDisposable = true)
        {
            int[] db = new int[utf8Json.Length / 2 + 12];
            int dbLength = 0;
            ParseCore(utf8Json, readerOptions, ref db, ref dbLength);
            JsonDocumentImpl document = new JsonDocumentImpl(utf8Json, db, dbLength, isDisposable);
            if (!allowDuplicateProperties) ValidateNoDuplicateProperties(document);
            return document;
        }

        private static void ParseCore(byte[] utf8Json, JsonReaderOptionsImpl readerOptions, ref int[] db, ref int dbLength)
        {
            bool inArray = false;
            int arrayItemsOrPropertyCount = 0;
            int numberOfRowsForMembers = 0;
            int numberOfRowsForValues = 0;
            Stack<long> stack = new Stack<long>(); // (SizeOrLength << 32) | NumberOfRows

            Utf8JsonReaderImpl reader = new Utf8JsonReaderImpl(utf8Json, true, new JsonReaderStateImpl(readerOptions));
            while (reader.Read())
            {
                JsonTokenType tokenType = reader.TokenType;
                int tokenStart = (int)reader.TokenStartIndex;
                if (tokenType == JsonTokenType.StartObject)
                {
                    if (inArray) arrayItemsOrPropertyCount++;
                    numberOfRowsForValues++;
                    Append(ref db, ref dbLength, tokenType, tokenStart, -1);
                    stack.Push(((long)arrayItemsOrPropertyCount << 32) | (uint)(numberOfRowsForMembers + 1));
                    arrayItemsOrPropertyCount = 0;
                    numberOfRowsForMembers = 0;
                }
                else if (tokenType == JsonTokenType.EndObject)
                {
                    int rowIndex = FindOpenElement(db, dbLength, JsonTokenType.StartObject);
                    numberOfRowsForValues++;
                    numberOfRowsForMembers++;
                    db[rowIndex / 4 + 1] = arrayItemsOrPropertyCount;
                    int newRowIndex = dbLength;
                    Append(ref db, ref dbLength, tokenType, tokenStart, reader.ValueSpan.Length);
                    SetNumberOfRows(db, rowIndex, numberOfRowsForMembers);
                    SetNumberOfRows(db, newRowIndex, numberOfRowsForMembers);
                    long row = stack.Pop();
                    arrayItemsOrPropertyCount = (int)(row >> 32);
                    numberOfRowsForMembers += (int)(uint)row;
                }
                else if (tokenType == JsonTokenType.StartArray)
                {
                    if (inArray) arrayItemsOrPropertyCount++;
                    numberOfRowsForMembers++;
                    Append(ref db, ref dbLength, tokenType, tokenStart, -1);
                    stack.Push(((long)arrayItemsOrPropertyCount << 32) | (uint)(numberOfRowsForValues + 1));
                    arrayItemsOrPropertyCount = 0;
                    numberOfRowsForValues = 0;
                }
                else if (tokenType == JsonTokenType.EndArray)
                {
                    int rowIndex = FindOpenElement(db, dbLength, JsonTokenType.StartArray);
                    numberOfRowsForValues++;
                    numberOfRowsForMembers++;
                    db[rowIndex / 4 + 1] = arrayItemsOrPropertyCount;
                    SetNumberOfRows(db, rowIndex, numberOfRowsForValues);
                    if (arrayItemsOrPropertyCount + 1 != numberOfRowsForValues) SetHasComplexChildren(db, rowIndex);
                    int newRowIndex = dbLength;
                    Append(ref db, ref dbLength, tokenType, tokenStart, reader.ValueSpan.Length);
                    SetNumberOfRows(db, newRowIndex, numberOfRowsForValues);
                    long row = stack.Pop();
                    arrayItemsOrPropertyCount = (int)(row >> 32);
                    numberOfRowsForValues += (int)(uint)row;
                }
                else if (tokenType == JsonTokenType.PropertyName)
                {
                    numberOfRowsForValues++;
                    numberOfRowsForMembers++;
                    arrayItemsOrPropertyCount++;
                    Append(ref db, ref dbLength, tokenType, tokenStart + 1, reader.ValueSpan.Length);
                    if (reader.ValueIsEscaped) SetHasComplexChildren(db, dbLength - RowSize);
                }
                else
                {
                    numberOfRowsForValues++;
                    numberOfRowsForMembers++;
                    if (inArray) arrayItemsOrPropertyCount++;
                    if (tokenType == JsonTokenType.String)
                    {
                        Append(ref db, ref dbLength, tokenType, tokenStart + 1, reader.ValueSpan.Length);
                        if (reader.ValueIsEscaped) SetHasComplexChildren(db, dbLength - RowSize);
                    }
                    else
                    {
                        Append(ref db, ref dbLength, tokenType, tokenStart, reader.ValueSpan.Length);
                    }
                }
                inArray = reader.IsInArray;
            }
        }

        private static void ValidateNoDuplicateProperties(JsonDocumentImpl document) => ValidateNoDuplicates(document.RootElement);

        private static void ValidateNoDuplicates(JsonElementImpl element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    foreach (JsonPropertyImpl property in element.EnumerateObject()) ValidateNoDuplicates(property.Value);
                    HashSet<string> names = new HashSet<string>();
                    foreach (JsonPropertyImpl property in element.EnumerateObject())
                    {
                        if (!names.Add(property.Name))
                            throw new JsonExceptionImpl("Duplicate property '" + property.Name + "' encountered during deserialization.");
                    }
                    break;
                }
                case JsonValueKind.Array:
                    foreach (JsonElementImpl item in element.EnumerateArray()) ValidateNoDuplicates(item);
                    break;
            }
        }

        private void CheckNotDisposed()
        {
            if (_utf8Json == null) throw new ObjectDisposedException("JsonDocument");
        }

        private static void CheckExpectedType(JsonTokenType expected, JsonTokenType actual)
        {
            if (expected != actual)
                JsonThrowHelper.ThrowJsonElementWrongTypeException(JsonReaderHelper.ToValueKind(expected), JsonReaderHelper.ToValueKind(actual));
        }
    }

    [Shadows("System.Text.Json.JsonElement")]
    public readonly struct JsonElementImpl
    {
        internal readonly JsonDocumentImpl _parent;
        internal readonly int _idx;

        internal JsonElementImpl(JsonDocumentImpl parent, int idx)
        {
            _parent = parent;
            _idx = idx;
        }

        private JsonTokenType TokenType => _parent == null ? JsonTokenType.None : _parent.GetJsonTokenType(_idx);

        public JsonValueKind ValueKind => JsonReaderHelper.ToValueKind(TokenType);

        public JsonElementImpl this[int index]
        {
            get
            {
                CheckValidInstance();
                return _parent.GetArrayIndexElement(_idx, index);
            }
        }

        public int GetArrayLength()
        {
            CheckValidInstance();
            return _parent.GetArrayLength(_idx);
        }

        public int GetPropertyCount()
        {
            CheckValidInstance();
            return _parent.GetPropertyCount(_idx);
        }

        public JsonElementImpl GetProperty(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            if (TryGetProperty(propertyName, out JsonElementImpl property)) return property;
            throw new KeyNotFoundException("The given key '" + propertyName + "' was not present in the dictionary.");
        }

        public JsonElementImpl GetProperty(ReadOnlySpan<char> propertyName)
        {
            if (TryGetProperty(propertyName, out JsonElementImpl property)) return property;
            throw new KeyNotFoundException("The given key '" + new string(propertyName) + "' was not present in the dictionary.");
        }

        public JsonElementImpl GetProperty(ReadOnlySpan<byte> utf8PropertyName)
        {
            if (TryGetProperty(utf8PropertyName, out JsonElementImpl property)) return property;
            throw new KeyNotFoundException("The given key '" + Encoding.UTF8.GetString(utf8PropertyName.ToArray()) + "' was not present in the dictionary.");
        }

        public bool TryGetProperty(string propertyName, out JsonElementImpl value)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            return TryGetProperty(propertyName.AsSpan(), out value);
        }

        public bool TryGetProperty(ReadOnlySpan<char> propertyName, out JsonElementImpl value)
        {
            CheckValidInstance();
            return _parent.TryGetNamedPropertyValue(_idx, propertyName, out value);
        }

        public bool TryGetProperty(ReadOnlySpan<byte> utf8PropertyName, out JsonElementImpl value)
        {
            CheckValidInstance();
            return _parent.TryGetNamedPropertyValue(_idx, utf8PropertyName, out value);
        }

        public bool GetBoolean()
        {
            JsonTokenType type = TokenType;
            if (type == JsonTokenType.True) return true;
            if (type == JsonTokenType.False) return false;
            JsonThrowHelper.ThrowJsonElementWrongTypeException("Boolean", JsonReaderHelper.ToValueKind(type));
            return false;
        }

        public string GetString()
        {
            CheckValidInstance();
            return _parent.GetString(_idx, JsonTokenType.String);
        }

        public bool TryGetBytesFromBase64(out byte[] value)
        {
            CheckValidInstance();
            return _parent.TryGetValue(_idx, out value);
        }

        public byte[] GetBytesFromBase64()
        {
            if (!TryGetBytesFromBase64(out byte[] value)) throw new FormatException();
            return value;
        }

        public bool TryGetSByte(out sbyte value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public sbyte GetSByte() { if (!TryGetSByte(out sbyte value)) throw new FormatException(); return value; }
        public bool TryGetByte(out byte value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public byte GetByte() { if (!TryGetByte(out byte value)) throw new FormatException(); return value; }
        public bool TryGetInt16(out short value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public short GetInt16() { if (!TryGetInt16(out short value)) throw new FormatException(); return value; }
        public bool TryGetUInt16(out ushort value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public ushort GetUInt16() { if (!TryGetUInt16(out ushort value)) throw new FormatException(); return value; }
        public bool TryGetInt32(out int value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public int GetInt32() { if (!TryGetInt32(out int value)) throw new FormatException(); return value; }
        public bool TryGetUInt32(out uint value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public uint GetUInt32() { if (!TryGetUInt32(out uint value)) throw new FormatException(); return value; }
        public bool TryGetInt64(out long value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public long GetInt64() { if (!TryGetInt64(out long value)) throw new FormatException(); return value; }
        public bool TryGetUInt64(out ulong value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public ulong GetUInt64() { if (!TryGetUInt64(out ulong value)) throw new FormatException(); return value; }
        public bool TryGetDouble(out double value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public double GetDouble() { if (!TryGetDouble(out double value)) throw new FormatException(); return value; }
        public bool TryGetSingle(out float value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public float GetSingle() { if (!TryGetSingle(out float value)) throw new FormatException(); return value; }
        public bool TryGetDecimal(out decimal value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public decimal GetDecimal() { if (!TryGetDecimal(out decimal value)) throw new FormatException(); return value; }
        public bool TryGetDateTime(out DateTime value) { CheckValidInstance(); return _parent.TryGetValue(_idx, out value); }
        public DateTime GetDateTime() { if (!TryGetDateTime(out DateTime value)) throw new FormatException(); return value; }

        internal string GetPropertyName()
        {
            CheckValidInstance();
            return _parent.GetNameOfPropertyValue(_idx);
        }

        internal ReadOnlySpan<byte> GetPropertyNameRaw()
        {
            CheckValidInstance();
            return _parent.GetPropertyNameRaw(_idx);
        }

        public string GetRawText()
        {
            CheckValidInstance();
            return _parent.GetRawValueAsString(_idx);
        }

        internal ReadOnlySpan<byte> GetRawValue()
        {
            CheckValidInstance();
            return _parent.GetRawValueSpan(_idx, includeQuotes: true);
        }

        internal string GetPropertyRawText()
        {
            CheckValidInstance();
            return _parent.GetPropertyRawValueAsString(_idx);
        }

        internal bool ValueIsEscaped
        {
            get
            {
                CheckValidInstance();
                return _parent.ValueIsEscaped(_idx, isPropertyName: false);
            }
        }

        internal ReadOnlySpan<byte> ValueSpan
        {
            get
            {
                CheckValidInstance();
                return _parent.GetRawValueSpan(_idx, includeQuotes: false);
            }
        }

        public static bool DeepEquals(JsonElementImpl element1, JsonElementImpl element2)
        {
            element1.CheckValidInstance();
            element2.CheckValidInstance();
            JsonValueKind kind = element1.ValueKind;
            if (kind != element2.ValueKind) return false;
            switch (kind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.False:
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.Number:
                    return JsonNumberCompare.AreEqual(element1.GetRawValue(), element2.GetRawValue());
                case JsonValueKind.String:
                    return element1.GetString() == element2.GetString();
                case JsonValueKind.Array:
                {
                    if (element1.GetArrayLength() != element2.GetArrayLength()) return false;
                    ArrayEnumerator arrayEnumerator2 = element2.EnumerateArray();
                    foreach (JsonElementImpl e1 in element1.EnumerateArray())
                    {
                        arrayEnumerator2.MoveNext();
                        if (!DeepEquals(e1, arrayEnumerator2.Current)) return false;
                    }
                    return true;
                }
                default:
                {
                    int count = element1.GetPropertyCount();
                    if (count != element2.GetPropertyCount()) return false;
                    // Properties are matched by name, in order for equal names (as .NET's queue per name):
                    // a chain of element2's property indexes per name, consumed from the front.
                    JsonElementImpl[] values2 = new JsonElementImpl[count];
                    int[] next = new int[count];
                    Dictionary<string, int> head = new Dictionary<string, int>();
                    Dictionary<string, int> tail = new Dictionary<string, int>();
                    int index = 0;
                    foreach (JsonPropertyImpl p in element2.EnumerateObject())
                    {
                        values2[index] = p.Value;
                        next[index] = -1;
                        if (tail.TryGetValue(p.Name, out int last)) next[last] = index;
                        else head[p.Name] = index;
                        tail[p.Name] = index;
                        index++;
                    }
                    foreach (JsonPropertyImpl p in element1.EnumerateObject())
                    {
                        if (!head.TryGetValue(p.Name, out int first) || first < 0) return false;
                        head[p.Name] = next[first];
                        if (!DeepEquals(p.Value, values2[first])) return false;
                    }
                    return true;
                }
            }
        }

        public bool ValueEquals(string text)
        {
            if (TokenType == JsonTokenType.Null) return text == null;
            return TextEqualsHelper(text.AsSpan(), isPropertyName: false);
        }

        public bool ValueEquals(ReadOnlySpan<byte> utf8Text)
        {
            if (TokenType == JsonTokenType.Null) return false;
            return TextEqualsHelper(utf8Text, isPropertyName: false, shouldUnescape: true);
        }

        public bool ValueEquals(ReadOnlySpan<char> text)
        {
            if (TokenType == JsonTokenType.Null) return false;
            return TextEqualsHelper(text, isPropertyName: false);
        }

        internal bool TextEqualsHelper(ReadOnlySpan<byte> utf8Text, bool isPropertyName, bool shouldUnescape)
        {
            CheckValidInstance();
            return _parent.TextEquals(_idx, utf8Text, isPropertyName, shouldUnescape);
        }

        internal bool TextEqualsHelper(ReadOnlySpan<char> text, bool isPropertyName)
        {
            CheckValidInstance();
            return _parent.TextEquals(_idx, text, isPropertyName);
        }

        internal bool ValueIsEscapedHelper(bool isPropertyName)
        {
            CheckValidInstance();
            return _parent.ValueIsEscaped(_idx, isPropertyName);
        }

        public void WriteTo(Utf8JsonWriterImpl writer)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            CheckValidInstance();
            _parent.WriteElementTo(_idx, writer);
        }

        internal void WritePropertyNameTo(Utf8JsonWriterImpl writer)
        {
            CheckValidInstance();
            _parent.WritePropertyName(_idx, writer);
        }

        public ArrayEnumerator EnumerateArray()
        {
            CheckValidInstance();
            JsonTokenType tokenType = TokenType;
            if (tokenType != JsonTokenType.StartArray)
                JsonThrowHelper.ThrowJsonElementWrongTypeException(JsonValueKind.Array, JsonReaderHelper.ToValueKind(tokenType));
            return new ArrayEnumerator(this);
        }

        public ObjectEnumerator EnumerateObject()
        {
            CheckValidInstance();
            JsonTokenType tokenType = TokenType;
            if (tokenType != JsonTokenType.StartObject)
                JsonThrowHelper.ThrowJsonElementWrongTypeException(JsonValueKind.Object, JsonReaderHelper.ToValueKind(tokenType));
            return new ObjectEnumerator(this);
        }

        public override string ToString()
        {
            switch (TokenType)
            {
                case JsonTokenType.None:
                case JsonTokenType.Null:
                    return string.Empty;
                case JsonTokenType.True:
                    return "True";
                case JsonTokenType.False:
                    return "False";
                case JsonTokenType.Number:
                case JsonTokenType.StartArray:
                case JsonTokenType.StartObject:
                    return _parent.GetRawValueAsString(_idx);
                case JsonTokenType.String:
                    return GetString();
                default:
                    return string.Empty;
            }
        }

        public JsonElementImpl Clone()
        {
            CheckValidInstance();
            if (!_parent.IsDisposable) return this;
            return _parent.CloneElement(_idx);
        }

        private void CheckValidInstance()
        {
            if (_parent == null) throw new InvalidOperationException("Operation is not valid due to the current state of the object.");
        }

        public static JsonElementImpl ParseValue(ref Utf8JsonReaderImpl reader)
        {
            JsonDocumentImpl.TryParseValue(ref reader, out JsonDocumentImpl document, shouldThrow: true, isDisposable: false);
            return document.RootElement;
        }

        public static bool TryParseValue(ref Utf8JsonReaderImpl reader, out JsonElementImpl? element)
        {
            bool ret = JsonDocumentImpl.TryParseValue(ref reader, out JsonDocumentImpl document, shouldThrow: false, isDisposable: false);
            element = document == null ? (JsonElementImpl?)null : document.RootElement;
            return ret;
        }

        public static JsonElementImpl Parse(string json, JsonDocumentOptionsImpl options = default)
        {
            if (json == null) throw new ArgumentNullException("json");
            byte[] utf8 = JsonUtf8.FromUtf16(json, out int bad);
            if (utf8 == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            return JsonDocumentImpl.ParseValue(utf8, options).RootElement;
        }

        public static JsonElementImpl Parse(ReadOnlySpan<char> json, JsonDocumentOptionsImpl options = default) => Parse(new string(json), options);

        public static JsonElementImpl Parse(ReadOnlySpan<byte> utf8Json, JsonDocumentOptionsImpl options = default) =>
            JsonDocumentImpl.ParseValue(utf8Json.ToArray(), options).RootElement;

        [Shadows("System.Text.Json.JsonElement+ArrayEnumerator")]
        public struct ArrayEnumerator : IEnumerable<JsonElement>, IEnumerator<JsonElement>
        {
            private readonly JsonElementImpl _target;
            private int _curIdx;
            private readonly int _endIdxOrVersion;

            internal ArrayEnumerator(JsonElementImpl target, int currentIndex = -1)
            {
                _target = target;
                _curIdx = currentIndex;
                _endIdxOrVersion = target._parent.GetEndIndex(_target._idx, includeEndElement: false);
            }

            public JsonElementImpl Current => _curIdx < 0 ? default : new JsonElementImpl(_target._parent, _curIdx);

            public ArrayEnumerator GetEnumerator()
            {
                ArrayEnumerator ator = this;
                ator._curIdx = -1;
                return ator;
            }

            IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)(object)GetEnumerator();

            IEnumerator<JsonElement> IEnumerable<JsonElement>.GetEnumerator() => (IEnumerator<JsonElement>)(object)GetEnumerator();

            JsonElement IEnumerator<JsonElement>.Current => (JsonElement)(object)Current;

            public void Dispose() => _curIdx = _endIdxOrVersion;

            public void Reset() => _curIdx = -1;

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_curIdx >= _endIdxOrVersion) return false;
                if (_curIdx < 0) _curIdx = _target._idx + JsonDocumentImpl.RowSize;
                else _curIdx = _target._parent.GetEndIndex(_curIdx, includeEndElement: true);
                return _curIdx < _endIdxOrVersion;
            }
        }

        [Shadows("System.Text.Json.JsonElement+ObjectEnumerator")]
        public struct ObjectEnumerator : IEnumerable<JsonProperty>, IEnumerator<JsonProperty>
        {
            private readonly JsonElementImpl _target;
            private int _curIdx;
            private readonly int _endIdxOrVersion;

            internal ObjectEnumerator(JsonElementImpl target, int currentIndex = -1)
            {
                _target = target;
                _curIdx = currentIndex;
                _endIdxOrVersion = target._parent.GetEndIndex(_target._idx, includeEndElement: false);
            }

            public JsonPropertyImpl Current => _curIdx < 0 ? default : new JsonPropertyImpl(new JsonElementImpl(_target._parent, _curIdx));

            public ObjectEnumerator GetEnumerator()
            {
                ObjectEnumerator ator = this;
                ator._curIdx = -1;
                return ator;
            }

            IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)(object)GetEnumerator();

            IEnumerator<JsonProperty> IEnumerable<JsonProperty>.GetEnumerator() => (IEnumerator<JsonProperty>)(object)GetEnumerator();

            JsonProperty IEnumerator<JsonProperty>.Current => (JsonProperty)(object)Current;

            public void Dispose() => _curIdx = _endIdxOrVersion;

            public void Reset() => _curIdx = -1;

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_curIdx >= _endIdxOrVersion) return false;
                if (_curIdx < 0) _curIdx = _target._idx + JsonDocumentImpl.RowSize;
                else _curIdx = _target._parent.GetEndIndex(_curIdx, includeEndElement: true);
                _curIdx += JsonDocumentImpl.RowSize;
                return _curIdx < _endIdxOrVersion;
            }
        }
    }

    [Shadows("System.Text.Json.JsonProperty")]
    public readonly struct JsonPropertyImpl
    {
        public JsonElementImpl Value { get; }

        internal JsonPropertyImpl(JsonElementImpl value)
        {
            Value = value;
        }

        public string Name => Value.GetPropertyName();

        public bool NameEquals(string text) => NameEquals(text.AsSpan());

        public bool NameEquals(ReadOnlySpan<byte> utf8Text) => Value.TextEqualsHelper(utf8Text, isPropertyName: true, shouldUnescape: true);

        public bool NameEquals(ReadOnlySpan<char> text) => Value.TextEqualsHelper(text, isPropertyName: true);

        internal bool NameIsEscaped => Value.ValueIsEscapedHelper(isPropertyName: true);

        internal ReadOnlySpan<byte> NameSpan => Value.GetPropertyNameRaw();

        public void WriteTo(Utf8JsonWriterImpl writer)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            Value.WritePropertyNameTo(writer);
            Value.WriteTo(writer);
        }

        public override string ToString() => Value.GetPropertyRawText();
    }

    internal static class JsonNumberCompare
    {
        /// <summary>Mathematical equality of two JSON number literals (JsonHelpers.AreEqualJsonNumbers).</summary>
        public static bool AreEqual(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            Normalize(left, out bool ln, out string ld, out long le);
            Normalize(right, out bool rn, out string rd, out long re);
            return ln == rn && ld == rd && le == re;
        }

        // value = (negative ? -1 : 1) * 0.digits * 10^exponent with digits stripped of leading/trailing zeros.
        private static void Normalize(ReadOnlySpan<byte> span, out bool negative, out string digits, out long exponent)
        {
            int i = 0;
            negative = false;
            if (i < span.Length && span[i] == '-') { negative = true; i++; }
            StringBuilder sb = new StringBuilder();
            long pointPos = 0;
            bool seenPoint = false;
            for (; i < span.Length; i++)
            {
                byte b = span[i];
                if (b == '.') { seenPoint = true; continue; }
                if (b == 'e' || b == 'E') { i++; break; }
                sb.Append((char)b);
                if (!seenPoint) pointPos++;
            }
            long exp = 0;
            bool expNeg = false;
            if (i < span.Length && (span[i] == '-' || span[i] == '+')) { expNeg = span[i] == '-'; i++; }
            for (; i < span.Length; i++) exp = Math.Min(exp * 10 + (span[i] - '0'), 1_000_000_000_000L);
            if (expNeg) exp = -exp;
            string all = sb.ToString();
            int lead = 0;
            while (lead < all.Length && all[lead] == '0') lead++;
            int trail = all.Length;
            while (trail > lead && all[trail - 1] == '0') trail--;
            if (lead == trail)
            {
                negative = false;
                digits = "0";
                exponent = 0;
                return;
            }
            digits = all.Substring(lead, trail - lead);
            exponent = pointPos - lead + exp;
        }
    }
}
