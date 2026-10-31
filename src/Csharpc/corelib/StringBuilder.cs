// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/
// Text/StringBuilder.cs; see THIRD-PARTY-NOTICES.md.
//
// System.Text.StringBuilder as .NET builds it: a linked list of chunks, the builder object itself being the
// last one (each chunk points to its predecessor), new chunks of max(needed, min(Length, 8000)) chars, and
// .NET's Insert/Remove/Replace/Length/Capacity algorithms — so GetChunks, Capacity and EnsureCapacity report
// exactly what .NET's do. csharpc keeps its own fast paths where they change nothing observable: integers
// are written straight into the current chunk, a single-chunk builder copies out directly, and the indexer
// remembers the chunk it last found (keyed on a structure version) instead of walking from the end each time.
// Exception types and messages follow .NET 11 (tests/samples/161_stringbuilder.cs, 732_stringbuilder_chunks.cs).
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.StringBuilder")]
    public sealed class StringBuilderImpl
    {
        private const int DefaultCapacity = 16;
        // Chunks stay below the large object heap's threshold in .NET; the size also sets the chunk layout.
        private const int MaxChunkSize = 8000;

        // The current (last) chunk: m_ChunkChars, m_ChunkLength, m_ChunkOffset, m_ChunkPrevious in .NET.
        private char[] _chars;
        private int _length;
        private int _offset;
        private StringBuilderImpl _previous;
        private int _maxCapacity;

        // On the builder object: the earlier chunk the indexer found last, cleared by every change to the chunk
        // list or to earlier chunks' offsets and lengths (so it never keeps a dropped chunk alive). Replace uses the
        // field on every chunk for forward links while it runs (ChunkCursor).
        private StringBuilderImpl _lookup;

        private const string IndexMustBeLessOrEqual = "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        private const string IndexLength = "Index and length must refer to a location within the string.";
        private const string CapacityExceeded = "Capacity exceeds maximum capacity.";
        private const string SmallCapacity = "capacity was less than the current size.";
        private const string LengthGreaterThanCapacity = "The length cannot be greater than the capacity.";

        // ---- construction ------------------------------------------------------------------------
        public StringBuilderImpl()
        {
            _maxCapacity = int.MaxValue;
            _chars = new char[DefaultCapacity];
        }

        public StringBuilderImpl(int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            if (capacity == 0) capacity = DefaultCapacity;
            _maxCapacity = int.MaxValue;
            _chars = new char[capacity];
        }

        public StringBuilderImpl(string value) : this(value, DefaultCapacity) { }

        public StringBuilderImpl(string value, int capacity) : this(value, 0, value == null ? 0 : value.Length, capacity) { }

        public StringBuilderImpl(string value, int startIndex, int length, int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            ThrowHelper.IfNegative(length, "length");
            ThrowHelper.IfNegative(startIndex, "startIndex");
            value ??= string.Empty;
            if (startIndex > value.Length - length) throw new ArgumentOutOfRangeException("length", IndexLength);
            _maxCapacity = int.MaxValue;
            if (capacity == 0) capacity = DefaultCapacity;
            capacity = Math.Max(capacity, length);
            _chars = new char[capacity];
            _length = length;
            value.AsSpan(startIndex, length).CopyTo(_chars);
        }

        public StringBuilderImpl(int capacity, int maxCapacity)
        {
            if (capacity > maxCapacity) throw new ArgumentOutOfRangeException("capacity", CapacityExceeded);
            if (maxCapacity <= 0)
                throw new ArgumentOutOfRangeException("maxCapacity", maxCapacity, "maxCapacity ('" + maxCapacity + "') must be a non-negative and non-zero value.");
            ThrowHelper.IfNegative(capacity, "capacity");
            if (capacity == 0) capacity = Math.Min(DefaultCapacity, maxCapacity);
            _maxCapacity = maxCapacity;
            _chars = new char[capacity];
        }

        /// <summary>A chunk taking over another's contents (ExpandByABlock's predecessor).</summary>
        private StringBuilderImpl(StringBuilderImpl from)
        {
            _length = from._length;
            _offset = from._offset;
            _chars = from._chars;
            _previous = from._previous;
            _maxCapacity = from._maxCapacity;
        }

        /// <summary>An empty chunk of <paramref name="size"/> chars after <paramref name="previousBlock"/> (MakeRoom).</summary>
        private StringBuilderImpl(int size, int maxCapacity, StringBuilderImpl previousBlock)
        {
            _chars = new char[size];
            _maxCapacity = maxCapacity;
            _previous = previousBlock;
            if (previousBlock != null) _offset = previousBlock._offset + previousBlock._length;
        }

        // ---- size --------------------------------------------------------------------------------
        public int Capacity
        {
            get => _chars.Length + _offset;
            set
            {
                ThrowHelper.IfNegative(value, "value");
                if (value > _maxCapacity) throw new ArgumentOutOfRangeException("value", CapacityExceeded);
                if (value < Length) throw new ArgumentOutOfRangeException("value", SmallCapacity);
                if (Capacity != value)
                {
                    char[] grown = new char[value - _offset];
                    Array.Copy(_chars, grown, _length);
                    _chars = grown;
                }
            }
        }

        public int MaxCapacity => _maxCapacity;

        public int EnsureCapacity(int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            if (Capacity < capacity) Capacity = capacity;
            return Capacity;
        }

        public int Length
        {
            get => _offset + _length;
            set
            {
                ThrowHelper.IfNegative(value, "value");
                if (value > _maxCapacity) throw new ArgumentOutOfRangeException("value", SmallCapacity);
                if (value == 0 && _previous == null)
                {
                    _length = 0;
                    _offset = 0;
                    return;
                }
                int delta = value - Length;
                if (delta > 0)
                {
                    Append('\0', delta); // pad with null characters
                    return;
                }
                StringBuilderImpl chunk = FindChunkForIndex(value);
                if (chunk != this)
                {
                    // Keep at most a little more capacity than the content (dotnet/coreclr#16926: no unbounded growth).
                    int capacityToPreserve = Math.Min(Capacity, Math.Max(Length * 6 / 5, _chars.Length));
                    int newLen = capacityToPreserve - chunk._offset;
                    if (newLen > chunk._chars.Length)
                    {
                        char[] grown = new char[newLen];
                        Array.Copy(chunk._chars, grown, chunk._length);
                        _chars = grown;
                    }
                    else _chars = chunk._chars;
                    _previous = chunk._previous;
                    _offset = chunk._offset;
                    ForgetLookup();
                }
                _length = value - chunk._offset;
            }
        }

        [IndexerName("Chars")] // the real indexer's metadata name
        public char this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException();
                int inChunk = index - _offset;
                if (inChunk >= 0) return _chars[inChunk];
                StringBuilderImpl chunk = _lookup;
                if (chunk != null && (uint)(inChunk = index - chunk._offset) < (uint)chunk._length) return chunk._chars[inChunk];
                chunk = LookupChunk(index);
                return chunk._chars[index - chunk._offset];
            }
            set
            {
                if ((uint)index >= (uint)Length)
                    throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
                int inChunk = index - _offset;
                if (inChunk >= 0) { _chars[inChunk] = value; return; }
                StringBuilderImpl chunk = _lookup;
                if (chunk != null && (uint)(inChunk = index - chunk._offset) < (uint)chunk._length) { chunk._chars[inChunk] = value; return; }
                chunk = LookupChunk(index);
                chunk._chars[index - chunk._offset] = value;
            }
        }

        /// <summary>
        /// The chunk holding <paramref name="index"/> (before the last chunk): .NET walks from the end every time,
        /// quadratic over a long builder; the chunk found last answers again (checked inline by the indexer).
        /// </summary>
        private void ForgetLookup() => _lookup = null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private StringBuilderImpl LookupChunk(int index)
        {
            // Walk back from the cached chunk when the index lies before it (a forward scan's next chunk is
            // found from the end once per chunk), else from the end.
            StringBuilderImpl cached = _lookup;
            StringBuilderImpl chunk = cached != null && index < cached._offset ? cached : this;
            while (chunk._offset > index) chunk = chunk._previous;
            _lookup = chunk;
            return chunk;
        }

        // ---- append ------------------------------------------------------------------------------
        public StringBuilderImpl Append(string value)
        {
            if (value != null)
            {
                int count = value.Length;
                int length = _length;
                char[] chars = _chars;
                if ((uint)(length + count) <= (uint)chars.Length)
                {
                    value.AsSpan().CopyTo(new Span<char>(chars, length, count));
                    _length = length + count;
                }
                else AppendWithExpansion(value.AsSpan());
            }
            return this;
        }

        public StringBuilderImpl Append(ReadOnlySpan<char> value)
        {
            int count = value.Length;
            if (count != 0)
            {
                int length = _length;
                char[] chars = _chars;
                if ((uint)(length + count) <= (uint)chars.Length)
                {
                    value.CopyTo(new Span<char>(chars, length, count));
                    _length = length + count;
                }
                else AppendWithExpansion(value);
            }
            return this;
        }

        public StringBuilderImpl Append(ReadOnlyMemory<char> value) => Append(value.Span);

        public unsafe StringBuilderImpl Append(char* value, int valueCount)
        {
            ThrowHelper.IfNegative(valueCount, "valueCount");
            return Append(new ReadOnlySpan<char>(value, valueCount));
        }

        /// <summary>The part that doesn't fit fills the current chunk, the rest goes into one new chunk.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendWithExpansion(ReadOnlySpan<char> value)
        {
            int valueCount = value.Length;
            int newLength = Length + valueCount;
            if (newLength > _maxCapacity || newLength < valueCount)
                throw new ArgumentOutOfRangeException("valueCount", LengthGreaterThanCapacity);
            int firstLength = _chars.Length - _length;
            if (firstLength > 0)
            {
                value.Slice(0, firstLength).CopyTo(new Span<char>(_chars, _length, firstLength));
                _length = _chars.Length;
            }
            int restLength = valueCount - firstLength;
            ExpandByABlock(restLength);
            value.Slice(firstLength).CopyTo(_chars);
            _length = restLength;
        }

        public StringBuilderImpl Append(string value, int startIndex, int count)
        {
            ThrowHelper.IfNegative(startIndex, "startIndex");
            ThrowHelper.IfNegative(count, "count");
            if (value == null)
            {
                if (startIndex == 0 && count == 0) return this;
                throw new ArgumentNullException("value");
            }
            if (count != 0)
            {
                if (startIndex > value.Length - count) throw new ArgumentOutOfRangeException("startIndex", IndexMustBeLessOrEqual);
                Append(value.AsSpan(startIndex, count));
            }
            return this;
        }

        public StringBuilderImpl Append(char value)
        {
            int length = _length;
            char[] chars = _chars;
            if ((uint)chars.Length > (uint)length)
            {
                chars[length] = value;
                _length = length + 1;
            }
            else AppendWithExpansion(value);
            return this;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendWithExpansion(char value)
        {
            ExpandByABlock(1);
            _chars[0] = value;
            _length++;
        }

        /// <summary>The rune's UTF-16 code units (one, or a surrogate pair), appended as one span, as .NET does.</summary>
        public StringBuilderImpl Append(System.Text.Rune value)
        {
            Span<char> units = stackalloc char[2];
            int count = value.EncodeToUtf16(units);
            return Append((ReadOnlySpan<char>)units.Slice(0, count));
        }

        public StringBuilderImpl Append(char value, int repeatCount)
        {
            ThrowHelper.IfNegative(repeatCount, "repeatCount");
            if (repeatCount == 0) return this;
            int length = _length;
            char[] chars = _chars;
            if ((uint)(length + repeatCount) <= (uint)chars.Length)
            {
                chars.AsSpan(length, repeatCount).Fill(value);
                _length = length + repeatCount;
            }
            else AppendWithExpansion(value, repeatCount);
            return this;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendWithExpansion(char value, int repeatCount)
        {
            if ((uint)(repeatCount + Length) > (uint)_maxCapacity)
                throw new ArgumentOutOfRangeException("repeatCount", LengthGreaterThanCapacity);
            int firstLength = _chars.Length - _length;
            if (firstLength > 0)
            {
                _chars.AsSpan(_length, firstLength).Fill(value);
                _length = _chars.Length;
            }
            int restLength = repeatCount - firstLength;
            ExpandByABlock(restLength);
            _chars.AsSpan(0, restLength).Fill(value);
            _length = restLength;
        }

        public StringBuilderImpl Append(char[] value)
        {
            if (value != null) Append(new ReadOnlySpan<char>(value));
            return this;
        }

        public StringBuilderImpl Append(char[] value, int startIndex, int charCount)
        {
            ThrowHelper.IfNegative(startIndex, "startIndex");
            ThrowHelper.IfNegative(charCount, "charCount");
            if (value == null)
            {
                if (startIndex == 0 && charCount == 0) return this;
                throw new ArgumentNullException("value");
            }
            if (charCount > value.Length - startIndex) throw new ArgumentOutOfRangeException("charCount", IndexMustBeLessOrEqual);
            if (charCount != 0) Append(new ReadOnlySpan<char>(value, startIndex, charCount));
            return this;
        }

        public StringBuilderImpl Append(StringBuilderImpl value)
        {
            if (value != null && value.Length != 0) return AppendCore(value, 0, value.Length);
            return this;
        }

        public StringBuilderImpl Append(StringBuilderImpl value, int startIndex, int count)
        {
            ThrowHelper.IfNegative(startIndex, "startIndex");
            ThrowHelper.IfNegative(count, "count");
            if (value == null)
            {
                if (startIndex == 0 && count == 0) return this;
                throw new ArgumentNullException("value");
            }
            if (count == 0) return this;
            if (count > value.Length - startIndex) throw new ArgumentOutOfRangeException("startIndex", IndexMustBeLessOrEqual);
            return AppendCore(value, startIndex, count);
        }

        private StringBuilderImpl AppendCore(StringBuilderImpl value, int startIndex, int count)
        {
            if (value == this) return Append(value.ToString(startIndex, count));
            int newLength = Length + count;
            if ((uint)newLength > (uint)_maxCapacity) throw new ArgumentOutOfRangeException("Capacity", CapacityExceeded);
            while (count > 0)
            {
                int length = Math.Min(_chars.Length - _length, count);
                if (length == 0)
                {
                    ExpandByABlock(count);
                    length = count;
                }
                value.CopyTo(startIndex, new Span<char>(_chars, _length, length), length);
                _length += length;
                startIndex += length;
                count -= length;
            }
            return this;
        }

        /// <summary>
        /// Integers are formatted straight into the current chunk when they fit (no temporary string; chars ≤ 20,
        /// so the sum can't overflow), else appended as their string — .NET's AppendSpanFormattable: TryFormat
        /// into the chunk's free space, else Append(ToString()), which places the chars the same way.
        /// </summary>
        private unsafe StringBuilderImpl AppendSigned(long value)
        {
            // Negative values take the current culture's NegativeSign (Append(int) formats like ToString()).
            if (value < 0) return Append(Number.Int64ToDecStr(value));
            int chars = Number.CountChars(value);
            if (_length + chars <= _chars.Length)
            {
                fixed (char* buffer = _chars) Number.WriteSigned(buffer + _length, chars, value);
                _length += chars;
                return this;
            }
            return Append(Number.Int64ToDecStr(value));
        }

        private unsafe StringBuilderImpl AppendUnsigned(ulong value)
        {
            int chars = Number.CountDigits(value);
            if (_length + chars <= _chars.Length)
            {
                fixed (char* buffer = _chars) Number.WriteUnsigned(buffer + _length, chars, value);
                _length += chars;
                return this;
            }
            return Append(value.ToString());
        }

        public StringBuilderImpl Append(int value) => AppendSigned(value);
        public StringBuilderImpl Append(long value) => AppendSigned(value);
        public StringBuilderImpl Append(short value) => AppendSigned(value);
        public StringBuilderImpl Append(sbyte value) => AppendSigned(value);
        public StringBuilderImpl Append(uint value) => AppendUnsigned(value);
        public StringBuilderImpl Append(ulong value) => AppendUnsigned(value);
        public StringBuilderImpl Append(ushort value) => AppendUnsigned(value);
        public StringBuilderImpl Append(byte value) => AppendUnsigned(value);
        public StringBuilderImpl Append(bool value) => Append(value ? "True" : "False");
        public StringBuilderImpl Append(double value) => Append(value.ToString());
        public StringBuilderImpl Append(float value) => Append(value.ToString());
        public StringBuilderImpl Append(decimal value) => Append(value.ToString());
        public StringBuilderImpl Append(object value) => value == null ? this : Append(value.ToString());

        public StringBuilderImpl AppendLine() => Append(global::System.Environment.NewLine);
        public StringBuilderImpl AppendLine(string value)
        {
            Append(value);
            return Append(global::System.Environment.NewLine);
        }

        // ---- AppendFormat ----------------------------------------------------------------------------
        public StringBuilderImpl AppendFormat(string format, object arg0) => AppendFormat(null, format, new[] { arg0 });
        public StringBuilderImpl AppendFormat(string format, object arg0, object arg1) => AppendFormat(null, format, new[] { arg0, arg1 });
        public StringBuilderImpl AppendFormat(string format, object arg0, object arg1, object arg2) => AppendFormat(null, format, new[] { arg0, arg1, arg2 });
        public StringBuilderImpl AppendFormat(string format, params object[] args)
        {
            if (args == null) throw new ArgumentNullException(format == null ? "format" : "args");
            return AppendFormat(null, format, (ReadOnlySpan<object>)args);
        }
        public StringBuilderImpl AppendFormat(string format, params ReadOnlySpan<object> args) => AppendFormat(null, format, args);
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, object arg0) => AppendFormat(provider, format, new[] { arg0 });
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, object arg0, object arg1) => AppendFormat(provider, format, new[] { arg0, arg1 });
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, object arg0, object arg1, object arg2) =>
            AppendFormat(provider, format, new[] { arg0, arg1, arg2 });
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, params object[] args)
        {
            if (args == null) throw new ArgumentNullException(format == null ? "format" : "args");
            return AppendFormat(provider, format, (ReadOnlySpan<object>)args);
        }

        /// <summary>
        /// .NET's AppendFormat step for step: literal runs, escaped braces and padding are appended piece by piece
        /// (the pieces decide where chunks split), and errors report .NET's offsets and reasons. An argument
        /// formatted into the chunk's free space by .NET lands where appending its string puts it.
        /// </summary>
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, params ReadOnlySpan<object> args)
        {
            if (format == null) throw new ArgumentNullException("format");
            const int IndexLimit = 1000000, WidthLimit = 1000000; // exclusive, as in .NET
            ICustomFormatter customFormatter = provider == null ? null : provider.GetFormat(typeof(ICustomFormatter)) as ICustomFormatter;
            int pos = 0;
            char ch;
            while (true)
            {
                while (true)
                {
                    if ((uint)pos >= (uint)format.Length) return this;
                    ReadOnlySpan<char> remainder = format.AsSpan(pos);
                    int countUntilNextBrace = remainder.IndexOfAny('{', '}');
                    if (countUntilNextBrace < 0)
                    {
                        Append(remainder);
                        return this;
                    }
                    Append(remainder.Slice(0, countUntilNextBrace));
                    pos += countUntilNextBrace;
                    char brace = format[pos];
                    ch = Number.CompositeMoveNext(format, ref pos);
                    if (brace == ch)
                    {
                        Append(ch);
                        pos++;
                        continue;
                    }
                    if (brace != '{') Number.ThrowInvalidComposite(pos, "Unexpected closing brace without a corresponding opening brace.");
                    break;
                }

                int width = 0;
                bool leftJustify = false;
                string itemFormat = null;
                int index = ch - '0';
                if ((uint)index >= 10u) Number.ThrowInvalidComposite(pos, "Expected an ASCII digit.");
                ch = Number.CompositeMoveNext(format, ref pos);
                if (ch != '}')
                {
                    while (ch >= '0' && ch <= '9' && index < IndexLimit)
                    {
                        index = index * 10 + ch - '0';
                        ch = Number.CompositeMoveNext(format, ref pos);
                    }
                    while (ch == ' ') ch = Number.CompositeMoveNext(format, ref pos);
                    if (ch == ',')
                    {
                        do { ch = Number.CompositeMoveNext(format, ref pos); } while (ch == ' ');
                        if (ch == '-')
                        {
                            leftJustify = true;
                            ch = Number.CompositeMoveNext(format, ref pos);
                        }
                        width = ch - '0';
                        if ((uint)width >= 10u) Number.ThrowInvalidComposite(pos, "Expected an ASCII digit.");
                        ch = Number.CompositeMoveNext(format, ref pos);
                        while (ch >= '0' && ch <= '9' && width < WidthLimit)
                        {
                            width = width * 10 + ch - '0';
                            ch = Number.CompositeMoveNext(format, ref pos);
                        }
                        while (ch == ' ') ch = Number.CompositeMoveNext(format, ref pos);
                    }
                    if (ch != '}')
                    {
                        if (ch != ':') Number.ThrowInvalidComposite(pos, "Format item ends prematurely.");
                        int startingPos = pos;
                        while (true)
                        {
                            ch = Number.CompositeMoveNext(format, ref pos);
                            if (ch == '}') break;
                            if (ch == '{') Number.ThrowInvalidComposite(pos, "Format item ends prematurely.");
                        }
                        startingPos++;
                        itemFormat = format.Substring(startingPos, pos - startingPos);
                    }
                }
                pos++;
                if ((uint)index >= (uint)args.Length)
                    throw new FormatException("Index (zero based) must be greater than or equal to zero and less than the size of the argument list.");
                object arg = args[index];
                string s = null;
                if (customFormatter != null) s = customFormatter.Format(itemFormat, arg, provider);
                s ??= arg == null ? string.Empty : Number.FormatObjectWith(arg, itemFormat, provider);
                if (width <= s.Length) Append(s);
                else if (leftJustify)
                {
                    Append(s);
                    Append(' ', width - s.Length);
                }
                else
                {
                    Append(' ', width - s.Length);
                    Append(s);
                }
            }
        }

        // ---- AppendJoin ----------------------------------------------------------------------------
        // .NET's AppendJoinCore: each value's ToString(), the separator between them (null values append nothing).
        public StringBuilderImpl AppendJoin(string separator, params string[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return AppendJoin(separator, (ReadOnlySpan<string>)values);
        }

        public StringBuilderImpl AppendJoin(string separator, params object[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return AppendJoin(separator, (ReadOnlySpan<object>)values);
        }

        public StringBuilderImpl AppendJoin<T>(string separator, IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return AppendJoinEnumerable((separator ?? string.Empty).AsSpan(), values);
        }

        public StringBuilderImpl AppendJoin(string separator, params ReadOnlySpan<string> values)
        {
            ReadOnlySpan<char> sep = (separator ?? string.Empty).AsSpan();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(sep);
                if (values[i] != null) Append(values[i]);
            }
            return this;
        }

        public StringBuilderImpl AppendJoin(string separator, params ReadOnlySpan<object> values)
        {
            ReadOnlySpan<char> sep = (separator ?? string.Empty).AsSpan();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(sep);
                if (values[i] != null) Append(values[i].ToString());
            }
            return this;
        }

        public StringBuilderImpl AppendJoin(char separator, params string[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return AppendJoin(separator, (ReadOnlySpan<string>)values);
        }

        public StringBuilderImpl AppendJoin(char separator, params object[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return AppendJoin(separator, (ReadOnlySpan<object>)values);
        }

        public StringBuilderImpl AppendJoin<T>(char separator, IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            Span<char> sep = stackalloc char[1];
            sep[0] = separator;
            return AppendJoinEnumerable(sep, values);
        }

        public StringBuilderImpl AppendJoin(char separator, params ReadOnlySpan<string> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                if (values[i] != null) Append(values[i]);
            }
            return this;
        }

        public StringBuilderImpl AppendJoin(char separator, params ReadOnlySpan<object> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                if (values[i] != null) Append(values[i].ToString());
            }
            return this;
        }

        private StringBuilderImpl AppendJoinEnumerable<T>(ReadOnlySpan<char> separator, IEnumerable<T> values)
        {
            using (IEnumerator<T> en = values.GetEnumerator())
            {
                if (!en.MoveNext()) return this;
                T value = en.Current;
                if (value != null) Append(value.ToString());
                while (en.MoveNext())
                {
                    Append(separator);
                    value = en.Current;
                    if (value != null) Append(value.ToString());
                }
            }
            return this;
        }

        // ---- copy out --------------------------------------------------------------------------------
        public override string ToString()
        {
            int length = _length;
            if (_previous == null)
            {
                // One chunk (most builders): a single copy.
                if (length == 0) return string.Empty;
                string single = CoreIntrinsics.FastAllocateString(length);
                new ReadOnlySpan<char>(_chars, 0, length).CopyTo(CoreIntrinsics.StringChars(single));
                return single;
            }
            return ToStringChunked();
        }

        private string ToStringChunked()
        {
            // Each chunk knows its offset: copied from the last one back, with no list of chunks to build.
            string result = CoreIntrinsics.FastAllocateString(Length);
            Span<char> destination = CoreIntrinsics.StringChars(result);
            StringBuilderImpl chunk = this;
            do
            {
                new ReadOnlySpan<char>(chunk._chars, 0, chunk._length).CopyTo(destination.Slice(chunk._offset));
                chunk = chunk._previous;
            }
            while (chunk != null);
            return result;
        }

        public string ToString(int startIndex, int length)
        {
            int currentLength = Length;
            ThrowHelper.IfNegative(startIndex, "startIndex");
            if (startIndex > currentLength) throw new ArgumentOutOfRangeException("startIndex", "startIndex cannot be larger than length of string.");
            ThrowHelper.IfNegative(length, "length");
            if (startIndex > currentLength - length) throw new ArgumentOutOfRangeException("length", IndexLength);
            string result = CoreIntrinsics.FastAllocateString(length);
            CopyTo(startIndex, CoreIntrinsics.StringChars(result), length);
            return result;
        }

        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            if (destination == null) throw new ArgumentNullException("destination");
            ThrowHelper.IfNegative(destinationIndex, "destinationIndex");
            if (destinationIndex > destination.Length - count)
                throw new ArgumentException("Either offset did not refer to a position in the string, or there is an insufficient length of destination character array.");
            CopyTo(sourceIndex, new Span<char>(destination).Slice(destinationIndex), count);
        }

        public void CopyTo(int sourceIndex, Span<char> destination, int count)
        {
            ThrowHelper.IfNegative(count, "count");
            if ((uint)sourceIndex > (uint)Length) throw new ArgumentOutOfRangeException("sourceIndex", IndexMustBeLessOrEqual);
            if (sourceIndex > Length - count) throw new ArgumentException("Source string was not long enough. Check sourceIndex and count.");
            StringBuilderImpl chunk = this;
            int sourceEndIndex = sourceIndex + count;
            int curDestIndex = count;
            while (count > 0)
            {
                int chunkEndIndex = sourceEndIndex - chunk._offset;
                if (chunkEndIndex >= 0)
                {
                    chunkEndIndex = Math.Min(chunkEndIndex, chunk._length);
                    int chunkCount = count;
                    int chunkStartIndex = chunkEndIndex - count;
                    if (chunkStartIndex < 0)
                    {
                        chunkCount += chunkStartIndex;
                        chunkStartIndex = 0;
                    }
                    curDestIndex -= chunkCount;
                    count -= chunkCount;
                    new ReadOnlySpan<char>(chunk._chars, chunkStartIndex, chunkCount).CopyTo(destination.Slice(curDestIndex));
                }
                chunk = chunk._previous;
            }
        }

        public bool Equals(StringBuilderImpl sb)
        {
            if (sb == null || Length != sb.Length) return false;
            if (sb == this) return true;
            StringBuilderImpl thisChunk = this;
            int thisChunkIndex = thisChunk._length;
            StringBuilderImpl sbChunk = sb;
            int sbChunkIndex = sbChunk._length;
            while (true)
            {
                --thisChunkIndex;
                --sbChunkIndex;
                while (thisChunkIndex < 0)
                {
                    thisChunk = thisChunk._previous;
                    if (thisChunk == null) break;
                    thisChunkIndex = thisChunk._length + thisChunkIndex;
                }
                while (sbChunkIndex < 0)
                {
                    sbChunk = sbChunk._previous;
                    if (sbChunk == null) break;
                    sbChunkIndex = sbChunk._length + sbChunkIndex;
                }
                if (thisChunkIndex < 0) return sbChunkIndex < 0;
                if (sbChunkIndex < 0) return false;
                if (thisChunk._chars[thisChunkIndex] != sbChunk._chars[sbChunkIndex]) return false;
            }
        }

        public bool Equals(ReadOnlySpan<char> span)
        {
            if (span.Length != Length) return false;
            StringBuilderImpl chunk = this;
            int offset = 0;
            do
            {
                int chunkLength = chunk._length;
                offset += chunkLength;
                if (!new ReadOnlySpan<char>(chunk._chars, 0, chunkLength).SequenceEqual(span.Slice(span.Length - offset, chunkLength))) return false;
                chunk = chunk._previous;
            }
            while (chunk != null);
            return true;
        }

        public StringBuilderImpl Clear()
        {
            Length = 0;
            return this;
        }

        // ---- insert / remove -------------------------------------------------------------------------
        public StringBuilderImpl Insert(int index, string value)
        {
            if ((uint)index > (uint)Length) throw new ArgumentOutOfRangeException("index", IndexMustBeLessOrEqual);
            if (value != null) InsertCore(index, value.AsSpan());
            return this;
        }

        public StringBuilderImpl Insert(int index, string value, int count) => Insert(index, value.AsSpan(), count);

        private StringBuilderImpl Insert(int index, ReadOnlySpan<char> value, int count)
        {
            ThrowHelper.IfNegative(count, "count");
            if ((uint)index > (uint)Length) throw new ArgumentOutOfRangeException("index", IndexMustBeLessOrEqual);
            if (value.IsEmpty || count == 0) return this;
            long insertingChars = (long)value.Length * count;
            if (insertingChars > _maxCapacity - Length) throw new OutOfMemoryException();
            MakeRoom(index, (int)insertingChars, out StringBuilderImpl chunk, out int indexInChunk, false);
            while (count > 0)
            {
                ReplaceInPlaceAtChunk(ref chunk, ref indexInChunk, value);
                --count;
            }
            return this;
        }

        public StringBuilderImpl Insert(int index, ReadOnlySpan<char> value)
        {
            if ((uint)index > (uint)Length) throw new ArgumentOutOfRangeException("index", IndexMustBeLessOrEqual);
            if (value.Length != 0) InsertCore(index, value);
            return this;
        }

        public StringBuilderImpl Insert(int index, char value)
        {
            if ((uint)index > (uint)Length) throw new ArgumentOutOfRangeException("index", IndexMustBeLessOrEqual);
            Span<char> one = stackalloc char[1];
            one[0] = value;
            InsertCore(index, one);
            return this;
        }

        public StringBuilderImpl Insert(int index, System.Text.Rune value)
        {
            Span<char> units = stackalloc char[2];
            int count = value.EncodeToUtf16(units);
            return Insert(index, (ReadOnlySpan<char>)units.Slice(0, count));
        }

        public StringBuilderImpl Insert(int index, char[] value)
        {
            if ((uint)index > (uint)Length) throw new ArgumentOutOfRangeException("index", IndexMustBeLessOrEqual);
            if (value != null) InsertCore(index, value);
            return this;
        }

        public StringBuilderImpl Insert(int index, char[] value, int startIndex, int charCount)
        {
            if ((uint)index > (uint)Length) throw new ArgumentOutOfRangeException("index", IndexMustBeLessOrEqual);
            if (value == null)
            {
                if (startIndex == 0 && charCount == 0) return this;
                throw new ArgumentNullException("value");
            }
            ThrowHelper.IfNegative(startIndex, "startIndex");
            ThrowHelper.IfNegative(charCount, "charCount");
            if (startIndex > value.Length - charCount) throw new ArgumentOutOfRangeException("startIndex", IndexMustBeLessOrEqual);
            if (charCount > 0) InsertCore(index, new ReadOnlySpan<char>(value, startIndex, charCount));
            return this;
        }

        // Formatted values go in as one span with .NET's count path (InsertSpanFormattable: OutOfMemoryException past MaxCapacity).
        public StringBuilderImpl Insert(int index, bool value) => Insert(index, (value ? "True" : "False").AsSpan(), 1);
        public StringBuilderImpl Insert(int index, sbyte value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, byte value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, short value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, ushort value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, int value) => Insert(index, Number.Int32ToDecStr(value).AsSpan(), 1);
        public StringBuilderImpl Insert(int index, uint value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, long value) => Insert(index, Number.Int64ToDecStr(value).AsSpan(), 1);
        public StringBuilderImpl Insert(int index, ulong value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, float value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, double value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, decimal value) => Insert(index, value.ToString().AsSpan(), 1);
        public StringBuilderImpl Insert(int index, object value) => value == null ? this : Insert(index, value.ToString(), 1);

        private void InsertCore(int index, ReadOnlySpan<char> value)
        {
            if (value.Length > 0)
            {
                MakeRoom(index, value.Length, out StringBuilderImpl chunk, out int indexInChunk, false);
                ReplaceInPlaceAtChunk(ref chunk, ref indexInChunk, value);
            }
        }

        public StringBuilderImpl Remove(int startIndex, int length)
        {
            ThrowHelper.IfNegative(length, "length");
            ThrowHelper.IfNegative(startIndex, "startIndex");
            if (length > Length - startIndex) throw new ArgumentOutOfRangeException("length", IndexMustBeLessOrEqual);
            if (Length == length && startIndex == 0)
            {
                Length = 0;
                return this;
            }
            if (length > 0) Remove(startIndex, length, out _, out _);
            return this;
        }

        // ---- replace ---------------------------------------------------------------------------------
        public StringBuilderImpl Replace(string oldValue, string newValue) => Replace(oldValue, newValue, 0, Length);

        public StringBuilderImpl Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue) => Replace(oldValue, newValue, 0, Length);

        public StringBuilderImpl Replace(string oldValue, string newValue, int startIndex, int count)
        {
            if (oldValue == null) throw new ArgumentNullException("oldValue");
            if (oldValue.Length == 0) throw new ArgumentException("The value cannot be an empty string.", "oldValue");
            return Replace(oldValue.AsSpan(), newValue.AsSpan(), startIndex, count);
        }

        public StringBuilderImpl Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue, int startIndex, int count)
        {
            int currentLength = Length;
            if ((uint)startIndex > (uint)currentLength) throw new ArgumentOutOfRangeException("startIndex", IndexMustBeLessOrEqual);
            if (count < 0 || startIndex > currentLength - count) throw new ArgumentOutOfRangeException("count", IndexMustBeLessOrEqual);
            if (oldValue.Length == 0) throw new ArgumentException("Span may not be empty.", "oldValue");

            var replacements = new List<int>(); // replacement positions in the current chunk
            StringBuilderImpl chunk = FindChunkForIndex(startIndex);
            var cursor = new ChunkCursor(this, chunk);
            int indexInChunk = startIndex - chunk._offset;
            while (count > 0)
            {
                // Matches wholly inside the chunk, then the ones that may straddle into the next chunk.
                ReadOnlySpan<char> remainingChunk = new ReadOnlySpan<char>(chunk._chars, indexInChunk, Math.Min(chunk._length - indexInChunk, count));
                while (oldValue.Length <= remainingChunk.Length)
                {
                    int foundPos = remainingChunk.IndexOf(oldValue);
                    if (foundPos >= 0)
                    {
                        indexInChunk += foundPos;
                        replacements.Add(indexInChunk);
                        remainingChunk = remainingChunk.Slice(foundPos + oldValue.Length);
                        indexInChunk += oldValue.Length;
                        count -= foundPos + oldValue.Length;
                        if (count == 0) break;
                    }
                    else
                    {
                        int move = remainingChunk.Length - (oldValue.Length - 1);
                        indexInChunk += move;
                        count -= move;
                        break;
                    }
                }
                while (indexInChunk < chunk._length && count > 0)
                {
                    if (StartsWith(ref cursor, chunk, indexInChunk, count, oldValue))
                    {
                        replacements.Add(indexInChunk);
                        indexInChunk += oldValue.Length;
                        count -= oldValue.Length;
                    }
                    else
                    {
                        indexInChunk++;
                        --count;
                    }
                }
                // Replacing mutates the chunks: go through a logical index, found from a chunk before the
                // mutated ones (the chunk's predecessor stays in place; new chunks only go after it).
                int index = indexInChunk + chunk._offset;
                StringBuilderImpl before = chunk._previous;
                if (replacements.Count != 0)
                {
                    ReplaceAllInChunk(ref cursor, replacements, chunk, oldValue.Length, newValue);
                    index += (newValue.Length - oldValue.Length) * replacements.Count;
                    replacements.Clear();
                }
                chunk = FindForward(ref cursor, before ?? cursor.Head, index);
                indexInChunk = index - chunk._offset;
            }
            cursor.Finish();
            return this;
        }

        public StringBuilderImpl Replace(char oldChar, char newChar) => Replace(oldChar, newChar, 0, Length);

        public StringBuilderImpl Replace(char oldChar, char newChar, int startIndex, int count)
        {
            int currentLength = Length;
            if ((uint)startIndex > (uint)currentLength) throw new ArgumentOutOfRangeException("startIndex", IndexMustBeLessOrEqual);
            if (count < 0 || startIndex > currentLength - count) throw new ArgumentOutOfRangeException("count", IndexMustBeLessOrEqual);
            int endIndex = startIndex + count;
            StringBuilderImpl chunk = this;
            while (true)
            {
                int endIndexInChunk = endIndex - chunk._offset;
                int startIndexInChunk = startIndex - chunk._offset;
                if (endIndexInChunk >= 0)
                {
                    int curInChunk = Math.Max(startIndexInChunk, 0);
                    int endInChunk = Math.Min(chunk._length, endIndexInChunk);
                    Span<char> span = new Span<char>(chunk._chars, curInChunk, endInChunk - curInChunk);
                    for (int i = 0; i < span.Length; i++)
                        if (span[i] == oldChar) span[i] = newChar;
                }
                if (startIndexInChunk >= 0) break;
                chunk = chunk._previous;
            }
            return this;
        }

        public StringBuilderImpl Replace(System.Text.Rune oldRune, System.Text.Rune newRune) => Replace(oldRune, newRune, 0, Length);

        public StringBuilderImpl Replace(System.Text.Rune oldRune, System.Text.Rune newRune, int startIndex, int count)
        {
            Span<char> oldChars = stackalloc char[2];
            Span<char> newChars = stackalloc char[2];
            int oldCount = oldRune.EncodeToUtf16(oldChars);
            int newCount = newRune.EncodeToUtf16(newChars);
            return Replace((ReadOnlySpan<char>)oldChars.Slice(0, oldCount), (ReadOnlySpan<char>)newChars.Slice(0, newCount), startIndex, count);
        }

        // ---- chunks ----------------------------------------------------------------------------------
        public ChunkEnumerator GetChunks() => new ChunkEnumerator(this);

        /// <summary>The chunks from the first (the start of the text) to the builder itself, as .NET enumerates them.</summary>
        [Shadows("System.Text.StringBuilder+ChunkEnumerator")]
        public struct ChunkEnumerator
        {
            private readonly StringBuilderImpl _firstChunk; // the builder object: the end of the text
            private StringBuilderImpl _currentChunk;
            private readonly StringBuilderImpl[] _manyChunks; // first chunk first, for builders of more than 8 chunks
            private int _chunkPos;

            internal ChunkEnumerator(StringBuilderImpl builder)
            {
                _firstChunk = builder;
                _currentChunk = null;
                _manyChunks = null;
                _chunkPos = -1;
                // .NET's trade-off: rescan the predecessor links for a few chunks, an array for more than 8.
                int chunkCount = 0;
                for (StringBuilderImpl c = builder; c != null; c = c._previous) chunkCount++;
                if (8 < chunkCount)
                {
                    _manyChunks = new StringBuilderImpl[chunkCount];
                    StringBuilderImpl c = builder;
                    while (0 <= --chunkCount)
                    {
                        _manyChunks[chunkCount] = c;
                        c = c._previous;
                    }
                }
            }

            public ChunkEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                if (_currentChunk == _firstChunk) return false;
                if (_manyChunks != null)
                {
                    int pos = ++_chunkPos;
                    if (_manyChunks.Length <= pos) return false;
                    _currentChunk = _manyChunks[pos];
                    return true;
                }
                StringBuilderImpl next = _firstChunk;
                while (next._previous != _currentChunk) next = next._previous;
                _currentChunk = next;
                return true;
            }

            public ReadOnlyMemory<char> Current
            {
                get
                {
                    if (_currentChunk == null) throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    return new ReadOnlyMemory<char>(_currentChunk._chars, 0, _currentChunk._length);
                }
            }
        }

        private StringBuilderImpl FindChunkForIndex(int index)
        {
            StringBuilderImpl result = this;
            while (result._offset > index) result = result._previous;
            return result;
        }

        private StringBuilderImpl Next(StringBuilderImpl chunk) => chunk == this ? null : FindChunkForIndex(chunk._offset + chunk._length);

        /// <summary>A new last chunk of max(needed, min(Length, 8000)) chars; the current contents move to a predecessor object.</summary>
        private void ExpandByABlock(int minBlockCharCount)
        {
            if ((minBlockCharCount + Length) > _maxCapacity || minBlockCharCount + Length < minBlockCharCount)
                throw new ArgumentOutOfRangeException("requiredLength", SmallCapacity);
            int newBlockLength = Math.Max(minBlockCharCount, Math.Min(Length, MaxChunkSize));
            if (_offset + _length + newBlockLength < newBlockLength) throw new OutOfMemoryException();
            char[] chunkChars = new char[newBlockLength];
            _previous = new StringBuilderImpl(this);
            _offset += _length;
            _length = 0;
            _chars = chunkChars;
        }

        /// <summary>Room for <paramref name="count"/> chars at <paramref name="index"/>: in place in a small chunk, else a new chunk before it.</summary>
        private void MakeRoom(int index, int count, out StringBuilderImpl chunk, out int indexInChunk, bool doNotMoveFollowingChars)
        {
            if (count + Length > _maxCapacity || count + Length < count)
                throw new ArgumentOutOfRangeException("requiredLength", SmallCapacity);
            ForgetLookup();
            chunk = this;
            while (chunk._offset > index)
            {
                chunk._offset += count;
                chunk = chunk._previous;
            }
            indexInChunk = index - chunk._offset;

            // Space left in a small chunk (repeated small inserts at one spot): slide its tail over.
            if (!doNotMoveFollowingChars && chunk._length <= DefaultCapacity * 2 && chunk._chars.Length - chunk._length >= count)
            {
                for (int i = chunk._length; i > indexInChunk;)
                {
                    --i;
                    chunk._chars[i + count] = chunk._chars[i];
                }
                chunk._length += count;
                return;
            }

            // A new chunk before the current one, holding the head of the current chunk's chars.
            StringBuilderImpl newChunk = new StringBuilderImpl(Math.Max(count, DefaultCapacity), chunk._maxCapacity, chunk._previous);
            newChunk._length = count;
            int copyCount1 = Math.Min(count, indexInChunk);
            if (copyCount1 > 0)
            {
                new ReadOnlySpan<char>(chunk._chars, 0, copyCount1).CopyTo(newChunk._chars);
                int copyCount2 = indexInChunk - copyCount1;
                if (copyCount2 >= 0)
                {
                    new ReadOnlySpan<char>(chunk._chars, copyCount1, copyCount2).CopyTo(chunk._chars);
                    indexInChunk = copyCount2;
                }
            }
            chunk._previous = newChunk;
            chunk._offset += count;
            if (copyCount1 < count)
            {
                chunk = newChunk;
                indexInChunk = copyCount1;
            }
        }

        /// <summary>Removes <paramref name="count"/> chars at <paramref name="startIndex"/>, dropping the chunks wholly inside the range.</summary>
        private void Remove(int startIndex, int count, out StringBuilderImpl chunk, out int indexInChunk)
        {
            ForgetLookup();
            int endIndex = startIndex + count;
            chunk = this;
            StringBuilderImpl endChunk = null;
            int endIndexInChunk = 0;
            while (true)
            {
                if (endIndex - chunk._offset >= 0)
                {
                    if (endChunk == null)
                    {
                        endChunk = chunk;
                        endIndexInChunk = endIndex - endChunk._offset;
                    }
                    if (startIndex - chunk._offset >= 0)
                    {
                        indexInChunk = startIndex - chunk._offset;
                        break;
                    }
                }
                else chunk._offset -= count;
                chunk = chunk._previous;
            }
            int copyTargetIndexInChunk = indexInChunk;
            int copyCount = endChunk._length - endIndexInChunk;
            if (endChunk != chunk)
            {
                copyTargetIndexInChunk = 0;
                chunk._length = indexInChunk;
                endChunk._previous = chunk;
                endChunk._offset = chunk._offset + chunk._length;
                if (indexInChunk == 0)
                {
                    endChunk._previous = chunk._previous;
                    chunk = endChunk;
                }
            }
            endChunk._length -= endIndexInChunk - copyTargetIndexInChunk;
            if (copyTargetIndexInChunk != endIndexInChunk)
                new ReadOnlySpan<char>(endChunk._chars, endIndexInChunk, copyCount).CopyTo(new Span<char>(endChunk._chars, copyTargetIndexInChunk, copyCount));
        }

        /// <summary>
        /// Replace walks the chunk list front to back, so where .NET finds every next chunk and shifts every later
        /// chunk's offset from the end (quadratic in the number of chunks, which Replace itself multiplies), the
        /// cursor links the chunks forward (through <see cref="_lookup"/>, the indexer cache, unused meanwhile) and
        /// keeps the chunks past <see cref="Frontier"/> one pending shift behind, applied as the walk reaches them.
        /// The chunks and their contents come out exactly as .NET's; <see cref="Finish"/> settles the offsets
        /// (also before Replace throws) and drops the links.
        /// </summary>
        private struct ChunkCursor
        {
            public readonly StringBuilderImpl Builder; // the last chunk
            public StringBuilderImpl Head;             // the first chunk
            public StringBuilderImpl Frontier;         // offsets are current up to here, Pending behind after it
            public int Pending;
            public int Length;

            public ChunkCursor(StringBuilderImpl builder, StringBuilderImpl start)
            {
                Builder = builder;
                Length = builder.Length;
                builder.ForgetLookup();
                StringBuilderImpl c = builder;
                c._lookup = null;
                while (c._previous != null)
                {
                    c._previous._lookup = c;
                    c = c._previous;
                }
                Head = c;
                Frontier = start;
                Pending = 0;
            }

            /// <summary>The chunk after <paramref name="c"/> (a current one), its offset brought up to date.</summary>
            public StringBuilderImpl After(StringBuilderImpl c)
            {
                StringBuilderImpl next = c._lookup;
                if (c == Frontier && next != null)
                {
                    next._offset += Pending;
                    Frontier = next;
                }
                return next;
            }

            /// <summary>Adds <paramref name="delta"/> to the offsets of the chunks after <paramref name="c"/>.</summary>
            public void ShiftAfter(StringBuilderImpl c, int delta)
            {
                while (c != Frontier)
                {
                    c = c._lookup;
                    c._offset += delta;
                }
                Pending += delta;
            }

            public void Finish()
            {
                StringBuilderImpl c = Frontier;
                while (c._lookup != null)
                {
                    c = c._lookup;
                    c._offset += Pending;
                }
                Pending = 0;
                Frontier = c;
                for (c = Builder; c != null; c = c._previous) c._lookup = null;
            }
        }

        /// <summary>FindChunkForIndex(index) walking forward from <paramref name="from"/> (at or before the index).</summary>
        private static StringBuilderImpl FindForward(ref ChunkCursor cursor, StringBuilderImpl from, int index)
        {
            while (true)
            {
                StringBuilderImpl next = cursor.After(from);
                if (next == null || next._offset > index) return from;
                from = next;
            }
        }

        /// <summary>.NET's Next(chunk): the last chunk starting at or before the end of <paramref name="chunk"/>.</summary>
        private static StringBuilderImpl Next(ref ChunkCursor cursor, StringBuilderImpl chunk)
        {
            if (chunk == cursor.Builder) return null;
            return FindForward(ref cursor, cursor.After(chunk), chunk._offset + chunk._length);
        }

        private static void ReplaceAllInChunk(ref ChunkCursor cursor, List<int> replacements, StringBuilderImpl sourceChunk, int removeCount, ReadOnlySpan<char> value)
        {
            long longDelta = (value.Length - removeCount) * (long)replacements.Count;
            int delta = (int)longDelta;
            if (delta != longDelta)
            {
                cursor.Finish();
                throw new OutOfMemoryException();
            }
            StringBuilderImpl targetChunk = sourceChunk;
            int targetIndexInChunk = replacements[0];
            if (delta > 0) MakeRoomBefore(ref cursor, sourceChunk, targetIndexInChunk, delta, out targetChunk, out targetIndexInChunk);
            int i = 0;
            while (true)
            {
                ReplaceInPlaceAtChunk(ref cursor, ref targetChunk, ref targetIndexInChunk, value);
                int gapStart = replacements[i] + removeCount;
                i++;
                if (i >= replacements.Count) break;
                int gapEnd = replacements[i];
                if (delta != 0)
                    ReplaceInPlaceAtChunk(ref cursor, ref targetChunk, ref targetIndexInChunk, new ReadOnlySpan<char>(sourceChunk._chars, gapStart, gapEnd - gapStart));
                else
                    targetIndexInChunk += gapEnd - gapStart;
            }
            if (delta < 0) Remove(ref cursor, targetChunk, targetChunk._offset + targetIndexInChunk, -delta);
            cursor.Length += delta;
        }

        /// <summary>MakeRoom(index, count, doNotMoveFollowingChars: true) at <paramref name="indexInChunk"/> of <paramref name="chunk"/>.</summary>
        private static void MakeRoomBefore(ref ChunkCursor cursor, StringBuilderImpl chunk, int indexInChunk, int count, out StringBuilderImpl target, out int targetIndexInChunk)
        {
            if (count + cursor.Length > chunk._maxCapacity || count + cursor.Length < count)
            {
                cursor.Finish();
                throw new ArgumentOutOfRangeException("requiredLength", SmallCapacity);
            }
            cursor.ShiftAfter(chunk, count);
            StringBuilderImpl previous = chunk._previous;
            StringBuilderImpl newChunk = new StringBuilderImpl(Math.Max(count, DefaultCapacity), chunk._maxCapacity, previous);
            newChunk._length = count;
            newChunk._lookup = chunk;
            if (previous != null) previous._lookup = newChunk;
            else cursor.Head = newChunk;
            int copyCount1 = Math.Min(count, indexInChunk);
            if (copyCount1 > 0)
            {
                new ReadOnlySpan<char>(chunk._chars, 0, copyCount1).CopyTo(newChunk._chars);
                int copyCount2 = indexInChunk - copyCount1;
                if (copyCount2 >= 0)
                {
                    new ReadOnlySpan<char>(chunk._chars, copyCount1, copyCount2).CopyTo(chunk._chars);
                    indexInChunk = copyCount2;
                }
            }
            chunk._previous = newChunk;
            chunk._offset += count;
            target = chunk;
            targetIndexInChunk = indexInChunk;
            if (copyCount1 < count)
            {
                target = newChunk;
                targetIndexInChunk = copyCount1;
            }
        }

        /// <summary>Remove(startIndex, count, ...) with the chunks found forward from <paramref name="from"/>.</summary>
        private static void Remove(ref ChunkCursor cursor, StringBuilderImpl from, int startIndex, int count)
        {
            int endIndex = startIndex + count;
            StringBuilderImpl chunk = FindForward(ref cursor, from, startIndex);
            StringBuilderImpl endChunk = FindForward(ref cursor, chunk, endIndex);
            cursor.ShiftAfter(endChunk, -count);
            int indexInChunk = startIndex - chunk._offset;
            int endIndexInChunk = endIndex - endChunk._offset;
            int copyTargetIndexInChunk = indexInChunk;
            int copyCount = endChunk._length - endIndexInChunk;
            if (endChunk != chunk)
            {
                copyTargetIndexInChunk = 0;
                chunk._length = indexInChunk;
                endChunk._previous = chunk;
                chunk._lookup = endChunk;
                endChunk._offset = chunk._offset + chunk._length;
                if (indexInChunk == 0)
                {
                    StringBuilderImpl previous = chunk._previous;
                    endChunk._previous = previous;
                    if (previous != null) previous._lookup = endChunk;
                    else cursor.Head = endChunk;
                }
            }
            endChunk._length -= endIndexInChunk - copyTargetIndexInChunk;
            if (copyTargetIndexInChunk != endIndexInChunk)
                new ReadOnlySpan<char>(endChunk._chars, endIndexInChunk, copyCount).CopyTo(new Span<char>(endChunk._chars, copyTargetIndexInChunk, copyCount));
        }

        private static bool StartsWith(ref ChunkCursor cursor, StringBuilderImpl chunk, int indexInChunk, int count, ReadOnlySpan<char> value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (count == 0) return false;
                if (indexInChunk >= chunk._length)
                {
                    chunk = Next(ref cursor, chunk);
                    if (chunk == null) return false;
                    indexInChunk = 0;
                }
                if (value[i] != chunk._chars[indexInChunk]) return false;
                indexInChunk++;
                --count;
            }
            return true;
        }

        private static void ReplaceInPlaceAtChunk(ref ChunkCursor cursor, ref StringBuilderImpl chunk, ref int indexInChunk, ReadOnlySpan<char> value)
        {
            while (value.Length != 0)
            {
                int lengthInChunk = chunk._length - indexInChunk;
                int lengthToCopy = Math.Min(lengthInChunk, value.Length);
                value.Slice(0, lengthToCopy).CopyTo(new Span<char>(chunk._chars, indexInChunk, lengthToCopy));
                indexInChunk += lengthToCopy;
                if (indexInChunk >= chunk._length)
                {
                    chunk = Next(ref cursor, chunk);
                    indexInChunk = 0;
                }
                value = value.Slice(lengthToCopy);
            }
        }

        /// <summary>Overwrites chars from (chunk, indexInChunk) on with <paramref name="value"/>, moving into following chunks.</summary>
        private void ReplaceInPlaceAtChunk(ref StringBuilderImpl chunk, ref int indexInChunk, ReadOnlySpan<char> value)
        {
            while (value.Length != 0)
            {
                int lengthInChunk = chunk._length - indexInChunk;
                int lengthToCopy = Math.Min(lengthInChunk, value.Length);
                value.Slice(0, lengthToCopy).CopyTo(new Span<char>(chunk._chars, indexInChunk, lengthToCopy));
                indexInChunk += lengthToCopy;
                if (indexInChunk >= chunk._length)
                {
                    chunk = Next(chunk);
                    indexInChunk = 0;
                }
                value = value.Slice(lengthToCopy);
            }
        }

        // ---- entry points for compiler-synthesized code (records, anonymous types, interpolation) ----------
        internal static StringBuilderImpl RtNew(int capacity) => new StringBuilderImpl(capacity);
        internal static StringBuilderImpl RtAppend(StringBuilderImpl sb, string value) => sb.Append(value);
        internal static string RtToString(StringBuilderImpl sb) => sb.ToString();

        /// <summary>
        /// AppendInterpolatedStringHandler.AppendFormatted with an alignment, as .NET appends it: right-aligned
        /// values get their padding first, left-aligned ones after (two appends, which decide where chunks split).
        /// A null value formats as nothing.
        /// </summary>
        internal static StringBuilderImpl RtAppendAligned(StringBuilderImpl sb, string value, int alignment)
        {
            value ??= string.Empty;
            if (alignment == 0) return sb.Append(value);
            if (alignment < 0)
            {
                sb.Append(value);
                int padding = -alignment - value.Length;
                if (padding > 0) sb.Append(' ', padding);
                return sb;
            }
            int leftPadding = alignment - value.Length;
            if (leftPadding > 0) sb.Append(' ', leftPadding);
            return sb.Append(value);
        }
    }
}
