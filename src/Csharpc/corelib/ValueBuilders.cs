// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Stand-ins for .NET's internal ValueStringBuilder / ValueListBuilder (without ArrayPool), shared by the
// ports of .NET code in the corelib (Regex, Globalization). Ported from dotnet/runtime (release/11.0,
// commit bc123d0bd173); see THIRD-PARTY-NOTICES.md.
#nullable enable annotations

using System;

namespace CsharpcCoreLib
{
    internal ref struct ValueStringBuilder
    {
        private Span<char> _chars;
        private int _pos;

        public ValueStringBuilder(Span<char> initialBuffer)
        {
            _chars = initialBuffer;
            _pos = 0;
        }

        public ValueStringBuilder(int initialCapacity)
        {
            _chars = new char[initialCapacity];
            _pos = 0;
        }

        public int Length
        {
            get => _pos;
            set => _pos = value;
        }

        public int Capacity => _chars.Length;

        public ref char this[int index] => ref _chars[index];

        public override string ToString() => _chars.Slice(0, _pos).ToString();

        public ReadOnlySpan<char> AsSpan() => _chars.Slice(0, _pos);
        public ReadOnlySpan<char> AsSpan(int start) => _chars.Slice(start, _pos - start);
        public ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);

        public void Insert(int index, char value, int count)
        {
            if (_pos > _chars.Length - count)
            {
                Grow(count);
            }

            int remaining = _pos - index;
            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            _chars.Slice(index, count).Fill(value);
            _pos += count;
        }

        public void Append(char c)
        {
            if ((uint)_pos >= (uint)_chars.Length)
            {
                Grow(1);
            }

            _chars[_pos++] = c;
        }

        public void Append(string? s)
        {
            if (s == null)
            {
                return;
            }

            Append(s.AsSpan());
        }

        public void Append(char c, int count)
        {
            if (_pos > _chars.Length - count)
            {
                Grow(count);
            }

            for (int i = 0; i < count; i++)
            {
                _chars[_pos + i] = c;
            }
            _pos += count;
        }

        public void Append(scoped ReadOnlySpan<char> value)
        {
            if (_pos > _chars.Length - value.Length)
            {
                Grow(value.Length);
            }

            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        public Span<char> AppendSpan(int length)
        {
            int origPos = _pos;
            if (origPos > _chars.Length - length)
            {
                Grow(length);
            }

            _pos = origPos + length;
            return _chars.Slice(origPos, length);
        }

        private void Grow(int additionalCapacityBeyondPos)
        {
            int newCapacity = (int)Math.Max((uint)(_pos + additionalCapacityBeyondPos), Math.Min((uint)_chars.Length * 2, 0x7FFFFFC7u));
            char[] array = new char[newCapacity];
            _chars.Slice(0, _pos).CopyTo(array);
            _chars = array;
        }

        public void Dispose()
        {
            this = default;
        }
    }

    internal ref struct ValueListBuilder<T>
    {
        private Span<T> _span;
        private int _pos;

        public ValueListBuilder(Span<T> scratchBuffer)
        {
            _span = scratchBuffer;
            _pos = 0;
        }

        public ValueListBuilder(int capacity)
        {
            _span = new T[capacity];
            _pos = 0;
        }

        public int Length
        {
            get => _pos;
            set => _pos = value;
        }

        public ref T this[int index] => ref _span[index];

        public void Append(T item)
        {
            if ((uint)_pos >= (uint)_span.Length)
            {
                Grow(1);
            }

            _span[_pos++] = item;
        }

        public void Append(scoped ReadOnlySpan<T> source)
        {
            if (_pos > _span.Length - source.Length)
            {
                Grow(source.Length);
            }

            source.CopyTo(_span.Slice(_pos));
            _pos += source.Length;
        }

        public Span<T> AppendSpan(int length)
        {
            int origPos = _pos;
            if (origPos > _span.Length - length)
            {
                Grow(length);
            }

            _pos = origPos + length;
            return _span.Slice(origPos, length);
        }

        public T Pop()
        {
            _pos--;
            return _span[_pos];
        }

        public ReadOnlySpan<T> AsSpan() => _span.Slice(0, _pos);

        public void Dispose()
        {
            this = default;
        }

        private void Grow(int additionalCapacityBeyondPos)
        {
            int nextCapacity = Math.Max(_span.Length != 0 ? _span.Length * 2 : 4, _pos + additionalCapacityBeyondPos);
            T[] array = new T[nextCapacity];
            _span.Slice(0, _pos).CopyTo(array);
            _span = array;
        }
    }
}
