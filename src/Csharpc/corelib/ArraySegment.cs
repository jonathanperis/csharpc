// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/ArraySegment.cs;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md): ThrowHelper calls
// inlined with .NET's messages, a lazily created Empty (no static initializers in generic corelib types).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.ArraySegment`1")]
    public readonly struct ArraySegmentImpl<T> : IList<T>, IReadOnlyList<T>
    {
        // .NET allocates one empty array per instantiation (not Array.Empty<T>()); ToArray() of an empty
        // segment returns it, so it must be the same instance every time.
        private static T[] s_emptyArray;
        private static T[] EmptyArray => s_emptyArray ??= new T[0];

        public static ArraySegment<T> Empty => new ArraySegment<T>(EmptyArray);

        private readonly T[] _array;
        private readonly int _offset;
        private readonly int _count;

        public ArraySegmentImpl(T[] array)
        {
            if (array == null) throw new ArgumentNullException("array");
            _array = array;
            _offset = 0;
            _count = array.Length;
        }

        public ArraySegmentImpl(T[] array, int offset, int count)
        {
            // Negative values discovered though conversion to high values when converted to unsigned
            if (array == null || (uint)offset > (uint)array.Length || (uint)count > (uint)(array.Length - offset))
                ThrowCtorValidationFailedExceptions(array, offset, count);
            _array = array;
            _offset = offset;
            _count = count;
        }

        private static void ThrowCtorValidationFailedExceptions(T[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException("array");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }

        public T[] Array => _array;

        public int Offset => _offset;

        public int Count => _count;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) ThrowIndexMustBeLess();
                return _array[_offset + index];
            }
            set
            {
                if ((uint)index >= (uint)_count) ThrowIndexMustBeLess();
                _array[_offset + index] = value;
            }
        }

        public Enumerator GetEnumerator()
        {
            ThrowInvalidOperationIfDefault();
            return new Enumerator(_array, _offset, _count);
        }

        public override int GetHashCode() =>
            _array is null ? 0 : HashCode.Combine(_offset, _count, _array.GetHashCode());

        public void CopyTo(T[] destination) => CopyTo(destination, 0);

        public void CopyTo(T[] destination, int destinationIndex)
        {
            ThrowInvalidOperationIfDefault();
            System.Array.Copy(_array, _offset, destination, destinationIndex, _count);
        }

        public void CopyTo(ArraySegment<T> destination)
        {
            ThrowInvalidOperationIfDefault();
            if (destination.Array == null) ThrowNullArray();
            if (_count > destination.Count) throw new ArgumentException("Destination is too short.", "destination");
            System.Array.Copy(_array, _offset, destination.Array, destination.Offset, _count);
        }

        public override bool Equals(object obj) => obj is ArraySegment<T> other && Equals(other);

        public bool Equals(ArraySegment<T> obj) =>
            obj.Array == _array && obj.Offset == _offset && obj.Count == _count;

        public ArraySegment<T> Slice(int index)
        {
            ThrowInvalidOperationIfDefault();
            if ((uint)index > (uint)_count) ThrowIndexMustBeLessOrEqual();
            return new ArraySegment<T>(_array, _offset + index, _count - index);
        }

        public ArraySegment<T> Slice(int index, int count)
        {
            ThrowInvalidOperationIfDefault();
            if ((uint)index > (uint)_count || (uint)count > (uint)(_count - index)) ThrowIndexMustBeLessOrEqual();
            return new ArraySegment<T>(_array, _offset + index, count);
        }

        public T[] ToArray()
        {
            ThrowInvalidOperationIfDefault();
            if (_count == 0) return EmptyArray;
            var array = new T[_count];
            System.Array.Copy(_array, _offset, array, 0, _count);
            return array;
        }

        public static bool operator ==(ArraySegmentImpl<T> a, ArraySegmentImpl<T> b) =>
            a._array == b._array && a._offset == b._offset && a._count == b._count;

        public static bool operator !=(ArraySegmentImpl<T> a, ArraySegmentImpl<T> b) => !(a == b);

        public static implicit operator ArraySegmentImpl<T>(T[] array) => array != null ? new ArraySegmentImpl<T>(array) : default;

        // ---- IList<T> / IReadOnlyList<T> / ICollection<T> -------------------------------------------

        T IList<T>.this[int index]
        {
            get
            {
                ThrowInvalidOperationIfDefault();
                if (index < 0 || index >= _count) ThrowIndexMustBeLess();
                return _array[_offset + index];
            }
            set
            {
                ThrowInvalidOperationIfDefault();
                if (index < 0 || index >= _count) ThrowIndexMustBeLess();
                _array[_offset + index] = value;
            }
        }

        int IList<T>.IndexOf(T item)
        {
            ThrowInvalidOperationIfDefault();
            int index = System.Array.IndexOf(_array, item, _offset, _count);
            return index >= 0 ? index - _offset : -1;
        }

        void IList<T>.Insert(int index, T item) => throw new NotSupportedException();

        void IList<T>.RemoveAt(int index) => throw new NotSupportedException();

        T IReadOnlyList<T>.this[int index]
        {
            get
            {
                ThrowInvalidOperationIfDefault();
                if (index < 0 || index >= _count) ThrowIndexMustBeLess();
                return _array[_offset + index];
            }
        }

        // The indexer setter does not throw although IsReadOnly is true, to match arrays.
        bool ICollection<T>.IsReadOnly => true;

        void ICollection<T>.Add(T item) => throw new NotSupportedException();

        void ICollection<T>.Clear() => throw new NotSupportedException();

        bool ICollection<T>.Contains(T item)
        {
            ThrowInvalidOperationIfDefault();
            return System.Array.IndexOf(_array, item, _offset, _count) >= 0;
        }

        bool ICollection<T>.Remove(T item) => throw new NotSupportedException();

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            ThrowInvalidOperationIfDefault();
            return new Enumerator(_array, _offset, _count);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            ThrowInvalidOperationIfDefault();
            return new Enumerator(_array, _offset, _count);
        }

        private void ThrowInvalidOperationIfDefault()
        {
            if (_array == null) ThrowNullArray();
        }

        private static void ThrowNullArray() => throw new InvalidOperationException("The underlying array is null.");

        private static void ThrowIndexMustBeLess() =>
            throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");

        private static void ThrowIndexMustBeLessOrEqual() =>
            throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");

        [Shadows("System.ArraySegment`1+Enumerator")]
        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] _array;
            private readonly int _start;
            private readonly int _end; // cache Offset + Count, since it's a little slow
            private int _current;

            internal Enumerator(T[] array, int offset, int count)
            {
                _array = array;
                _start = offset;
                _end = offset + count;
                _current = offset - 1;
            }

            public bool MoveNext()
            {
                if (_current < _end)
                {
                    _current++;
                    return _current < _end;
                }
                return false;
            }

            public T Current
            {
                get
                {
                    if (_current < _start) throw new InvalidOperationException("Enumeration has not started. Call MoveNext.");
                    if (_current >= _end) throw new InvalidOperationException("Enumeration already finished.");
                    return _array[_current];
                }
            }

            object IEnumerator.Current => Current;

            void IEnumerator.Reset() => _current = _start - 1;

            public void Dispose()
            {
            }
        }
    }

    // MemoryExtensions' ArraySegment<T> overloads. A default segment (null array) gives an empty span,
    // like new Span<T>(null, 0, 0).
    [ShadowMembers("System.MemoryExtensions")]
    internal static class ArraySegmentMemoryExtensions
    {
        private static Span<T> MakeSpan<T>(T[] array, int start, int length) => array == null ? default : new Span<T>(array, start, length);
        private static Memory<T> MakeMemory<T>(T[] array, int start, int length) => array == null ? default : new Memory<T>(array, start, length);

        public static Span<T> AsSpan<T>(ArraySegment<T> segment) => MakeSpan(segment.Array, segment.Offset, segment.Count);

        public static Span<T> AsSpan<T>(ArraySegment<T> segment, int start)
        {
            if ((uint)start > (uint)segment.Count) throw new ArgumentOutOfRangeException("start");
            return MakeSpan(segment.Array, segment.Offset + start, segment.Count - start);
        }

        public static Span<T> AsSpan<T>(ArraySegment<T> segment, Index startIndex) => AsSpan(segment, startIndex.GetOffset(segment.Count));

        public static Span<T> AsSpan<T>(ArraySegment<T> segment, int start, int length)
        {
            if ((uint)start > (uint)segment.Count) throw new ArgumentOutOfRangeException("start");
            if ((uint)length > (uint)(segment.Count - start)) throw new ArgumentOutOfRangeException("length");
            return MakeSpan(segment.Array, segment.Offset + start, length);
        }

        public static Span<T> AsSpan<T>(ArraySegment<T> segment, Range range)
        {
            // Range.GetOffsetAndLength
            int start = range.Start.GetOffset(segment.Count);
            int end = range.End.GetOffset(segment.Count);
            if ((uint)end > (uint)segment.Count || (uint)start > (uint)end) throw new ArgumentOutOfRangeException("length");
            int length = end - start;
            return MakeSpan(segment.Array, segment.Offset + start, length);
        }

        public static Memory<T> AsMemory<T>(ArraySegment<T> segment) => MakeMemory(segment.Array, segment.Offset, segment.Count);

        public static Memory<T> AsMemory<T>(ArraySegment<T> segment, int start)
        {
            if ((uint)start > (uint)segment.Count) throw new ArgumentOutOfRangeException("start");
            return MakeMemory(segment.Array, segment.Offset + start, segment.Count - start);
        }

        public static Memory<T> AsMemory<T>(ArraySegment<T> segment, int start, int length)
        {
            if ((uint)start > (uint)segment.Count) throw new ArgumentOutOfRangeException("start");
            if ((uint)length > (uint)(segment.Count - start)) throw new ArgumentOutOfRangeException("length");
            return MakeMemory(segment.Array, segment.Offset + start, length);
        }
    }
}
