// System.Text.Json reader: Utf8JsonReader (single-segment input).
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Reader (release/11.0):
// Utf8JsonReader.cs and Utf8JsonReader.TryGet.cs with the ReadOnlySequence (multi-segment) paths
// removed; the token state machine, line/byte positions and exception messages are unchanged.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.Utf8JsonReader")]
    public ref partial struct Utf8JsonReaderImpl
    {
        private ReadOnlySpan<byte> _buffer;

        private readonly bool _isFinalBlock;

        private long _lineNumber;
        private long _bytePositionInLine;

        // bytes consumed in the current segment (not token)
        private int _consumed;
        private bool _inObject;
        private bool _isNotPrimitive;
        private JsonTokenType _tokenType;
        private JsonTokenType _previousTokenType;
        private JsonReaderOptionsImpl _readerOptions;
        private JsonBitStack _bitStack;

        private long _totalConsumed;
        private bool _trailingCommaBeforeComment;

        private readonly bool IsLastSpan => _isFinalBlock;

        internal readonly int ValueLength => ValueSpan.Length;

        internal readonly ReadOnlySpan<byte> OriginalSpan => _buffer;

        internal readonly bool AllowMultipleValues => _readerOptions.AllowMultipleValues;

        public ReadOnlySpan<byte> ValueSpan { get; private set; }

        public readonly long BytesConsumed
        {
            get
            {

                return _totalConsumed + _consumed;
            }
        }

        public long TokenStartIndex { get; private set; }

        public readonly int CurrentDepth
        {
            get
            {
                int readerDepth = _bitStack.CurrentDepth;
                if (TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                {

                    readerDepth--;
                }
                return readerDepth;
            }
        }

        internal readonly bool IsInArray => !_inObject;

        public readonly JsonTokenType TokenType => _tokenType;

        public bool HasValueSequence => false;

        public bool ValueIsEscaped { get; private set; }

        public readonly bool IsFinalBlock => _isFinalBlock;



        public readonly JsonReaderStateImpl CurrentState => new JsonReaderStateImpl
        (
            lineNumber: _lineNumber,
            bytePositionInLine: _bytePositionInLine,
            inObject: _inObject,
            isNotPrimitive: _isNotPrimitive,
            valueIsEscaped: ValueIsEscaped,
            trailingCommaBeforeComment: _trailingCommaBeforeComment,
            tokenType: _tokenType,
            previousTokenType: _previousTokenType,
            readerOptions: _readerOptions,
            bitStack: _bitStack
        );

        public Utf8JsonReaderImpl(ReadOnlySpan<byte> jsonData, bool isFinalBlock, JsonReaderStateImpl state)
        {
            _buffer = jsonData;

            _isFinalBlock = isFinalBlock;

            _lineNumber = state._lineNumber;
            _bytePositionInLine = state._bytePositionInLine;
            _inObject = state._inObject;
            _isNotPrimitive = state._isNotPrimitive;
            ValueIsEscaped = state._valueIsEscaped;
            _trailingCommaBeforeComment = state._trailingCommaBeforeComment;
            _tokenType = state._tokenType;
            _previousTokenType = state._previousTokenType;
            _readerOptions = state._readerOptions;
            if (_readerOptions.MaxDepth == 0)
            {
                _readerOptions.MaxDepth = JsonConstants.DefaultMaxDepth;  // If max depth is not set, revert to the default depth.
            }
            _bitStack = state._bitStack;

            _consumed = 0;
            TokenStartIndex = 0;
            _totalConsumed = 0;
            

            ValueSpan = ReadOnlySpan<byte>.Empty;
        }

        public Utf8JsonReaderImpl(ReadOnlySpan<byte> jsonData, JsonReaderOptionsImpl options = default)
            : this(jsonData, isFinalBlock: true, new JsonReaderStateImpl(options))
        {
        }

        public bool Read()
        {
            bool retVal = ReadSingleSegment();

            if (!retVal)
            {
                if (_isFinalBlock && TokenType is JsonTokenType.None && !_readerOptions.AllowMultipleValues)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedJsonTokens);
                }
            }
            return retVal;
        }

        public void Skip()
        {
            if (!_isFinalBlock)
            {
                JsonThrowHelper.ThrowInvalidOperationException_CannotSkipOnPartial();
            }

            SkipHelper();
        }
        private void SkipHelper()
        {

            if (TokenType is JsonTokenType.PropertyName)
            {
                bool result = Read();
                // Since _isFinalBlock == true here, and the JSON token is not a primitive value or comment.
                // Read() is guaranteed to return true OR throw for invalid/incomplete data.

            }

            if (TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                int depth = CurrentDepth;
                do
                {
                    bool result = Read();
                    // Since _isFinalBlock == true here, and the JSON token is not a primitive value or comment.
                    // Read() is guaranteed to return true OR throw for invalid/incomplete data.

                }
                while (depth < CurrentDepth);
            }
        }

        public bool TrySkip()
        {
            if (_isFinalBlock)
            {
                SkipHelper();
                return true;
            }

            Utf8JsonReaderImpl restore = this;
            bool success = TrySkipPartial(targetDepth: CurrentDepth);
            if (!success)
            {
                // Roll back the reader if it contains partial data.
                this = restore;
            }

            return success;
        }

        internal bool TrySkipPartial(int targetDepth)
        {

            if (targetDepth == CurrentDepth)
            {
                // This is the first call to TrySkipHelper.
                if (TokenType is JsonTokenType.PropertyName)
                {
                    // Skip any property name tokens preceding the value.
                    if (!Read())
                    {
                        return false;
                    }
                }

                if (TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
                {
                    // The next value is not an object or array, so there is nothing to skip.
                    return true;
                }
            }

            // Start or resume iterating through the JSON object or array.
            do
            {
                if (!Read())
                {
                    return false;
                }
            }
            while (targetDepth < CurrentDepth);

            return true;
        }

        public readonly bool ValueTextEquals(ReadOnlySpan<byte> utf8Text)
        {
            if (!IsTokenTypeString(TokenType))
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedStringComparison(TokenType);
            }

            return TextEqualsHelper(utf8Text);
        }

        public readonly bool ValueTextEquals(string text)
        {
            return ValueTextEquals(text.AsSpan());
        }
        private readonly bool TextEqualsHelper(ReadOnlySpan<byte> otherUtf8Text)
        {

            if (ValueIsEscaped)
            {
                return UnescapeAndCompare(otherUtf8Text);
            }

            return otherUtf8Text.SequenceEqual(ValueSpan);
        }

        public readonly bool ValueTextEquals(ReadOnlySpan<char> text)
        {
            if (!IsTokenTypeString(TokenType))
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedStringComparison(TokenType);
            }

            if (MatchNotPossible(text.Length))
            {
                return false;
            }

            byte[] otherUtf8Text = JsonUtf8.FromUtf16(new string(text), out int bad);
            if (otherUtf8Text == null)
            {
                return false;
            }
            return TextEqualsHelper(otherUtf8Text);
        }

        private readonly bool UnescapeAndCompare(ReadOnlySpan<byte> other)
        {

            ReadOnlySpan<byte> localSpan = ValueSpan;

            if (localSpan.Length < other.Length || localSpan.Length / JsonConstants.MaxExpansionFactorWhileEscaping > other.Length)
            {
                return false;
            }

            int idx = localSpan.IndexOf(JsonConstants.BackSlash);

            if (!other.StartsWith(localSpan.Slice(0, idx)))
            {
                return false;
            }

            return JsonReaderHelper.UnescapeAndCompare(localSpan.Slice(idx), other.Slice(idx));
        }



        // Returns true if the TokenType is a primitive string "value", i.e. PropertyName or String
        // Otherwise, return false.
        private static bool IsTokenTypeString(JsonTokenType tokenType)
        {
            return tokenType is JsonTokenType.PropertyName or JsonTokenType.String;
        }
        private readonly bool MatchNotPossible(int charTextLength)
        {

            int sourceLength = ValueSpan.Length;

            // Transcoding from UTF-16 to UTF-8 will change the length by somwhere between 1x and 3x.
            // Unescaping the token value will at most shrink its length by 6x.
            // There is no point incurring the transcoding/unescaping/comparing cost if:
            // - The token value is smaller than charTextLength
            // - The token value needs to be transcoded AND unescaped and it is more than 6x larger than charTextLength
            //      - For an ASCII UTF-16 characters, transcoding = 1x, escaping = 6x => 6x factor
            //      - For non-ASCII UTF-16 characters within the BMP, transcoding = 2-3x, but they are represented as a single escaped hex value, \uXXXX => 6x factor
            //      - For non-ASCII UTF-16 characters outside of the BMP, transcoding = 4x, but the surrogate pair (2 characters) are represented by 16 bytes \uXXXX\uXXXX => 6x factor
            // - The token value needs to be transcoded, but NOT escaped and it is more than 3x larger than charTextLength
            //      - For an ASCII UTF-16 characters, transcoding = 1x,
            //      - For non-ASCII UTF-16 characters within the BMP, transcoding = 2-3x,
            //      - For non-ASCII UTF-16 characters outside of the BMP, transcoding = 2x, (surrogate pairs - 2 characters transcode to 4 UTF-8 bytes)

            if (sourceLength < charTextLength
                || sourceLength / (ValueIsEscaped ? JsonConstants.MaxExpansionFactorWhileEscaping : JsonConstants.MaxExpansionFactorWhileTranscoding) > charTextLength)
            {
                return true;
            }
            return false;
        }


        private void StartObject()
        {
            if (_bitStack.CurrentDepth >= _readerOptions.MaxDepth)
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ObjectDepthTooLarge);

            _bitStack.PushTrue();

            ValueSpan = _buffer.Slice(_consumed, 1);
            _consumed++;
            _bytePositionInLine++;
            _tokenType = JsonTokenType.StartObject;
            _inObject = true;
        }

        private void EndObject()
        {
            if (!_inObject || _bitStack.CurrentDepth <= 0)
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.MismatchedObjectArray, JsonConstants.CloseBrace);

            if (_trailingCommaBeforeComment)
            {
                if (!_readerOptions.AllowTrailingCommas)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeObjectEnd);
                }
                _trailingCommaBeforeComment = false;
            }

            _tokenType = JsonTokenType.EndObject;
            ValueSpan = _buffer.Slice(_consumed, 1);

            UpdateBitStackOnEndToken();
        }

        private void StartArray()
        {
            if (_bitStack.CurrentDepth >= _readerOptions.MaxDepth)
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ArrayDepthTooLarge);

            _bitStack.PushFalse();

            ValueSpan = _buffer.Slice(_consumed, 1);
            _consumed++;
            _bytePositionInLine++;
            _tokenType = JsonTokenType.StartArray;
            _inObject = false;
        }

        private void EndArray()
        {
            if (_inObject || _bitStack.CurrentDepth <= 0)
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.MismatchedObjectArray, JsonConstants.CloseBracket);

            if (_trailingCommaBeforeComment)
            {
                if (!_readerOptions.AllowTrailingCommas)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeArrayEnd);
                }
                _trailingCommaBeforeComment = false;
            }

            _tokenType = JsonTokenType.EndArray;
            ValueSpan = _buffer.Slice(_consumed, 1);

            UpdateBitStackOnEndToken();
        }
        private void UpdateBitStackOnEndToken()
        {
            _consumed++;
            _bytePositionInLine++;
            _inObject = _bitStack.Pop();
        }

        private bool ReadSingleSegment()
        {
            bool retVal = false;
            ValueSpan = default;
            ValueIsEscaped = false;

            if (!HasMoreData())
            {
                goto Done;
            }

            byte first = _buffer[_consumed];

            // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
            // SkipWhiteSpace only skips the whitespace characters as defined by JSON RFC 8259 section 2.
            // We do not validate if 'first' is an invalid JSON byte here (such as control characters).
            // Those cases are captured in ConsumeNextToken and ConsumeValue.
            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpace();
                if (!HasMoreData())
                {
                    goto Done;
                }
                first = _buffer[_consumed];
            }

            TokenStartIndex = _consumed;

            if (_tokenType == JsonTokenType.None)
            {
                goto ReadFirstToken;
            }

            if (first == JsonConstants.Slash)
            {
                retVal = ConsumeNextTokenOrRollback(first);
                goto Done;
            }

            if (_tokenType == JsonTokenType.StartObject)
            {
                if (first == JsonConstants.CloseBrace)
                {
                    EndObject();
                }
                else
                {
                    if (first != JsonConstants.Quote)
                    {
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }

                    int prevConsumed = _consumed;
                    long prevPosition = _bytePositionInLine;
                    long prevLineNumber = _lineNumber;
                    retVal = ConsumePropertyName();
                    if (!retVal)
                    {
                        // roll back potential changes
                        _consumed = prevConsumed;
                        _tokenType = JsonTokenType.StartObject;
                        _bytePositionInLine = prevPosition;
                        _lineNumber = prevLineNumber;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.StartArray)
            {
                if (first == JsonConstants.CloseBracket)
                {
                    EndArray();
                }
                else
                {
                    retVal = ConsumeValue(first);
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.PropertyName)
            {
                retVal = ConsumeValue(first);
                goto Done;
            }
            else
            {
                retVal = ConsumeNextTokenOrRollback(first);
                goto Done;
            }

            retVal = true;

        Done:
            return retVal;

        ReadFirstToken:
            retVal = ReadFirstToken(first);
            goto Done;
        }
        private bool HasMoreData()
        {
            if (_consumed >= (uint)_buffer.Length)
            {
                if (_isNotPrimitive && IsLastSpan)
                {
                    if (_bitStack.CurrentDepth != 0)
                    {
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ZeroDepthAtEnd);
                    }

                    if (_readerOptions.CommentHandling == JsonCommentHandling.Allow && _tokenType == JsonTokenType.Comment)
                    {
                        return false;
                    }

                    if (_tokenType is not JsonTokenType.EndArray and not JsonTokenType.EndObject)
                    {
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJsonNonPrimitive);
                    }
                }
                return false;
            }
            return true;
        }

        // Unlike the parameter-less overload of HasMoreData, if there is no more data when this method is called, we know the JSON input is invalid.
        // This is because, this method is only called after a ',' (i.e. we expect a value/property name) or after
        // a property name, which means it must be followed by a value.
        private bool HasMoreData(ExceptionResource resource)
        {
            if (_consumed >= (uint)_buffer.Length)
            {
                if (IsLastSpan)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, resource);
                }
                return false;
            }
            return true;
        }

        private bool ReadFirstToken(byte first)
        {
            if (first == JsonConstants.OpenBrace)
            {
                _bitStack.SetFirstBit();
                _tokenType = JsonTokenType.StartObject;
                ValueSpan = _buffer.Slice(_consumed, 1);
                _consumed++;
                _bytePositionInLine++;
                _inObject = true;
                _isNotPrimitive = true;
            }
            else if (first == JsonConstants.OpenBracket)
            {
                _bitStack.ResetFirstBit();
                _tokenType = JsonTokenType.StartArray;
                ValueSpan = _buffer.Slice(_consumed, 1);
                _consumed++;
                _bytePositionInLine++;
                _isNotPrimitive = true;
            }
            else
            {
                // Create local copy to avoid bounds checks.
                ReadOnlySpan<byte> localBuffer = _buffer;

                if (JsonHelpers.IsDigit(first) || first == '-')
                {
                    if (!TryGetNumber(localBuffer.Slice(_consumed), out int numberOfBytes))
                    {
                        return false;
                    }
                    _tokenType = JsonTokenType.Number;
                    _consumed += numberOfBytes;
                    _bytePositionInLine += numberOfBytes;
                }
                else if (!ConsumeValue(first))
                {
                    return false;
                }

                _isNotPrimitive = _tokenType is JsonTokenType.StartObject or JsonTokenType.StartArray;
                // Intentionally fall out of the if-block to return true
            }
            return true;
        }

        private void SkipWhiteSpace()
        {
            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer;

            for (; _consumed < localBuffer.Length; _consumed++)
            {
                byte val = localBuffer[_consumed];

                // JSON RFC 8259 section 2 says only these 4 characters count, not all of the Unicode definitions of whitespace.
                if (val is not JsonConstants.Space and
                           not JsonConstants.CarriageReturn and
                           not JsonConstants.LineFeed and
                           not JsonConstants.Tab)
                {
                    break;
                }

                if (val == JsonConstants.LineFeed)
                {
                    _lineNumber++;
                    _bytePositionInLine = 0;
                }
                else
                {
                    _bytePositionInLine++;
                }
            }
        }

        private bool ConsumeValue(byte marker)
        {
            while (true)
            {

                _trailingCommaBeforeComment = false;

                if (marker == JsonConstants.Quote)
                {
                    return ConsumeString();
                }
                else if (marker == JsonConstants.OpenBrace)
                {
                    StartObject();
                }
                else if (marker == JsonConstants.OpenBracket)
                {
                    StartArray();
                }
                else if (JsonHelpers.IsDigit(marker) || marker == '-')
                {
                    return ConsumeNumber();
                }
                else if (marker == 'f')
                {
                    return ConsumeLiteral(JsonConstants.FalseValue, JsonTokenType.False);
                }
                else if (marker == 't')
                {
                    return ConsumeLiteral(JsonConstants.TrueValue, JsonTokenType.True);
                }
                else if (marker == 'n')
                {
                    return ConsumeLiteral(JsonConstants.NullValue, JsonTokenType.Null);
                }
                else
                {
                    switch (_readerOptions.CommentHandling)
                    {
                        case JsonCommentHandling.Disallow:
                            break;
                        case JsonCommentHandling.Allow:
                            if (marker == JsonConstants.Slash)
                            {
                                return ConsumeComment();
                            }
                            break;
                        default:

                            if (marker == JsonConstants.Slash)
                            {
                                if (SkipComment())
                                {
                                    if (_consumed >= (uint)_buffer.Length)
                                    {
                                        if (_isNotPrimitive && IsLastSpan && _tokenType != JsonTokenType.EndArray && _tokenType != JsonTokenType.EndObject)
                                        {
                                            JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJsonNonPrimitive);
                                        }
                                        return false;
                                    }

                                    marker = _buffer[_consumed];

                                    // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                                    if (marker <= JsonConstants.Space)
                                    {
                                        SkipWhiteSpace();
                                        if (!HasMoreData())
                                        {
                                            return false;
                                        }
                                        marker = _buffer[_consumed];
                                    }

                                    TokenStartIndex = _consumed;

                                    // Skip comments and consume the actual JSON value.
                                    continue;
                                }
                                return false;
                            }
                            break;
                    }
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, marker);
                }
                break;
            }
            return true;
        }

        // Consumes 'null', or 'true', or 'false'
        private bool ConsumeLiteral(ReadOnlySpan<byte> literal, JsonTokenType tokenType)
        {
            ReadOnlySpan<byte> span = _buffer.Slice(_consumed);

            if (!span.StartsWith(literal))
            {
                return CheckLiteral(span, literal);
            }

            ValueSpan = span.Slice(0, literal.Length);
            _tokenType = tokenType;
            _consumed += literal.Length;
            _bytePositionInLine += literal.Length;
            return true;
        }

        private bool CheckLiteral(ReadOnlySpan<byte> span, ReadOnlySpan<byte> literal)
        {

            int indexOfFirstMismatch = 0;

            for (int i = 1; i < literal.Length; i++)
            {
                if (span.Length > i)
                {
                    if (span[i] != literal[i])
                    {
                        _bytePositionInLine += i;
                        ThrowInvalidLiteral(span);
                    }
                }
                else
                {
                    indexOfFirstMismatch = i;
                    break;
                }
            }

            if (IsLastSpan)
            {
                _bytePositionInLine += indexOfFirstMismatch;
                ThrowInvalidLiteral(span);
            }
            return false;
        }

        private void ThrowInvalidLiteral(ReadOnlySpan<byte> span)
        {
            byte firstByte = span[0];

            ExceptionResource resource;
            switch (firstByte)
            {
                case (byte)'t':
                    resource = ExceptionResource.ExpectedTrue;
                    break;
                case (byte)'f':
                    resource = ExceptionResource.ExpectedFalse;
                    break;
                default:

                    resource = ExceptionResource.ExpectedNull;
                    break;
            }
            JsonThrowHelper.ThrowJsonReaderException(ref this, resource, bytes: span);
        }

        private bool ConsumeNumber()
        {
            if (!TryGetNumber(_buffer.Slice(_consumed), out int consumed))
            {
                return false;
            }

            _tokenType = JsonTokenType.Number;
            _consumed += consumed;
            _bytePositionInLine += consumed;

            if (_consumed >= (uint)_buffer.Length)
            {

                // If there is no more data, and the JSON is not a single value, throw.
                if (_isNotPrimitive)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, _buffer[_consumed - 1]);
                }
            }

            // If there is more data and the JSON is not a single value, assert that there is an end of number delimiter.
            // Else, if either the JSON is a single value XOR if there is no more data, don't assert anything since there won't always be an end of number delimiter.

            return true;
        }

        private bool ConsumePropertyName()
        {
            _trailingCommaBeforeComment = false;

            if (!ConsumeString())
            {
                return false;
            }

            if (!HasMoreData(ExceptionResource.ExpectedValueAfterPropertyNameNotFound))
            {
                return false;
            }

            byte first = _buffer[_consumed];

            // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
            // We do not validate if 'first' is an invalid JSON byte here (such as control characters).
            // Those cases are captured below where we only accept ':'.
            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpace();
                if (!HasMoreData(ExceptionResource.ExpectedValueAfterPropertyNameNotFound))
                {
                    return false;
                }
                first = _buffer[_consumed];
            }

            // The next character must be a key / value separator. Validate and skip.
            if (first != JsonConstants.KeyValueSeparator)
            {
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedSeparatorAfterPropertyNameNotFound, first);
            }

            _consumed++;
            _bytePositionInLine++;
            _tokenType = JsonTokenType.PropertyName;
            return true;
        }

        private bool ConsumeString()
        {

            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer.Slice(_consumed + 1);

            // Vectorized search for either quote, backslash, or any control character.
            // If the first found byte is a quote, we have reached an end of string, and
            // can avoid validation.
            // Otherwise, in the uncommon case, iterate one character at a time and validate.
            int idx = JsonReaderHelper.IndexOfQuoteOrAnyControlOrBackSlash(localBuffer);

            if (idx >= 0)
            {
                byte foundByte = localBuffer[idx];
                if (foundByte == JsonConstants.Quote)
                {
                    _bytePositionInLine += idx + 2; // Add 2 for the start and end quotes.
                    ValueSpan = localBuffer.Slice(0, idx);
                    ValueIsEscaped = false;
                    _tokenType = JsonTokenType.String;
                    _consumed += idx + 2;
                    return true;
                }
                else
                {
                    return ConsumeStringAndValidate(localBuffer, idx);
                }
            }
            else
            {
                if (IsLastSpan)
                {
                    _bytePositionInLine += localBuffer.Length + 1;  // Account for the start quote
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                }
                return false;
            }
        }

        // Found a backslash or control characters which are considered invalid within a string.
        // Search through the rest of the string one byte at a time.
        // https://tools.ietf.org/html/rfc8259#section-7
        private bool ConsumeStringAndValidate(ReadOnlySpan<byte> data, int idx)
        {

            long prevLineBytePosition = _bytePositionInLine;
            long prevLineNumber = _lineNumber;

            _bytePositionInLine += idx + 1; // Add 1 for the first quote

            bool nextCharEscaped = false;
            for (; idx < data.Length; idx++)
            {
                byte currentByte = data[idx];
                if (currentByte == JsonConstants.Quote)
                {
                    if (!nextCharEscaped)
                    {
                        goto Done;
                    }
                    nextCharEscaped = false;
                }
                else if (currentByte == JsonConstants.BackSlash)
                {
                    nextCharEscaped = !nextCharEscaped;
                }
                else if (nextCharEscaped)
                {
                    int index = JsonConstants.EscapableChars.IndexOf(currentByte);
                    if (index == -1)
                    {
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterAfterEscapeWithinString, currentByte);
                    }

                    if (currentByte == 'u')
                    {
                        // Expecting 4 hex digits to follow the escaped 'u'
                        _bytePositionInLine++;  // move past the 'u'
                        if (ValidateHexDigits(data, idx + 1))
                        {
                            idx += 4;   // Skip the 4 hex digits, the for loop accounts for idx incrementing past the 'u'
                        }
                        else
                        {
                            // We found less than 4 hex digits. Check if there is more data to follow, otherwise throw.
                            idx = data.Length;
                            break;
                        }

                    }
                    nextCharEscaped = false;
                }
                else if (currentByte < JsonConstants.Space)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterWithinString, currentByte);
                }

                _bytePositionInLine++;
            }

            if (idx >= data.Length)
            {
                if (IsLastSpan)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                }
                _lineNumber = prevLineNumber;
                _bytePositionInLine = prevLineBytePosition;
                return false;
            }

        Done:
            _bytePositionInLine++;  // Add 1 for the end quote
            ValueSpan = data.Slice(0, idx);
            ValueIsEscaped = true;
            _tokenType = JsonTokenType.String;
            _consumed += idx + 2;
            return true;
        }

        private bool ValidateHexDigits(ReadOnlySpan<byte> data, int idx)
        {
            for (int j = idx; j < data.Length; j++)
            {
                byte nextByte = data[j];
                if (!JsonReaderHelper.IsHexDigit(nextByte))
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidHexCharacterWithinString, nextByte);
                }
                if (j - idx >= 3)
                {
                    return true;
                }
                _bytePositionInLine++;
            }

            return false;
        }

        // https://tools.ietf.org/html/rfc7159#section-6
        private bool TryGetNumber(ReadOnlySpan<byte> data, out int consumed)
        {
            // TODO: https://github.com/dotnet/runtime/issues/27837

            consumed = 0;
            int i = 0;

            ConsumeNumberResult signResult = ConsumeNegativeSign(ref data, ref i);
            if (signResult == ConsumeNumberResult.NeedMoreData)
            {
                return false;
            }

            byte nextByte = data[i];

            if (nextByte == '0')
            {
                ConsumeNumberResult result = ConsumeZero(ref data, ref i);
                if (result == ConsumeNumberResult.NeedMoreData)
                {
                    return false;
                }
                if (result == ConsumeNumberResult.Success)
                {
                    goto Done;
                }

                nextByte = data[i];
            }
            else
            {
                i++;
                ConsumeNumberResult result = ConsumeIntegerDigits(ref data, ref i);
                if (result == ConsumeNumberResult.NeedMoreData)
                {
                    return false;
                }
                if (result == ConsumeNumberResult.Success)
                {
                    goto Done;
                }

                nextByte = data[i];
                if (nextByte is not ((byte)'.' or (byte)'E' or (byte)'e'))
                {
                    _bytePositionInLine += i;
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
                }
            }

            if (nextByte == '.')
            {
                i++;
                ConsumeNumberResult result = ConsumeDecimalDigits(ref data, ref i);
                if (result == ConsumeNumberResult.NeedMoreData)
                {
                    return false;
                }
                if (result == ConsumeNumberResult.Success)
                {
                    goto Done;
                }

                nextByte = data[i];
                if (nextByte is not ((byte)'E' or (byte)'e'))
                {
                    _bytePositionInLine += i;
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitEValueNotFound, nextByte);
                }
            }

            i++;

            signResult = ConsumeSign(ref data, ref i);
            if (signResult == ConsumeNumberResult.NeedMoreData)
            {
                return false;
            }

            i++;
            ConsumeNumberResult resultExponent = ConsumeIntegerDigits(ref data, ref i);
            if (resultExponent == ConsumeNumberResult.NeedMoreData)
            {
                return false;
            }
            if (resultExponent == ConsumeNumberResult.Success)
            {
                goto Done;
            }

            _bytePositionInLine += i;
            JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, data[i]);

        Done:
            ValueSpan = data.Slice(0, i);
            consumed = i;
            return true;
        }

        private ConsumeNumberResult ConsumeNegativeSign(ref ReadOnlySpan<byte> data, scoped ref int i)
        {
            byte nextByte = data[i];

            if (nextByte == '-')
            {
                i++;
                if (i >= data.Length)
                {
                    if (IsLastSpan)
                    {
                        _bytePositionInLine += i;
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                    }
                    return ConsumeNumberResult.NeedMoreData;
                }

                nextByte = data[i];
                if (!JsonHelpers.IsDigit(nextByte))
                {
                    _bytePositionInLine += i;
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundAfterSign, nextByte);
                }
            }
            return ConsumeNumberResult.OperationIncomplete;
        }

        private ConsumeNumberResult ConsumeZero(ref ReadOnlySpan<byte> data, scoped ref int i)
        {

            i++;
            byte nextByte;
            if (i < data.Length)
            {
                nextByte = data[i];
                if (JsonConstants.Delimiters.Contains(nextByte))
                {
                    return ConsumeNumberResult.Success;
                }
            }
            else
            {
                if (IsLastSpan)
                {
                    // A payload containing a single value: "0" is valid
                    // If we are dealing with multi-value JSON,
                    // ConsumeNumber will validate that we have a delimiter following the "0".
                    return ConsumeNumberResult.Success;
                }
                else
                {
                    return ConsumeNumberResult.NeedMoreData;
                }
            }
            nextByte = data[i];
            if (nextByte is not ((byte)'.' or (byte)'E' or (byte)'e'))
            {
                _bytePositionInLine += i;
                JsonThrowHelper.ThrowJsonReaderException(ref this,
                    JsonHelpers.IsInRangeInclusive(nextByte, '0', '9') ? ExceptionResource.InvalidLeadingZeroInNumber : ExceptionResource.ExpectedEndOfDigitNotFound,
                    nextByte);
            }

            return ConsumeNumberResult.OperationIncomplete;
        }

        private ConsumeNumberResult ConsumeIntegerDigits(ref ReadOnlySpan<byte> data, scoped ref int i)
        {
            byte nextByte = default;
            for (; i < data.Length; i++)
            {
                nextByte = data[i];
                if (!JsonHelpers.IsDigit(nextByte))
                {
                    break;
                }
            }
            if (i >= data.Length)
            {
                if (IsLastSpan)
                {
                    // A payload containing a single value of integers (e.g. "12") is valid
                    // If we are dealing with multi-value JSON,
                    // ConsumeNumber will validate that we have a delimiter following the integer.
                    return ConsumeNumberResult.Success;
                }
                else
                {
                    return ConsumeNumberResult.NeedMoreData;
                }
            }
            if (JsonConstants.Delimiters.Contains(nextByte))
            {
                return ConsumeNumberResult.Success;
            }

            return ConsumeNumberResult.OperationIncomplete;
        }

        private ConsumeNumberResult ConsumeDecimalDigits(ref ReadOnlySpan<byte> data, scoped ref int i)
        {
            if (i >= data.Length)
            {
                if (IsLastSpan)
                {
                    _bytePositionInLine += i;
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                }
                return ConsumeNumberResult.NeedMoreData;
            }
            byte nextByte = data[i];
            if (!JsonHelpers.IsDigit(nextByte))
            {
                _bytePositionInLine += i;
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundAfterDecimal, nextByte);
            }
            i++;

            return ConsumeIntegerDigits(ref data, ref i);
        }

        private ConsumeNumberResult ConsumeSign(ref ReadOnlySpan<byte> data, scoped ref int i)
        {
            if (i >= data.Length)
            {
                if (IsLastSpan)
                {
                    _bytePositionInLine += i;
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                }
                return ConsumeNumberResult.NeedMoreData;
            }

            byte nextByte = data[i];
            if (nextByte is (byte)'+' or (byte)'-')
            {
                i++;
                if (i >= data.Length)
                {
                    if (IsLastSpan)
                    {
                        _bytePositionInLine += i;
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                    }
                    return ConsumeNumberResult.NeedMoreData;
                }
                nextByte = data[i];
            }

            if (!JsonHelpers.IsDigit(nextByte))
            {
                _bytePositionInLine += i;
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundAfterSign, nextByte);
            }

            return ConsumeNumberResult.OperationIncomplete;
        }

        private bool ConsumeNextTokenOrRollback(byte marker)
        {
            int prevConsumed = _consumed;
            long prevPosition = _bytePositionInLine;
            long prevLineNumber = _lineNumber;
            JsonTokenType prevTokenType = _tokenType;
            bool prevTrailingCommaBeforeComment = _trailingCommaBeforeComment;
            ConsumeTokenResult result = ConsumeNextToken(marker);
            if (result == ConsumeTokenResult.Success)
            {
                return true;
            }
            if (result == ConsumeTokenResult.NotEnoughDataRollBackState)
            {
                _consumed = prevConsumed;
                _tokenType = prevTokenType;
                _bytePositionInLine = prevPosition;
                _lineNumber = prevLineNumber;
                _trailingCommaBeforeComment = prevTrailingCommaBeforeComment;
            }
            return false;
        }

        private ConsumeTokenResult ConsumeNextToken(byte marker)
        {
            if (_readerOptions.CommentHandling != JsonCommentHandling.Disallow)
            {
                if (_readerOptions.CommentHandling == JsonCommentHandling.Allow)
                {
                    if (marker == JsonConstants.Slash)
                    {
                        return ConsumeComment() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                    }
                    if (_tokenType == JsonTokenType.Comment)
                    {
                        return ConsumeNextTokenFromLastNonCommentToken();
                    }
                }
                else
                {

                    return ConsumeNextTokenUntilAfterAllCommentsAreSkipped(marker);
                }
            }

            if (_bitStack.CurrentDepth == 0)
            {
                if (_readerOptions.AllowMultipleValues)
                {
                    return ReadFirstToken(marker) ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }

                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, marker);
            }

            if (marker == JsonConstants.ListSeparator)
            {
                _consumed++;
                _bytePositionInLine++;

                if (_consumed >= (uint)_buffer.Length)
                {
                    if (IsLastSpan)
                    {
                        _consumed--;
                        _bytePositionInLine--;
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                    }
                    return ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                byte first = _buffer[_consumed];

                // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                    {
                        return ConsumeTokenResult.NotEnoughDataRollBackState;
                    }
                    first = _buffer[_consumed];
                }

                TokenStartIndex = _consumed;

                if (_readerOptions.CommentHandling == JsonCommentHandling.Allow && first == JsonConstants.Slash)
                {
                    _trailingCommaBeforeComment = true;
                    return ConsumeComment() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }

                if (_inObject)
                {
                    if (first != JsonConstants.Quote)
                    {
                        if (first == JsonConstants.CloseBrace)
                        {
                            if (_readerOptions.AllowTrailingCommas)
                            {
                                EndObject();
                                return ConsumeTokenResult.Success;
                            }
                            JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeObjectEnd);
                        }
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }
                    return ConsumePropertyName() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                else
                {
                    if (first == JsonConstants.CloseBracket)
                    {
                        if (_readerOptions.AllowTrailingCommas)
                        {
                            EndArray();
                            return ConsumeTokenResult.Success;
                        }
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeArrayEnd);
                    }
                    return ConsumeValue(first) ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
            }
            else if (marker == JsonConstants.CloseBrace)
            {
                EndObject();
            }
            else if (marker == JsonConstants.CloseBracket)
            {
                EndArray();
            }
            else
            {
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.FoundInvalidCharacter, marker);
            }
            return ConsumeTokenResult.Success;
        }

        private ConsumeTokenResult ConsumeNextTokenFromLastNonCommentToken()
        {

            if (JsonReaderHelper.IsTokenTypePrimitive(_previousTokenType))
            {
                _tokenType = _inObject ? JsonTokenType.StartObject : JsonTokenType.StartArray;
            }
            else
            {
                _tokenType = _previousTokenType;
            }

            if (!HasMoreData())
            {
                goto RollBack;
            }

            byte first = _buffer[_consumed];

            // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpace();
                if (!HasMoreData())
                {
                    goto RollBack;
                }
                first = _buffer[_consumed];
            }

            if (_bitStack.CurrentDepth == 0 && _tokenType != JsonTokenType.None)
            {
                if (_readerOptions.AllowMultipleValues)
                {
                    return ReadFirstToken(first) ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }

                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, first);
            }

            TokenStartIndex = _consumed;

            if (first == JsonConstants.ListSeparator)
            {
                // A comma without some JSON value preceding it is invalid
                if (_previousTokenType <= JsonTokenType.StartObject || _previousTokenType == JsonTokenType.StartArray || _trailingCommaBeforeComment)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueAfterComment, first);
                }

                _consumed++;
                _bytePositionInLine++;

                if (_consumed >= (uint)_buffer.Length)
                {
                    if (IsLastSpan)
                    {
                        _consumed--;
                        _bytePositionInLine--;
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                    }
                    goto RollBack;
                }
                first = _buffer[_consumed];

                // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                    {
                        goto RollBack;
                    }
                    first = _buffer[_consumed];
                }

                TokenStartIndex = _consumed;

                if (first == JsonConstants.Slash)
                {
                    _trailingCommaBeforeComment = true;
                    if (ConsumeComment())
                    {
                        goto Done;
                    }
                    else
                    {
                        goto RollBack;
                    }
                }

                if (_inObject)
                {
                    if (first != JsonConstants.Quote)
                    {
                        if (first == JsonConstants.CloseBrace)
                        {
                            if (_readerOptions.AllowTrailingCommas)
                            {
                                EndObject();
                                goto Done;
                            }
                            JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeObjectEnd);
                        }

                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }
                    if (ConsumePropertyName())
                    {
                        goto Done;
                    }
                    else
                    {
                        goto RollBack;
                    }
                }
                else
                {
                    if (first == JsonConstants.CloseBracket)
                    {
                        if (_readerOptions.AllowTrailingCommas)
                        {
                            EndArray();
                            goto Done;
                        }
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeArrayEnd);
                    }

                    if (ConsumeValue(first))
                    {
                        goto Done;
                    }
                    else
                    {
                        goto RollBack;
                    }
                }
            }
            else if (first == JsonConstants.CloseBrace)
            {
                EndObject();
            }
            else if (first == JsonConstants.CloseBracket)
            {
                EndArray();
            }
            else if (_tokenType == JsonTokenType.None)
            {
                if (ReadFirstToken(first))
                {
                    goto Done;
                }
                else
                {
                    goto RollBack;
                }
            }
            else if (_tokenType == JsonTokenType.StartObject)
            {

                if (first != JsonConstants.Quote)
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                }

                int prevConsumed = _consumed;
                long prevPosition = _bytePositionInLine;
                long prevLineNumber = _lineNumber;
                if (!ConsumePropertyName())
                {
                    // roll back potential changes
                    _consumed = prevConsumed;
                    _tokenType = JsonTokenType.StartObject;
                    _bytePositionInLine = prevPosition;
                    _lineNumber = prevLineNumber;
                    goto RollBack;
                }
                goto Done;
            }
            else if (_tokenType == JsonTokenType.StartArray)
            {

                if (!ConsumeValue(first))
                {
                    goto RollBack;
                }
                goto Done;
            }
            else if (_tokenType == JsonTokenType.PropertyName)
            {
                if (!ConsumeValue(first))
                {
                    goto RollBack;
                }
                goto Done;
            }
            else
            {

                if (_inObject)
                {

                    if (first != JsonConstants.Quote)
                    {
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }

                    if (ConsumePropertyName())
                    {
                        goto Done;
                    }
                    else
                    {
                        goto RollBack;
                    }
                }
                else
                {

                    if (ConsumeValue(first))
                    {
                        goto Done;
                    }
                    else
                    {
                        goto RollBack;
                    }
                }
            }

        Done:
            return ConsumeTokenResult.Success;

        RollBack:
            return ConsumeTokenResult.NotEnoughDataRollBackState;
        }

        private bool SkipAllComments(scoped ref byte marker)
        {
            while (marker == JsonConstants.Slash)
            {
                if (SkipComment())
                {
                    if (!HasMoreData())
                    {
                        goto IncompleteNoRollback;
                    }

                    marker = _buffer[_consumed];

                    // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                    if (marker <= JsonConstants.Space)
                    {
                        SkipWhiteSpace();
                        if (!HasMoreData())
                        {
                            goto IncompleteNoRollback;
                        }
                        marker = _buffer[_consumed];
                    }
                }
                else
                {
                    goto IncompleteNoRollback;
                }
            }
            return true;

        IncompleteNoRollback:
            return false;
        }

        private bool SkipAllComments(scoped ref byte marker, ExceptionResource resource)
        {
            while (marker == JsonConstants.Slash)
            {
                if (SkipComment())
                {
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(resource))
                    {
                        goto IncompleteRollback;
                    }

                    marker = _buffer[_consumed];

                    // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                    if (marker <= JsonConstants.Space)
                    {
                        SkipWhiteSpace();
                        // The next character must be a start of a property name or value.
                        if (!HasMoreData(resource))
                        {
                            goto IncompleteRollback;
                        }
                        marker = _buffer[_consumed];
                    }
                }
                else
                {
                    goto IncompleteRollback;
                }
            }
            return true;

        IncompleteRollback:
            return false;
        }

        private ConsumeTokenResult ConsumeNextTokenUntilAfterAllCommentsAreSkipped(byte marker)
        {
            if (!SkipAllComments(ref marker))
            {
                goto IncompleteNoRollback;
            }

            TokenStartIndex = _consumed;

            if (_tokenType == JsonTokenType.StartObject)
            {
                if (marker == JsonConstants.CloseBrace)
                {
                    EndObject();
                }
                else
                {
                    if (marker != JsonConstants.Quote)
                    {
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, marker);
                    }

                    int prevConsumed = _consumed;
                    long prevPosition = _bytePositionInLine;
                    long prevLineNumber = _lineNumber;
                    if (!ConsumePropertyName())
                    {
                        // roll back potential changes
                        _consumed = prevConsumed;
                        _tokenType = JsonTokenType.StartObject;
                        _bytePositionInLine = prevPosition;
                        _lineNumber = prevLineNumber;
                        goto IncompleteNoRollback;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.StartArray)
            {
                if (marker == JsonConstants.CloseBracket)
                {
                    EndArray();
                }
                else
                {
                    if (!ConsumeValue(marker))
                    {
                        goto IncompleteNoRollback;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.PropertyName)
            {
                if (!ConsumeValue(marker))
                {
                    goto IncompleteNoRollback;
                }
                goto Done;
            }
            else if (_bitStack.CurrentDepth == 0)
            {
                if (_readerOptions.AllowMultipleValues)
                {
                    return ReadFirstToken(marker) ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }

                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, marker);
            }
            else if (marker == JsonConstants.ListSeparator)
            {
                _consumed++;
                _bytePositionInLine++;

                if (_consumed >= (uint)_buffer.Length)
                {
                    if (IsLastSpan)
                    {
                        _consumed--;
                        _bytePositionInLine--;
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                    }
                    return ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                marker = _buffer[_consumed];

                // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                if (marker <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                    {
                        return ConsumeTokenResult.NotEnoughDataRollBackState;
                    }
                    marker = _buffer[_consumed];
                }

                if (!SkipAllComments(ref marker, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                {
                    goto IncompleteRollback;
                }

                TokenStartIndex = _consumed;

                if (_inObject)
                {
                    if (marker != JsonConstants.Quote)
                    {
                        if (marker == JsonConstants.CloseBrace)
                        {
                            if (_readerOptions.AllowTrailingCommas)
                            {
                                EndObject();
                                goto Done;
                            }
                            JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeObjectEnd);
                        }

                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, marker);
                    }
                    return ConsumePropertyName() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                else
                {
                    if (marker == JsonConstants.CloseBracket)
                    {
                        if (_readerOptions.AllowTrailingCommas)
                        {
                            EndArray();
                            goto Done;
                        }
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.TrailingCommaNotAllowedBeforeArrayEnd);
                    }

                    return ConsumeValue(marker) ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
            }
            else if (marker == JsonConstants.CloseBrace)
            {
                EndObject();
            }
            else if (marker == JsonConstants.CloseBracket)
            {
                EndArray();
            }
            else
            {
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.FoundInvalidCharacter, marker);
            }

        Done:
            return ConsumeTokenResult.Success;
        IncompleteNoRollback:
            return ConsumeTokenResult.IncompleteNoRollBackNecessary;
        IncompleteRollback:
            return ConsumeTokenResult.NotEnoughDataRollBackState;
        }

        private bool SkipComment()
        {
            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer.Slice(_consumed + 1);

            if (localBuffer.Length > 0)
            {
                byte marker = localBuffer[0];
                if (marker == JsonConstants.Slash)
                {
                    return SkipSingleLineComment(localBuffer.Slice(1), out _);
                }
                else if (marker == JsonConstants.Asterisk)
                {
                    return SkipMultiLineComment(localBuffer.Slice(1), out _);
                }
                else
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, JsonConstants.Slash);
                }
            }

            if (IsLastSpan)
            {
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, JsonConstants.Slash);
            }
            return false;
        }

        private bool SkipSingleLineComment(ReadOnlySpan<byte> localBuffer, out int idx)
        {
            idx = FindLineSeparator(localBuffer);
            int toConsume;
            if (idx != -1)
            {
                toConsume = idx;
                if (localBuffer[idx] == JsonConstants.LineFeed)
                {
                    goto EndOfComment;
                }

                // If we are here, we have definintely found a \r. So now to check if \n follows.

                if (idx < localBuffer.Length - 1)
                {
                    if (localBuffer[idx + 1] == JsonConstants.LineFeed)
                    {
                        toConsume++;
                    }

                    goto EndOfComment;
                }

                if (IsLastSpan)
                {
                    goto EndOfComment;
                }
                else
                {
                    // there might be LF in the next segment
                    return false;
                }
            }

            if (IsLastSpan)
            {
                idx = localBuffer.Length;
                toConsume = idx;
                // Assume everything on this line is a comment and there is no more data.
                _bytePositionInLine += 2 + localBuffer.Length;
                goto Done;
            }
            else
            {
                return false;
            }

        EndOfComment:
            toConsume++;
            _bytePositionInLine = 0;
            _lineNumber++;

        Done:
            _consumed += 2 + toConsume;
            return true;
        }

        private int FindLineSeparator(ReadOnlySpan<byte> localBuffer)
        {
            int totalIdx = 0;
            while (true)
            {
                int idx = JsonReaderHelper.IndexOfAny(localBuffer, JsonConstants.LineFeed, JsonConstants.CarriageReturn, JsonConstants.StartingByteOfNonStandardSeparator);

                if (idx == -1)
                {
                    return -1;
                }

                totalIdx += idx;

                if (localBuffer[idx] != JsonConstants.StartingByteOfNonStandardSeparator)
                {
                    return totalIdx;
                }

                totalIdx++;
                localBuffer = localBuffer.Slice(idx + 1);

                ThrowOnDangerousLineSeparator(localBuffer);
            }
        }

        // assumes first byte (JsonConstants.StartingByteOfNonStandardSeparator) is already read
        private void ThrowOnDangerousLineSeparator(ReadOnlySpan<byte> localBuffer)
        {
            // \u2028 and \u2029 are considered respectively line and paragraph separators
            // UTF-8 representation for them is E2, 80, A8/A9
            // we have already read E2, we need to check for remaining 2 bytes

            if (localBuffer.Length < 2)
            {
                return;
            }

            byte next = localBuffer[1];
            if (localBuffer[0] == 0x80 && (next is 0xA8 or 0xA9))
            {
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.UnexpectedEndOfLineSeparator);
            }
        }

        private bool SkipMultiLineComment(ReadOnlySpan<byte> localBuffer, out int idx)
        {
            idx = 0;
            while (true)
            {
                int foundIdx = localBuffer.Slice(idx).IndexOf(JsonConstants.Slash);
                if (foundIdx == -1)
                {
                    if (IsLastSpan)
                    {
                        JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.EndOfCommentNotFound);
                    }
                    return false;
                }
                if (foundIdx != 0 && localBuffer[foundIdx + idx - 1] == JsonConstants.Asterisk)
                {
                    // foundIdx points just after '*' in the end-of-comment delimiter. Hence increment idx by one
                    // position less to make it point right before beginning of end-of-comment delimiter i.e. */
                    idx += foundIdx - 1;
                    break;
                }
                idx += foundIdx + 1;
            }

            // Consume the /* and */ characters that are part of the multi-line comment.
            // idx points right before the final '*' (which is right before the last '/'). Hence increment _consumed
            // by 4 to exclude the start/end-of-comment delimiters.
            _consumed += 4 + idx;

            (int newLines, int newLineIndex) = JsonReaderHelper.CountNewLines(localBuffer.Slice(0, idx));
            _lineNumber += newLines;
            if (newLineIndex != -1)
            {
                // newLineIndex points at last newline character and byte positions in the new line start
                // after that. Hence add 1 to skip the newline character.
                _bytePositionInLine = idx - newLineIndex + 1;
            }
            else
            {
                _bytePositionInLine += 4 + idx;
            }
            return true;
        }

        private bool ConsumeComment()
        {
            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer.Slice(_consumed + 1);

            if (localBuffer.Length > 0)
            {
                byte marker = localBuffer[0];
                if (marker == JsonConstants.Slash)
                {
                    return ConsumeSingleLineComment(localBuffer.Slice(1), _consumed);
                }
                else if (marker == JsonConstants.Asterisk)
                {
                    return ConsumeMultiLineComment(localBuffer.Slice(1), _consumed);
                }
                else
                {
                    JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterAtStartOfComment, marker);
                }
            }

            if (IsLastSpan)
            {
                JsonThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.UnexpectedEndOfDataWhileReadingComment);
            }
            return false;
        }

        private bool ConsumeSingleLineComment(ReadOnlySpan<byte> localBuffer, int previousConsumed)
        {
            if (!SkipSingleLineComment(localBuffer, out int idx))
            {
                return false;
            }

            // Exclude the // at start of the comment. idx points right before the line separator
            // at the end of the comment.
            ValueSpan = _buffer.Slice(previousConsumed + 2, idx);
            if (_tokenType != JsonTokenType.Comment)
            {
                _previousTokenType = _tokenType;
            }
            _tokenType = JsonTokenType.Comment;
            return true;
        }

        private bool ConsumeMultiLineComment(ReadOnlySpan<byte> localBuffer, int previousConsumed)
        {
            if (!SkipMultiLineComment(localBuffer, out int idx))
            {
                return false;
            }

            // Exclude the /* at start of the comment. idx already points right before the terminal '*/'
            // for the end of multiline comment.
            ValueSpan = _buffer.Slice(previousConsumed + 2, idx);
            if (_tokenType != JsonTokenType.Comment)
            {
                _previousTokenType = _tokenType;
            }
            _tokenType = JsonTokenType.Comment;
            return true;
        }
        private string DebuggerDisplay => $"TokenType = {DebugTokenType}, TokenStartIndex = {TokenStartIndex}, Consumed = {BytesConsumed}";

        // Using TokenType.ToString() (or {TokenType}) fails to render in the debug window. The
        // message "The runtime refused to evaluate the expression at this time." is shown. This
        // is a workaround until we root cause and fix the issue.
        private string DebugTokenType
            => TokenType switch
            {
                JsonTokenType.Comment => nameof(JsonTokenType.Comment),
                JsonTokenType.EndArray => nameof(JsonTokenType.EndArray),
                JsonTokenType.EndObject => nameof(JsonTokenType.EndObject),
                JsonTokenType.False => nameof(JsonTokenType.False),
                JsonTokenType.None => nameof(JsonTokenType.None),
                JsonTokenType.Null => nameof(JsonTokenType.Null),
                JsonTokenType.Number => nameof(JsonTokenType.Number),
                JsonTokenType.PropertyName => nameof(JsonTokenType.PropertyName),
                JsonTokenType.StartArray => nameof(JsonTokenType.StartArray),
                JsonTokenType.StartObject => nameof(JsonTokenType.StartObject),
                JsonTokenType.String => nameof(JsonTokenType.String),
                JsonTokenType.True => nameof(JsonTokenType.True),
                _ => ((byte)TokenType).ToString()
            };

        private ReadOnlySpan<byte> GetUnescapedSpan()
        {
            ReadOnlySpan<byte> span = ValueSpan;
            if (ValueIsEscaped)
            {
                span = JsonReaderHelper.GetUnescaped(span);
            }

            return span;
        }
    
        // ---- Utf8JsonReaderImpl.TryGet.cs ----

        public string GetString()
        {
            if (TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (TokenType != JsonTokenType.String && TokenType != JsonTokenType.PropertyName)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;

            if (ValueIsEscaped)
            {
                return JsonReaderHelper.GetUnescapedString(span);
            }

            return JsonReaderHelper.TranscodeHelper(span);
        }

        public readonly int CopyString(Span<byte> utf8Destination)
        {
            if (_tokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(_tokenType);
            }

            return CopyValue(utf8Destination);
        }

        internal readonly int CopyValue(Span<byte> utf8Destination)
        {
            int bytesWritten;
            if (ValueIsEscaped)
            {
                if (!TryCopyEscapedString(utf8Destination, out bytesWritten))
                {
                    utf8Destination.Slice(0, bytesWritten).Clear();
                    JsonThrowHelper.ThrowArgumentException_DestinationTooShort();
                }
            }
            else
            {
                ReadOnlySpan<byte> valueSpan = ValueSpan;
                if (valueSpan.Length > utf8Destination.Length) JsonThrowHelper.ThrowArgumentException_DestinationTooShort();
                valueSpan.CopyTo(utf8Destination);
                bytesWritten = valueSpan.Length;
            }
            JsonReaderHelper.ValidateUtf8(utf8Destination.Slice(0, bytesWritten));
            return bytesWritten;
        }

        public readonly int CopyString(Span<char> destination)
        {
            if (_tokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(_tokenType);
            }
            return CopyValue(destination);
        }

        internal readonly int CopyValue(Span<char> destination)
        {
            ReadOnlySpan<byte> unescapedSource;
            if (ValueIsEscaped)
            {
                byte[] unescapedBuffer = new byte[ValueSpan.Length];
                TryCopyEscapedString(unescapedBuffer, out int bytesWritten);
                unescapedSource = new ReadOnlySpan<byte>(unescapedBuffer, 0, bytesWritten);
            }
            else
            {
                unescapedSource = ValueSpan;
            }
            return JsonReaderHelper.TranscodeHelper(unescapedSource, destination);
        }

        private readonly bool TryCopyEscapedString(Span<byte> destination, out int bytesWritten)
        {
            return JsonReaderHelper.TryUnescape(ValueSpan, destination, out bytesWritten);
        }

        public string GetComment()
        {
            if (TokenType != JsonTokenType.Comment)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedComment(TokenType);
            }
            ReadOnlySpan<byte> span = ValueSpan;
            return JsonReaderHelper.TranscodeHelper(span);
        }

        public bool GetBoolean()
        {
            JsonTokenType type = TokenType;
            if (type == JsonTokenType.True)
            {

                return true;
            }
            else if (type != JsonTokenType.False)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedBoolean(TokenType);

            }

            return false;
        }

        public byte[] GetBytesFromBase64()
        {
            if (!TryGetBytesFromBase64(out byte[] value))
            {
                JsonThrowHelper.ThrowFormatException(DataType.Base64String);
            }
            return value;
        }

        public byte GetByte()
        {
            if (!TryGetByte(out byte value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Byte);
            }
            return value;
        }

        internal byte GetByteWithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetByteCore(out byte value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Byte);
            }
            return value;
        }
        public sbyte GetSByte()
        {
            if (!TryGetSByte(out sbyte value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.SByte);
            }
            return value;
        }

        internal sbyte GetSByteWithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetSByteCore(out sbyte value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.SByte);
            }
            return value;
        }

        public short GetInt16()
        {
            if (!TryGetInt16(out short value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Int16);
            }
            return value;
        }

        internal short GetInt16WithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetInt16Core(out short value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Int16);
            }
            return value;
        }

        public int GetInt32()
        {
            if (!TryGetInt32(out int value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Int32);
            }
            return value;
        }

        internal int GetInt32WithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetInt32Core(out int value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Int32);
            }
            return value;
        }

        public long GetInt64()
        {
            if (!TryGetInt64(out long value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Int64);
            }
            return value;
        }

        internal long GetInt64WithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetInt64Core(out long value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Int64);
            }
            return value;
        }
        public ushort GetUInt16()
        {
            if (!TryGetUInt16(out ushort value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.UInt16);
            }
            return value;
        }

        internal ushort GetUInt16WithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetUInt16Core(out ushort value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.UInt16);
            }
            return value;
        }
        public uint GetUInt32()
        {
            if (!TryGetUInt32(out uint value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.UInt32);
            }
            return value;
        }

        internal uint GetUInt32WithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetUInt32Core(out uint value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.UInt32);
            }
            return value;
        }
        public ulong GetUInt64()
        {
            if (!TryGetUInt64(out ulong value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.UInt64);
            }
            return value;
        }

        internal ulong GetUInt64WithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetUInt64Core(out ulong value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.UInt64);
            }
            return value;
        }

        public float GetSingle()
        {
            if (!TryGetSingle(out float value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Single);
            }
            return value;
        }

        internal float GetSingleWithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();

            if (JsonReaderHelper.TryGetFloatingPointConstant(span, out float value))
            {
                return value;
            }

            // NETCOREAPP implementation of the TryParse method above permits case-insensitive variants of the
            // float constants "NaN", "Infinity", "-Infinity". This differs from the NETFRAMEWORK implementation.
            // The following logic reconciles the two implementations to enforce consistent behavior.
            if (!(JsonNumberParser.TryParse(span, out value)
                  && float.IsFinite(value)))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Single);
            }

            return value;
        }

        internal float GetSingleFloatingPointConstant()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();

            if (!JsonReaderHelper.TryGetFloatingPointConstant(span, out float value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Single);
            }

            return value;
        }

        public double GetDouble()
        {
            if (!TryGetDouble(out double value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Double);
            }
            return value;
        }

        internal double GetDoubleWithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();

            if (JsonReaderHelper.TryGetFloatingPointConstant(span, out double value))
            {
                return value;
            }

            // NETCOREAPP implementation of the TryParse method above permits case-insensitive variants of the
            // float constants "NaN", "Infinity", "-Infinity". This differs from the NETFRAMEWORK implementation.
            // The following logic reconciles the two implementations to enforce consistent behavior.
            if (!(JsonNumberParser.TryParse(span, out value)
                  && double.IsFinite(value)))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Double);
            }

            return value;
        }

        internal double GetDoubleFloatingPointConstant()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();

            if (!JsonReaderHelper.TryGetFloatingPointConstant(span, out double value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Double);
            }

            return value;
        }

        public decimal GetDecimal()
        {
            if (!TryGetDecimal(out decimal value))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Decimal);
            }
            return value;
        }

        internal decimal GetDecimalWithQuotes()
        {
            ReadOnlySpan<byte> span = GetUnescapedSpan();
            if (!TryGetDecimalCore(out decimal value, span))
            {
                JsonThrowHelper.ThrowFormatException(NumericType.Decimal);
            }
            return value;
        }

        public DateTime GetDateTime()
        {
            if (!TryGetDateTime(out DateTime value))
            {
                JsonThrowHelper.ThrowFormatException(DataType.DateTime);
            }

            return value;
        }

        public Guid GetGuid()
        {
            if (!TryGetGuid(out Guid value)) JsonThrowHelper.ThrowFormatException("Guid");
            return value;
        }

        public bool TryGetGuid(out Guid value)
        {
            if (TokenType != JsonTokenType.String) JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(TokenType);
            return JsonReaderHelper.TryGetValue(ValueSpan, ValueIsEscaped, out value);
        }

        public DateTimeOffset GetDateTimeOffset()
        {
            if (!TryGetDateTimeOffset(out DateTimeOffset value))
            {
                JsonThrowHelper.ThrowFormatException(DataType.DateTimeOffset);
            }

            return value;
        }

        internal DateTimeOffset GetDateTimeOffsetNoValidation()
        {
            if (!TryGetDateTimeOffsetCore(out DateTimeOffset value))
            {
                JsonThrowHelper.ThrowFormatException(DataType.DateTimeOffset);
            }

            return value;
        }

        internal DateTime GetDateTimeNoValidation()
        {
            if (!TryGetDateTimeCore(out DateTime value))
            {
                JsonThrowHelper.ThrowFormatException(DataType.DateTime);
            }

            return value;
        }





        public bool TryGetBytesFromBase64(out byte[] value)
        {
            if (TokenType != JsonTokenType.String)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;

            if (ValueIsEscaped)
            {
                return JsonReaderHelper.TryGetUnescapedBase64Bytes(span, out value);
            }

            return JsonReaderHelper.TryDecodeBase64(span, out value);
        }

        public bool TryGetByte(out byte value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetByteCore(out value, span);
        }
        internal static bool TryGetByteCore(out byte value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out byte tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }
        public bool TryGetSByte(out sbyte value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetSByteCore(out value, span);
        }
        internal static bool TryGetSByteCore(out sbyte value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out sbyte tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInt16(out short value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetInt16Core(out value, span);
        }
        internal static bool TryGetInt16Core(out short value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out short tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInt32(out int value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetInt32Core(out value, span);
        }
        internal static bool TryGetInt32Core(out int value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out int tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInt64(out long value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetInt64Core(out value, span);
        }
        internal static bool TryGetInt64Core(out long value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out long tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }
        public bool TryGetUInt16(out ushort value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetUInt16Core(out value, span);
        }
        internal static bool TryGetUInt16Core(out ushort value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out ushort tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }
        public bool TryGetUInt32(out uint value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetUInt32Core(out value, span);
        }
        internal static bool TryGetUInt32Core(out uint value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out uint tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }
        public bool TryGetUInt64(out ulong value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetUInt64Core(out value, span);
        }
        internal static bool TryGetUInt64Core(out ulong value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out ulong tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetSingle(out float value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;

            if (JsonNumberParser.TryParse(span, out float tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetDouble(out double value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;

            if (JsonNumberParser.TryParse(span, out double tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetDecimal(out decimal value)
        {
            if (TokenType != JsonTokenType.Number)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedNumber(TokenType);
            }

            ReadOnlySpan<byte> span = ValueSpan;
            return TryGetDecimalCore(out value, span);
        }
        internal static bool TryGetDecimalCore(out decimal value, ReadOnlySpan<byte> span)
        {
            if (JsonNumberParser.TryParse(span, out decimal tmp))
            {
                value = tmp;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetDateTime(out DateTime value)
        {
            if (TokenType != JsonTokenType.String)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(TokenType);
            }

            return TryGetDateTimeCore(out value);
        }

        public bool TryGetDateTimeOffset(out DateTimeOffset value)
        {
            if (TokenType != JsonTokenType.String)
            {
                JsonThrowHelper.ThrowInvalidOperationException_ExpectedString(TokenType);
            }

            return TryGetDateTimeOffsetCore(out value);
        }

        internal bool TryGetDateTimeOffsetCore(out DateTimeOffset value) =>
            JsonReaderHelper.TryGetValue(ValueSpan, ValueIsEscaped, out value);

        internal unsafe bool TryGetDateTimeCore(out DateTime value)
        {
            scoped ReadOnlySpan<byte> span;

                            span = ValueSpan;

            return JsonReaderHelper.TryGetValue(span, ValueIsEscaped, out value);
        }




        }
}
