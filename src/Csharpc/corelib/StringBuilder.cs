// System.Text.StringBuilder in C# (formerly src/runtime/strings.c + bclmisc.c): a growable char buffer.
// Exception types and messages follow .NET 11 exactly (tests/samples/161_stringbuilder.cs).
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.StringBuilder")]
    public sealed class StringBuilderImpl
    {
        private const int DefaultCapacity = 16;
        private char[] _chars;
        private int _length;

        public StringBuilderImpl() { _chars = new char[DefaultCapacity]; }

        public StringBuilderImpl(int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            _chars = new char[capacity > 0 ? capacity : DefaultCapacity];
        }

        public StringBuilderImpl(string value) : this(value, value == null ? DefaultCapacity : value.Length) { }

        public StringBuilderImpl(string value, int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            int length = value == null ? 0 : value.Length;
            _chars = new char[Math.Max(Math.Max(capacity, length), DefaultCapacity)];
            if (length > 0) { value.AsSpan().CopyTo(_chars); _length = length; }
        }

        public StringBuilderImpl(string value, int startIndex, int length, int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            ThrowHelper.IfNegative(length, "length");
            ThrowHelper.IfNegative(startIndex, "startIndex");
            value ??= "";
            if (startIndex > value.Length - length)
                throw new ArgumentOutOfRangeException("length", "Index and length must refer to a location within the string.");
            _chars = new char[Math.Max(Math.Max(capacity, length), DefaultCapacity)];
            value.AsSpan(startIndex, length).CopyTo(_chars);
            _length = length;
        }

        // ---- size --------------------------------------------------------------------------------
        public int Length
        {
            get => _length;
            set
            {
                ThrowHelper.IfNegative(value, "value");
                if (value > _length)
                {
                    Reserve(value - _length);
                    _chars.AsSpan(_length, value - _length).Clear(); // new chars are '\0'
                }
                _length = value;
            }
        }

        public int Capacity
        {
            get => _chars.Length;
            set
            {
                ThrowHelper.IfNegative(value, "value");
                if (value < _length) throw new ArgumentOutOfRangeException("value", "Capacity cannot be less than the current size.");
                if (value != _chars.Length) Resize(value);
            }
        }

        public int EnsureCapacity(int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            if (capacity > _chars.Length) Resize(capacity);
            return _chars.Length;
        }

        private void Resize(int capacity)
        {
            char[] grown = new char[capacity];
            _chars.AsSpan(0, _length).CopyTo(grown);
            _chars = grown;
        }

        /// <summary>
        /// Makes room for `extra` more chars. Every append calls this, so it is only the capacity test;
        /// growing lives in Grow, which keeps this small enough for LLVM to inline into the callers.
        /// </summary>
        private void Reserve(int extra)
        {
            if (_length + extra > _chars.Length) Grow(extra);
        }

        /// <summary>Doubling, like .NET's amortized growth.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void Grow(int extra)
        {
            int needed = _length + extra;
            int capacity = _chars.Length * 2;
            Resize(capacity < needed ? needed : capacity);
        }

        [System.Runtime.CompilerServices.IndexerName("Chars")] // the real indexer's metadata name
        public char this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_length) throw new IndexOutOfRangeException();
                return _chars[index];
            }
            set
            {
                if ((uint)index >= (uint)_length)
                    throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
                _chars[index] = value;
            }
        }

        // ---- append ------------------------------------------------------------------------------
        public StringBuilderImpl Append(string value)
        {
            if (value != null) Append(value.AsSpan());
            return This;
        }

        public StringBuilderImpl Append(ReadOnlySpan<char> value)
        {
            Reserve(value.Length);
            value.CopyTo(_chars.AsSpan(_length));
            _length += value.Length;
            return This;
        }

        public StringBuilderImpl Append(string value, int startIndex, int count)
        {
            if (value == null)
            {
                if (startIndex == 0 && count == 0) return This;
                throw new ArgumentNullException("value");
            }
            ThrowHelper.IfNegative(startIndex, "startIndex");
            ThrowHelper.IfNegative(count, "count");
            if (startIndex > value.Length - count)
                throw new ArgumentOutOfRangeException("startIndex", "Index and count must refer to a location within the string.");
            return Append(value.AsSpan(startIndex, count));
        }

        public StringBuilderImpl Append(char value)
        {
            if (_length == _chars.Length) Reserve(1);
            _chars[_length++] = value;
            return This;
        }

        /// <summary>The rune's UTF-16 code units (one, or a surrogate pair).</summary>
        public StringBuilderImpl Append(System.Text.Rune value)
        {
            if (value.IsBmp) return Append((char)value.Value);
            int v = value.Value - 0x10000;
            Append((char)(0xD800 + (v >> 10)));
            return Append((char)(0xDC00 + (v & 0x3FF)));
        }

        public StringBuilderImpl Append(char value, int repeatCount)
        {
            ThrowHelper.IfNegative(repeatCount, "repeatCount");
            Reserve(repeatCount);
            _chars.AsSpan(_length, repeatCount).Fill(value);
            _length += repeatCount;
            return This;
        }

        public StringBuilderImpl Append(char[] value)
        {
            if (value != null) Append(new ReadOnlySpan<char>(value));
            return This;
        }

        public StringBuilderImpl Append(char[] value, int startIndex, int charCount)
        {
            if (value == null)
            {
                if (startIndex == 0 && charCount == 0) return This;
                throw new ArgumentNullException("value");
            }
            ThrowHelper.IfNegative(startIndex, "startIndex");
            ThrowHelper.IfNegative(charCount, "charCount");
            if (charCount > value.Length - startIndex)
                throw new ArgumentOutOfRangeException("charCount", "Index and count must refer to a location within the string.");
            return Append(new ReadOnlySpan<char>(value, startIndex, charCount));
        }

        public StringBuilderImpl Append(StringBuilderImpl value)
        {
            if (value != null) Append(value.ToString());
            return This;
        }

        /// <summary>
        /// Integers are formatted straight into the buffer — no temporary string, and no range checks:
        /// Reserve(chars) just made room for exactly the chars written (chars ≤ 20, so `_length + chars`
        /// can't overflow: _length ≤ Array.MaxLength).
        /// </summary>
        private unsafe StringBuilderImpl AppendSigned(long value)
        {
            // Negative values take the current culture's NegativeSign (Append(int) formats like ToString()).
            if (value < 0) return Append(Number.Int64ToDecStr(value));
            int chars = Number.CountChars(value);
            Reserve(chars);
            fixed (char* buffer = _chars) Number.WriteSigned(buffer + _length, chars, value);
            _length += chars;
            return This;
        }

        private unsafe StringBuilderImpl AppendUnsigned(ulong value)
        {
            int chars = Number.CountDigits(value);
            Reserve(chars);
            fixed (char* buffer = _chars) Number.WriteUnsigned(buffer + _length, chars, value);
            _length += chars;
            return This;
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
        public StringBuilderImpl Append(object value) => value == null ? This : Append(value.ToString());

        // Composite formatting shares string.Format's engine (Number.FormatComposite[With]).
        public StringBuilderImpl AppendFormat(string format, object arg0) => Append(Number.FormatComposite(format, new[] { arg0 }));
        public StringBuilderImpl AppendFormat(string format, object arg0, object arg1) => Append(Number.FormatComposite(format, new[] { arg0, arg1 }));
        public StringBuilderImpl AppendFormat(string format, object arg0, object arg1, object arg2) =>
            Append(Number.FormatComposite(format, new[] { arg0, arg1, arg2 }));
        public StringBuilderImpl AppendFormat(string format, params object[] args)
        {
            if (args == null) throw new ArgumentNullException(format == null ? "format" : "args");
            return Append(Number.FormatComposite(format, args));
        }
        public StringBuilderImpl AppendFormat(string format, params ReadOnlySpan<object> args) => Append(Number.FormatComposite(format, args.ToArray()));
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, object arg0) => Append(Number.FormatCompositeWith(provider, format, new[] { arg0 }));
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, object arg0, object arg1) => Append(Number.FormatCompositeWith(provider, format, new[] { arg0, arg1 }));
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, object arg0, object arg1, object arg2) =>
            Append(Number.FormatCompositeWith(provider, format, new[] { arg0, arg1, arg2 }));
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, params object[] args)
        {
            if (args == null) throw new ArgumentNullException(format == null ? "format" : "args");
            return Append(Number.FormatCompositeWith(provider, format, args));
        }
        public StringBuilderImpl AppendFormat(IFormatProvider provider, string format, params ReadOnlySpan<object> args) =>
            Append(Number.FormatCompositeWith(provider, format, args.ToArray()));

        public StringBuilderImpl AppendLine() => Append(global::System.Environment.NewLine);
        public StringBuilderImpl AppendLine(string value) => Append(value).Append(global::System.Environment.NewLine);

        // ---- AppendJoin ----------------------------------------------------------------------------
        public StringBuilderImpl AppendJoin(string separator, params string[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        public StringBuilderImpl AppendJoin(string separator, params object[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        public StringBuilderImpl AppendJoin<T>(string separator, IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            bool first = true;
            foreach (T value in values)
            {
                if (!first) Append(separator);
                first = false;
                if (value != null) Append(value.ToString());
            }
            return This;
        }

        public StringBuilderImpl AppendJoin(string separator, params ReadOnlySpan<string> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        public StringBuilderImpl AppendJoin(string separator, params ReadOnlySpan<object> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        public StringBuilderImpl AppendJoin(char separator, params string[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        public StringBuilderImpl AppendJoin(char separator, params object[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        public StringBuilderImpl AppendJoin<T>(char separator, IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            bool first = true;
            foreach (T value in values)
            {
                if (!first) Append(separator);
                first = false;
                if (value != null) Append(value.ToString());
            }
            return This;
        }

        public StringBuilderImpl AppendJoin(char separator, params ReadOnlySpan<string> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        public StringBuilderImpl AppendJoin(char separator, params ReadOnlySpan<object> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) Append(separator);
                Append(values[i]);
            }
            return This;
        }

        // ---- insert / remove / replace -----------------------------------------------------------
        public StringBuilderImpl Insert(int index, string value)
        {
            if ((uint)index > (uint)_length) ThrowHelper.IndexMustBeLessOrEqual("index");
            if (string.IsNullOrEmpty(value)) return This;
            Reserve(value.Length);
            _chars.AsSpan(index, _length - index).CopyTo(_chars.AsSpan(index + value.Length));
            value.AsSpan().CopyTo(_chars.AsSpan(index));
            _length += value.Length;
            return This;
        }

        public StringBuilderImpl Insert(int index, char value)
        {
            if ((uint)index > (uint)_length) ThrowHelper.IndexMustBeLessOrEqual("index");
            Reserve(1);
            _chars.AsSpan(index, _length - index).CopyTo(_chars.AsSpan(index + 1));
            _chars[index] = value;
            _length++;
            return This;
        }

        public StringBuilderImpl Insert(int index, int value) => Insert(index, Number.Int32ToDecStr(value));
        public StringBuilderImpl Insert(int index, long value) => Insert(index, Number.Int64ToDecStr(value));
        public StringBuilderImpl Insert(int index, bool value) => Insert(index, value ? "True" : "False");
        public StringBuilderImpl Insert(int index, double value) => Insert(index, value.ToString());
        public StringBuilderImpl Insert(int index, object value) => value == null ? This : Insert(index, value.ToString());

        public StringBuilderImpl Remove(int startIndex, int length)
        {
            ThrowHelper.IfNegative(length, "length");
            ThrowHelper.IfNegative(startIndex, "startIndex");
            if (length > _length - startIndex) ThrowHelper.IndexMustBeLessOrEqual("length");
            if (length > 0)
            {
                _chars.AsSpan(startIndex + length, _length - startIndex - length).CopyTo(_chars.AsSpan(startIndex));
                _length -= length;
            }
            return This;
        }

        public StringBuilderImpl Replace(char oldChar, char newChar)
        {
            Span<char> chars = _chars.AsSpan(0, _length);
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] == oldChar) chars[i] = newChar;
            return This;
        }

        public StringBuilderImpl Replace(char oldChar, char newChar, int startIndex, int count)
        {
            CheckReplaceRange(startIndex, count);
            Span<char> chars = _chars.AsSpan(startIndex, count);
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] == oldChar) chars[i] = newChar;
            return This;
        }

        public StringBuilderImpl Replace(string oldValue, string newValue, int startIndex, int count)
        {
            if (oldValue == null) throw new ArgumentNullException("oldValue");
            if (oldValue.Length == 0) throw new ArgumentException("The value cannot be an empty string.", "oldValue");
            CheckReplaceRange(startIndex, count);
            // Matches lie wholly inside the range, left to right without overlapping: string.Replace on it.
            string range = new string(_chars, startIndex, count);
            string replaced = range.Replace(oldValue, newValue);
            if ((object)replaced != range)
            {
                string tail = new string(_chars, startIndex + count, _length - startIndex - count);
                _length = startIndex;
                Append(replaced);
                Append(tail);
            }
            return This;
        }

        private void CheckReplaceRange(int startIndex, int count)
        {
            if ((uint)startIndex > (uint)_length)
                throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (count < 0 || startIndex > _length - count)
                throw new ArgumentOutOfRangeException("count", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
        }

        public StringBuilderImpl Replace(string oldValue, string newValue)
        {
            if (oldValue == null) throw new ArgumentNullException("oldValue");
            if (oldValue.Length == 0) throw new ArgumentException("The value cannot be an empty string.", "oldValue");
            string current = ToString();
            string replaced = current.Replace(oldValue, newValue);
            if ((object)replaced != current)
            {
                _length = 0;
                Append(replaced);
            }
            return This;
        }

        public StringBuilderImpl Clear()
        {
            _length = 0;
            return This;
        }

        // ---- output -------------------------------------------------------------------------------
        public override string ToString()
        {
            string result = CoreIntrinsics.FastAllocateString(_length);
            _chars.AsSpan(0, _length).CopyTo(CoreIntrinsics.StringChars(result));
            return result;
        }

        public string ToString(int startIndex, int length)
        {
            ThrowHelper.IfNegative(startIndex, "startIndex");
            ThrowHelper.IfNegative(length, "length");
            if (startIndex > _length - length)
                throw new ArgumentOutOfRangeException("length", "Index and length must refer to a location within the string.");
            string result = CoreIntrinsics.FastAllocateString(length);
            _chars.AsSpan(startIndex, length).CopyTo(CoreIntrinsics.StringChars(result));
            return result;
        }

        /// <summary>
        /// The builder's text as read-only memory chunks. csharpc's builder is one buffer, so this is a
        /// single chunk with the whole text; .NET links chunks of up to 8000 chars, so the concatenated
        /// content is the same but the number and lengths of the chunks can differ (docs/18-compatibility.md).
        /// </summary>
        public ChunkEnumerator GetChunks() => new ChunkEnumerator(this);

        [Shadows("System.Text.StringBuilder+ChunkEnumerator")]
        public struct ChunkEnumerator
        {
            private readonly StringBuilderImpl _builder;
            private int _state; // 0 before the chunk, 1 on it, 2 past it

            internal ChunkEnumerator(StringBuilderImpl builder) { _builder = builder; _state = 0; }

            public ChunkEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                if (_state >= 1) { _state = 2; return false; }
                _state = 1;
                return true;
            }

            public ReadOnlyMemory<char> Current
            {
                get
                {
                    if (_state != 1) throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    return new ReadOnlyMemory<char>(_builder._chars, 0, _builder._length);
                }
            }
        }

        // Shadow types are matched by their real names (CoreLib.Signature) and return types are ignored,
        // so members can take and return StringBuilderImpl directly.
        private StringBuilderImpl This => this;

        // ---- entry points for compiler-synthesized code (records, anonymous types) ----------------
        internal static StringBuilderImpl RtNew(int capacity) => new StringBuilderImpl(capacity);
        internal static StringBuilderImpl RtAppend(StringBuilderImpl sb, string value) => sb.Append(value);
        internal static string RtToString(StringBuilderImpl sb) => sb.ToString();
    }
}
