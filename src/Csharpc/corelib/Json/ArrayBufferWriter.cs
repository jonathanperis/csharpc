// System.Buffers.ArrayBufferWriter<T>: the IBufferWriter<T> most Utf8JsonWriter samples use.
//
// Ported from dotnet/runtime src/libraries/Common/src/System/Buffers/ArrayBufferWriter.cs (release/11.0).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [Shadows("System.Buffers.ArrayBufferWriter`1")]
    public sealed class ArrayBufferWriterImpl<T> : IBufferWriter<T>
    {
        private const int DefaultInitialBufferSize = 256;
        private T[] _buffer;
        private int _index;

        public ArrayBufferWriterImpl()
        {
            _buffer = new T[0];
            _index = 0;
        }

        public ArrayBufferWriterImpl(int initialCapacity)
        {
            if (initialCapacity <= 0) throw new ArgumentException("Value must be positive.", "initialCapacity");
            _buffer = new T[initialCapacity];
            _index = 0;
        }

        public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, _index);

        public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _index);

        public int WrittenCount => _index;

        public int Capacity => _buffer.Length;

        public int FreeCapacity => _buffer.Length - _index;

        public void Clear()
        {
            Array.Clear(_buffer, 0, _index);
            _index = 0;
        }

        public void ResetWrittenCount() => _index = 0;

        public void Advance(int count)
        {
            if (count < 0) throw new ArgumentException("Value must be non-negative.", "count");
            if (_index > _buffer.Length - count)
                throw new InvalidOperationException("Cannot advance past the end of the buffer, which has a size of " + _buffer.Length.ToString() + ".");
            _index += count;
        }

        public Memory<T> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_index);
        }

        public Span<T> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_index);
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentException("Value must be non-negative.", "sizeHint");
            if (sizeHint == 0) sizeHint = 1;
            if (sizeHint > FreeCapacity)
            {
                int currentLength = _buffer.Length;
                int growBy = Math.Max(sizeHint, currentLength);
                if (currentLength == 0) growBy = Math.Max(growBy, DefaultInitialBufferSize);
                int newSize = currentLength + growBy;
                T[] grown = new T[newSize];
                Array.Copy(_buffer, grown, _index);
                _buffer = grown;
            }
        }
    }
}
