// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Buffers.ReadOnlySequence<T>, ReadOnlySequenceSegment<T>, SequencePosition, SequenceReader<T>,
// BuffersExtensions and SequenceReaderExtensions: what System.IO.Pipelines and Kestrel's HTTP parser
// read requests through. Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Memory
// and System.Private.CoreLib; see THIRD-PARTY-NOTICES.md. Adapted to the corelib (docs/12-corelib.md):
// a memory becomes a one-segment sequence (csharpc's memories don't record their array, MemoryManager or
// string), so the MemoryManager and string representations never occur; ThrowHelper inlined with
// System.Memory's exception types and parameter names; SequenceReader<T>.Length is computed, not cached.
#nullable enable annotations
#pragma warning disable CS8632
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;

namespace CsharpcCoreLib
{
    [Shadows("System.SequencePosition")]
    public readonly struct SequencePositionImpl : IEquatable<SequencePosition>
    {
        private readonly object? _object;
        private readonly int _integer;

        public SequencePositionImpl(object? @object, int integer)
        {
            _object = @object;
            _integer = integer;
        }

        public object? GetObject() => _object;

        public int GetInteger() => _integer;

        public bool Equals(SequencePosition other) => _integer == other.GetInteger() && object.Equals(_object, other.GetObject());

        public override bool Equals(object? obj) => obj is SequencePosition other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_object?.GetHashCode() ?? 0, _integer);
    }

    [Shadows("System.Buffers.ReadOnlySequenceSegment`1")]
    public abstract class ReadOnlySequenceSegmentImpl<T>
    {
        public ReadOnlyMemory<T> Memory { get; protected set; }

        public ReadOnlySequenceSegment<T>? Next { get; protected set; }

        public long RunningIndex { get; protected set; }
    }

    /// <summary>The single segment of a sequence made from a memory.</summary>
    internal sealed class MemorySegment<T> : ReadOnlySequenceSegment<T>
    {
        public MemorySegment(ReadOnlyMemory<T> memory) => Memory = memory;
    }

    internal enum SequenceArgument
    {
        length, start, minimumBufferSize, elementIndex, comparable, comparer, destination, offset, startSegment,
        endSegment, startIndex, endIndex, array, culture, manager, count, writer,
    }

    internal static class SequenceThrowHelper
    {
        internal static void ThrowArgumentNullException(SequenceArgument argument) => throw new ArgumentNullException(argument.ToString());
        internal static void ThrowArgumentOutOfRangeException(SequenceArgument argument) => throw new ArgumentOutOfRangeException(argument.ToString());
        internal static void ThrowInvalidOperationException() => throw new InvalidOperationException();
        internal static void ThrowInvalidOperationException_EndPositionNotReached() =>
            throw new InvalidOperationException("End position was not reached during enumeration.");
        internal static void ThrowArgumentOutOfRangeException_PositionOutOfRange() => throw new ArgumentOutOfRangeException("position");
        internal static void ThrowArgumentOutOfRangeException_OffsetOutOfRange() => throw new ArgumentOutOfRangeException("offset");

        public static void ThrowArgumentValidationException<T>(ReadOnlySequenceSegment<T>? startSegment, int startIndex, ReadOnlySequenceSegment<T>? endSegment)
        {
            if (startSegment == null) throw new ArgumentNullException("startSegment");
            if (endSegment == null) throw new ArgumentNullException("endSegment");
            if (startSegment != endSegment && startSegment.RunningIndex > endSegment.RunningIndex) throw new ArgumentOutOfRangeException("endSegment");
            if ((uint)startSegment.Memory.Length < (uint)startIndex) throw new ArgumentOutOfRangeException("startIndex");
            throw new ArgumentOutOfRangeException("endIndex");
        }

        public static void ThrowArgumentValidationException(Array? array, int start)
        {
            if (array == null) throw new ArgumentNullException("array");
            if ((uint)start > (uint)array.Length) throw new ArgumentOutOfRangeException("start");
            throw new ArgumentOutOfRangeException("length");
        }

        public static void ThrowStartOrEndArgumentValidationException(long start)
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start");
            throw new ArgumentOutOfRangeException("length");
        }
    }

    /// <summary>
    /// Represents a sequence that can read a sequential series of <typeparam name="T" />.
    /// </summary>
    [Shadows("System.Buffers.ReadOnlySequence`1")]
    public readonly partial struct ReadOnlySequenceImpl<T>
    {
        // The data is essentially two SequencePositions, however the Start and End SequencePositions are deconstructed to improve packing.
        private readonly object? _startObject;
        private readonly object? _endObject;
        private readonly int _startInteger;
        private readonly int _endInteger;

        /// <summary>
        /// Returns empty <see cref="ReadOnlySequence{T}"/>
        /// </summary>
        public static readonly ReadOnlySequenceImpl<T> Empty = new ReadOnlySequenceImpl<T>(new T[0]);

        /// <summary>
        /// Length of the <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        public long Length => GetLength();

        /// <summary>
        /// Determines if the <see cref="ReadOnlySequence{T}"/> is empty.
        /// </summary>
        public bool IsEmpty => Length == 0;

        /// <summary>
        /// Determines if the <see cref="ReadOnlySequence{T}"/> contains a single <see cref="ReadOnlyMemory{T}"/> segment.
        /// </summary>
        public bool IsSingleSegment
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _startObject == _endObject;
        }

        /// <summary>
        /// Gets <see cref="ReadOnlyMemory{T}"/> from the first segment.
        /// </summary>
        public ReadOnlyMemory<T> First => GetFirstBuffer();

        /// <summary>
        /// Gets <see cref="ReadOnlySpan{T}"/> from the first segment.
        /// </summary>
        public ReadOnlySpan<T> FirstSpan => GetFirstSpan();

        /// <summary>
        /// A position to the start of the <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        public SequencePosition Start
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new SequencePosition(_startObject, GetIndex(_startInteger));
        }

        /// <summary>
        /// A position to the end of the <see cref="ReadOnlySequence{T}"/>
        /// </summary>
        public SequencePosition End
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new SequencePosition(_endObject, GetIndex(_endInteger));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySequenceImpl(object? startSegment, int startIndexAndFlags, object? endSegment, int endIndexAndFlags)
        {
            // Used by SliceImpl to create new ReadOnlySequence

            // startSegment and endSegment can be null for default ReadOnlySequence only
            Debug.Assert((startSegment != null && endSegment != null) ||
                (startSegment == null && endSegment == null && startIndexAndFlags == 0 && endIndexAndFlags == 0));

            _startObject = startSegment;
            _endObject = endSegment;
            _startInteger = startIndexAndFlags;
            _endInteger = endIndexAndFlags;
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlySequence{T}"/> from linked memory list represented by start and end segments
        /// and corresponding indexes in them.
        /// </summary>
        public ReadOnlySequenceImpl(ReadOnlySequenceSegment<T> startSegment, int startIndex, ReadOnlySequenceSegment<T> endSegment, int endIndex)
        {
            if (startSegment == null ||
                endSegment == null ||
                (startSegment != endSegment && startSegment.RunningIndex > endSegment.RunningIndex) ||
                (uint)startSegment.Memory.Length < (uint)startIndex ||
                (uint)endSegment.Memory.Length < (uint)endIndex ||
                (startSegment == endSegment && endIndex < startIndex))
                SequenceThrowHelper.ThrowArgumentValidationException(startSegment, startIndex, endSegment);

            _startObject = startSegment;
            _endObject = endSegment;
            _startInteger = startIndex;
            _endInteger = endIndex;
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlySequence{T}"/> from the array.
        /// </summary>
        public ReadOnlySequenceImpl(T[] array)
        {
            if (array == null)
                SequenceThrowHelper.ThrowArgumentNullException(SequenceArgument.array);

            _startObject = array;
            _endObject = array;
            _startInteger = 0;
            _endInteger = ReadOnlySequenceFlags.ArrayToSequenceEnd(array.Length);
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlySequence{T}"/> from the array, start, and index.
        /// </summary>
        public ReadOnlySequenceImpl(T[] array, int start, int length)
        {
            if (array == null ||
                (uint)start > (uint)array.Length ||
                (uint)length > (uint)(array.Length - start))
                SequenceThrowHelper.ThrowArgumentValidationException(array, start);

            _startObject = array;
            _endObject = array;
            _startInteger = start;
            _endInteger = ReadOnlySequenceFlags.ArrayToSequenceEnd(start + length);
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlySequence{T}"/> from the <see cref="ReadOnlyMemory{T}"/>.
        /// Consumer is expected to manage lifetime of memory until <see cref="ReadOnlySequence{T}"/> is not used anymore.
        /// </summary>
        public ReadOnlySequenceImpl(ReadOnlyMemory<T> memory)
        {
            // csharpc's memories don't record what they were made from (an array, a MemoryManager, a string),
            // so a memory becomes a sequence of one segment; only SequencePosition.GetObject() tells.
            var segment = new MemorySegment<T>(memory);
            _startObject = segment;
            _endObject = segment;
            _startInteger = 0;
            _endInteger = memory.Length;
        }

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}"/>, beginning at <paramref name="start"/>, with <paramref name="length"/> items.
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="length">The length of the slice.</param>
        /// <returns>A slice that consists of <paramref name="length" /> elements from the current instance starting at index <paramref name="start" />.</returns>
        public ReadOnlySequenceImpl<T> Slice(long start, long length)
        {
            if (start < 0 || length < 0)
                SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(start);

            SequencePosition begin;
            SequencePosition end;

            int startIndex = GetIndex(_startInteger);
            int endIndex = GetIndex(_endInteger);

            object? startObject = _startObject;
            object? endObject = _endObject;

            if (startObject != endObject)
            {
                Debug.Assert(startObject != null);
                var startSegment = (ReadOnlySequenceSegment<T>)startObject;

                int currentLength = startSegment.Memory.Length - startIndex;

                // Position in start segment
                if (currentLength > start)
                {
                    startIndex += (int)start;
                    begin = new SequencePosition(startObject, startIndex);

                    end = GetEndPosition(startSegment, startObject, startIndex, endObject!, endIndex, length);
                }
                else
                {
                    if (currentLength < 0)
                        SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

                    begin = SeekMultiSegment(startSegment.Next!, endObject!, endIndex, start - currentLength, SequenceArgument.start);

                    int beginIndex = begin.GetInteger();
                    object beginObject = begin.GetObject()!;

                    if (beginObject != endObject)
                    {
                        Debug.Assert(beginObject != null);
                        end = GetEndPosition((ReadOnlySequenceSegment<T>)beginObject, beginObject, beginIndex, endObject!, endIndex, length);
                    }
                    else
                    {
                        if (endIndex - beginIndex < length)
                            SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(0);  // Passing value >= 0 means throw exception on length argument

                        end = new SequencePosition(beginObject, beginIndex + (int)length);
                    }
                }
            }
            else
            {
                if (endIndex - startIndex < start)
                    SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(-1); // Passing value < 0 means throw exception on start argument

                startIndex += (int)start;
                begin = new SequencePosition(startObject, startIndex);

                if (endIndex - startIndex < length)
                    SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(0);  // Passing value >= 0 means throw exception on length argument

                end = new SequencePosition(startObject, startIndex + (int)length);
            }

            return SliceImpl(begin, end);
        }

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}"/>, beginning at <paramref name="start"/> and ending at <paramref name="end"/> (exclusive).
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="end">The ending (exclusive) <see cref="SequencePosition"/> of the slice.</param>
        /// <returns>A slice that consists of items from the <paramref name="start" /> index to, but not including, the <paramref name="end" /> sequence position in the current read-only sequence.</returns>
        public ReadOnlySequenceImpl<T> Slice(long start, SequencePosition end)
        {
            if (start < 0)
                SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(start);

            uint startIndex = (uint)GetIndex(_startInteger);
            object? startObject = _startObject;

            uint endIndex = (uint)GetIndex(_endInteger);
            object? endObject = _endObject;

            uint sliceEndIndex = (uint)end.GetInteger();
            object? sliceEndObject = end.GetObject();

            if (sliceEndObject == null)
            {
                sliceEndObject = _startObject;
                sliceEndIndex = startIndex;
            }

            // Single-Segment Sequence
            if (startObject == endObject)
            {
                if (startObject != sliceEndObject || !InRange(sliceEndIndex, startIndex, endIndex))
                {
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
                }

                if (sliceEndIndex - startIndex < start)
                    SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(-1); // Passing value < 0 means throw exception on start argument

                goto FoundInFirstSegment;
            }

            // Multi-Segment Sequence
            var startSegment = (ReadOnlySequenceSegment<T>)startObject!;
            ulong startRange = (ulong)(startSegment.RunningIndex + startIndex);
            ulong sliceRange = (ulong)(((ReadOnlySequenceSegment<T>)sliceEndObject!).RunningIndex + sliceEndIndex);

            // This optimization works because we know sliceEndIndex, startIndex, and endIndex are all >= 0
            Debug.Assert(sliceEndIndex >= 0 && startIndex >= 0 && endIndex >= 0);
            if (!InRange(
                sliceRange,
                startRange,
                (ulong)(((ReadOnlySequenceSegment<T>)endObject!).RunningIndex + endIndex)))
            {
                SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
            }

            if (startRange + (ulong)start > sliceRange)
            {
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.start);
            }

            int currentLength = startSegment.Memory.Length - (int)startIndex;

            // Position not in startSegment
            if (currentLength <= start)
            {
                if (currentLength < 0)
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

                // End of segment. Move to start of next.
                SequencePosition begin = SeekMultiSegment(startSegment.Next!, sliceEndObject, (int)sliceEndIndex, start - currentLength, SequenceArgument.start);
                return SliceImpl(begin, end);
            }

        FoundInFirstSegment:
            // startIndex + start <= int.MaxValue
            Debug.Assert(start <= int.MaxValue - startIndex);
            return SliceImpl(new SequencePosition(startObject, (int)startIndex + (int)start), new SequencePosition(sliceEndObject, (int)sliceEndIndex));
        }

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}"/>, beginning at <paramref name="start"/>, with <paramref name="length"/> items.
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        /// <param name="length">The length of the slice.</param>
        /// <returns>A slice that consists of <paramref name="length" /> elements from the current instance starting at sequence position <paramref name="start" />.</returns>
        public ReadOnlySequenceImpl<T> Slice(SequencePosition start, long length)
        {
            uint startIndex = (uint)GetIndex(_startInteger);
            object? startObject = _startObject;

            uint endIndex = (uint)GetIndex(_endInteger);
            object? endObject = _endObject;

            // Check start before length
            uint sliceStartIndex = (uint)start.GetInteger();
            object? sliceStartObject = start.GetObject();

            if (sliceStartObject == null)
            {
                sliceStartIndex = startIndex;
                sliceStartObject = _startObject;
            }

            // Single-Segment Sequence
            if (startObject == endObject)
            {
                if (startObject != sliceStartObject || !InRange(sliceStartIndex, startIndex, endIndex))
                {
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
                }

                if (length < 0)
                    // Passing value >= 0 means throw exception on length argument
                    SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(0);

                if (endIndex - sliceStartIndex < length)
                    SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(0);

                goto FoundInFirstSegment;
            }

            // Multi-Segment Sequence
            var sliceStartSegment = (ReadOnlySequenceSegment<T>)sliceStartObject!;
            ulong sliceRange = (ulong)((sliceStartSegment.RunningIndex + sliceStartIndex));
            ulong startRange = (ulong)(((ReadOnlySequenceSegment<T>)startObject!).RunningIndex + startIndex);
            ulong endRange = (ulong)(((ReadOnlySequenceSegment<T>)endObject!).RunningIndex + endIndex);

            // This optimization works because we know sliceStartIndex, startIndex, and endIndex are all >= 0
            Debug.Assert(sliceStartIndex >= 0 && startIndex >= 0 && endIndex >= 0);
            if (!InRange(sliceRange, startRange, endRange))
            {
                SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
            }

            if (length < 0)
                // Passing value >= 0 means throw exception on length argument
                SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(0);

            if (sliceRange + (ulong)length > endRange)
            {
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.length);
            }

            int currentLength = sliceStartSegment.Memory.Length - (int)sliceStartIndex;

            // Position not in startSegment
            if (currentLength < length)
            {
                if (currentLength < 0)
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

                // End of segment. Move to start of next.
                SequencePosition end = SeekMultiSegment(sliceStartSegment.Next!, endObject, (int)endIndex, length - currentLength, SequenceArgument.length);
                return SliceImpl(start, end);
            }

        FoundInFirstSegment:
            // sliceStartIndex + length <= int.MaxValue
            Debug.Assert(length <= int.MaxValue - sliceStartIndex);
            return SliceImpl(new SequencePosition(sliceStartObject, (int)sliceStartIndex), new SequencePosition(sliceStartObject, (int)sliceStartIndex + (int)length));
        }

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}"/>, beginning at <paramref name="start"/>, with <paramref name="length"/> items.
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="length">The length of the slice.</param>
        /// <returns>A slice that consists of <paramref name="length" /> elements from the current instance starting at index <paramref name="start" />.</returns>
        public ReadOnlySequenceImpl<T> Slice(int start, int length) => Slice((long)start, length);

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}"/>, beginning at <paramref name="start"/> and ending at <paramref name="end"/> (exclusive).
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="end">The ending (exclusive) <see cref="SequencePosition"/> of the slice.</param>
        /// <returns>A slice that consists of items from the <paramref name="start" /> index to, but not including, the <paramref name="end" /> sequence position in the current read-only sequence.</returns>
        public ReadOnlySequenceImpl<T> Slice(int start, SequencePosition end) => Slice((long)start, end);

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}"/>, beginning at <paramref name="start"/>, with <paramref name="length"/> items.
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        /// <param name="length">The length of the slice.</param>
        /// <returns>A slice that consists of <paramref name="length" /> elements from the current instance starting at sequence position <paramref name="start" />.</returns>
        public ReadOnlySequenceImpl<T> Slice(SequencePosition start, int length) => Slice(start, (long)length);

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlySequence{T}"/>, beginning at <paramref name="start"/>, ending at <paramref name="end"/> (exclusive).
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        /// <param name="end">The ending (exclusive) <see cref="SequencePosition"/> of the slice.</param>
        /// <returns>A slice that consists of items from the <paramref name="start" /> sequence position to, but not including, the <paramref name="end" /> sequence position in the current read-only sequence.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySequenceImpl<T> Slice(SequencePosition start, SequencePosition end)
        {
            BoundsCheck((uint)start.GetInteger(), start.GetObject(), (uint)end.GetInteger(), end.GetObject());
            return SliceImpl(start, end);
        }

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}" />, beginning at a specified sequence position and continuing to the end of the read-only sequence.
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        /// <returns>A slice starting at sequence position <paramref name="start" /> and continuing to the end of the current read-only sequence.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySequenceImpl<T> Slice(SequencePosition start)
        {
            bool positionIsNotNull = start.GetObject() != null;
            BoundsCheck(start, positionIsNotNull);
            return SliceImpl(positionIsNotNull ? start : Start);
        }

        /// <summary>
        /// Forms a slice out of the current <see cref="ReadOnlySequence{T}" /> , beginning at a specified index and continuing to the end of the read-only sequence.
        /// </summary>
        /// <param name="start">The start index at which to begin this slice.</param>
        /// <returns>A slice starting at index <paramref name="start" /> and continuing to the end of the current read-only sequence.</returns>
        public ReadOnlySequenceImpl<T> Slice(long start)
        {
            if (start < 0)
                SequenceThrowHelper.ThrowStartOrEndArgumentValidationException(start);

            if (start == 0)
                return this;

            SequencePosition begin = Seek(start, SequenceArgument.start);
            return SliceImpl(begin);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (typeof(T) == typeof(char))
            {
                ReadOnlySequenceImpl<char> charSequence = (ReadOnlySequenceImpl<char>)(object)this;
                if (charSequence.TryGetString(out string text, out int start, out int length))
                    return text.Substring(start, length);
                if (Length < int.MaxValue)
                {
                    char[] chars = new char[(int)Length];
                    BuffersExtensionsImpl.CopyTo(charSequence, chars);
                    return new string(chars);
                }
            }
            return "System.Buffers.ReadOnlySequence<" + typeof(T).Name + ">[" + Length.ToString() + "]";
        }

        /// <summary>
        /// Returns an enumerator over the <see cref="ReadOnlySequence{T}"/>
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <summary>
        /// Returns a new <see cref="SequencePosition"/> at an <paramref name="offset"/> from the start of the sequence.
        /// </summary>
        public SequencePosition GetPosition(long offset)
        {
            if (offset < 0)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException_OffsetOutOfRange();

            return Seek(offset);
        }

        /// <summary>
        /// Returns the offset of a <paramref name="position" /> within this sequence.
        /// </summary>
        /// <param name="position">The <see cref="System.SequencePosition"/> of which to get the offset.</param>
        /// <returns>The offset in the sequence.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">The position is out of range.</exception>
        /// <remarks>
        /// The returned offset is not a zero-based index from the start.
        /// To obtain the zero-based index offset, subtract <code>mySequence.GetOffset(mySequence.Start)</code> from the returned offset.
        /// </remarks>
        public long GetOffset(SequencePosition position)
        {
            object? positionSequenceObject = position.GetObject();
            bool positionIsNull = positionSequenceObject == null;
            BoundsCheck(position, !positionIsNull);

            object? startObject = _startObject;
            object? endObject = _endObject;

            uint positionIndex = (uint)position.GetInteger();

            // if sequence object is null we suppose start segment
            if (positionIsNull)
            {
                positionSequenceObject = _startObject;
                positionIndex = (uint)GetIndex(_startInteger);
            }

            // Single-Segment Sequence
            if (startObject == endObject)
            {
                return positionIndex;
            }
            else
            {
                // Verify position validity, this is not covered by BoundsCheck for Multi-Segment Sequence
                // BoundsCheck for Multi-Segment Sequence check only validity inside current sequence but not for SequencePosition validity.
                // For single segment position bound check is implicit.
                Debug.Assert(positionSequenceObject != null);

                if (((ReadOnlySequenceSegment<T>)positionSequenceObject).Memory.Length - positionIndex < 0)
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

                // Multi-Segment Sequence
                ReadOnlySequenceSegment<T>? currentSegment = (ReadOnlySequenceSegment<T>?)startObject;
                while (currentSegment != null && currentSegment != positionSequenceObject)
                {
                    currentSegment = currentSegment.Next!;
                }

                // Hit the end of the segments but didn't find the segment
                if (currentSegment is null)
                {
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
                }

                Debug.Assert(currentSegment!.RunningIndex + positionIndex >= 0);

                return currentSegment!.RunningIndex + positionIndex;
            }
        }

        /// <summary>
        /// Returns a new <see cref="SequencePosition"/> at an <paramref name="offset"/> from the <paramref name="origin"/>
        /// </summary>
        public SequencePosition GetPosition(long offset, SequencePosition origin)
        {
            if (offset < 0)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException_OffsetOutOfRange();

            return Seek(origin, offset);
        }

        /// <summary>
        /// Tries to retrieve next segment after <paramref name="position"/> and return its contents in <paramref name="memory"/>.
        /// Returns <code>false</code> if end of <see cref="ReadOnlySequence{T}"/> was reached otherwise <code>true</code>.
        /// Sets <paramref name="position"/> to the beginning of next segment if <paramref name="advance"/> is set to <code>true</code>.
        /// </summary>
        public bool TryGet(ref SequencePosition position, out ReadOnlyMemory<T> memory, bool advance = true)
        {
            bool result = TryGetBuffer(position, out memory, out SequencePosition next);
            if (advance)
            {
                position = next;
            }

            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetBuffer(in SequencePosition position, out ReadOnlyMemory<T> memory, out SequencePosition next)
        {
            object? positionObject = position.GetObject();
            next = default;

            if (positionObject == null)
            {
                memory = default;
                return false;
            }

            SequenceType type = GetSequenceType();
            object? endObject = _endObject;
            int startIndex = position.GetInteger();
            int endIndex = GetIndex(_endInteger);

            if (type == SequenceType.MultiSegment)
            {
                Debug.Assert(positionObject is ReadOnlySequenceSegment<T>);

                ReadOnlySequenceSegment<T> startSegment = (ReadOnlySequenceSegment<T>)positionObject;

                if (startSegment != endObject)
                {
                    ReadOnlySequenceSegment<T>? nextSegment = startSegment.Next;

                    if (nextSegment == null)
                        SequenceThrowHelper.ThrowInvalidOperationException_EndPositionNotReached();

                    next = new SequencePosition(nextSegment, 0);
                    memory = startSegment.Memory.Slice(startIndex);
                }
                else
                {
                    memory = startSegment.Memory.Slice(startIndex, endIndex - startIndex);
                }
            }
            else
            {
                if (positionObject != endObject)
                    SequenceThrowHelper.ThrowInvalidOperationException_EndPositionNotReached();

                if (type == SequenceType.Array)
                {
                    Debug.Assert(positionObject is T[]);

                    memory = new ReadOnlyMemory<T>((T[])positionObject, startIndex, endIndex - startIndex);
                }
                else if (typeof(T) == typeof(char) && type == SequenceType.String)
                {
                    Debug.Assert(positionObject is string);

                    memory = (ReadOnlyMemory<T>)(object)((string)positionObject).AsMemory(startIndex, endIndex - startIndex);
                }
                else // type == SequenceType.MemoryManager
                {
                    Debug.Assert(type == SequenceType.MemoryManager);
                    Debug.Assert(positionObject is MemoryManager<T>);

                    memory = ManagerMemory(positionObject).Slice(startIndex, endIndex - startIndex);
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlyMemory<T> GetFirstBuffer()
        {
            object? startObject = _startObject;

            if (startObject == null)
                return default;

            int startIndex = _startInteger;
            int endIndex = _endInteger;

            bool isMultiSegment = startObject != _endObject;

            // The highest bit of startIndex and endIndex are used to infer the sequence type
            // The code below is structured this way for performance reasons and is equivalent to the following:
            // SequenceType type = GetSequenceType();
            // if (type == SequenceType.MultiSegment) { ... }
            // else if (type == SequenceType.Array) { ... }
            // else if (type == SequenceType.String){ ... }
            // else if (type == SequenceType.MemoryManager) { ... }

            // Highest bit of startIndex: A = startIndex >> 31
            // Highest bit of endIndex: B = endIndex >> 31

            // A == 0 && B == 0 means SequenceType.MultiSegment
            // Equivalent to startIndex >= 0 && endIndex >= 0
            if ((startIndex | endIndex) >= 0)
            {
                ReadOnlyMemory<T> memory = ((ReadOnlySequenceSegment<T>)startObject).Memory;
                if (isMultiSegment)
                {
                    return memory.Slice(startIndex);
                }
                return memory.Slice(startIndex, endIndex - startIndex);
            }
            else
            {
                return GetFirstBufferSlow(startObject, isMultiSegment);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ReadOnlyMemory<T> GetFirstBufferSlow(object startObject, bool isMultiSegment)
        {
            if (isMultiSegment)
                SequenceThrowHelper.ThrowInvalidOperationException_EndPositionNotReached();

            int startIndex = _startInteger;
            int endIndex = _endInteger;

            Debug.Assert(startIndex < 0 || endIndex < 0);

            // A == 0 && B == 1 means SequenceType.Array
            if (startIndex >= 0)
            {
                Debug.Assert(endIndex < 0);
                return new ReadOnlyMemory<T>((T[])startObject, startIndex, (endIndex & ReadOnlySequenceFlags.IndexBitMask) - startIndex);
            }
            else
            {
                // The type == char check here is redundant. However, we still have it to allow
                // the JIT to see when that the code is unreachable and eliminate it.
                // A == 1 && B == 1 means SequenceType.String
                if (typeof(T) == typeof(char) && endIndex < 0)
                {
                    // No need to remove the FlagBitMask since (endIndex - startIndex) == (endIndex & ReadOnlySequenceFlags.IndexBitMask) - (startIndex & ReadOnlySequenceFlags.IndexBitMask)
                    return (ReadOnlyMemory<T>)(object)((string)startObject).AsMemory(startIndex & ReadOnlySequenceFlags.IndexBitMask, endIndex - startIndex);
                }
                else // endIndex >= 0, A == 1 && B == 0 means SequenceType.MemoryManager
                {
                    startIndex &= ReadOnlySequenceFlags.IndexBitMask;
                    return ManagerMemory(startObject).Slice(startIndex, endIndex - startIndex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<T> GetFirstSpan()
        {
            object? startObject = _startObject;

            if (startObject == null)
                return default;

            int startIndex = _startInteger;
            int endIndex = _endInteger;

            bool isMultiSegment = startObject != _endObject;

            // The highest bit of startIndex and endIndex are used to infer the sequence type
            // The code below is structured this way for performance reasons and is equivalent to the following:
            // SequenceType type = GetSequenceType();
            // if (type == SequenceType.MultiSegment) { ... }
            // else if (type == SequenceType.Array) { ... }
            // else if (type == SequenceType.String){ ... }
            // else if (type == SequenceType.MemoryManager) { ... }

            // Highest bit of startIndex: A = startIndex >> 31
            // Highest bit of endIndex: B = endIndex >> 31

            // A == 0 && B == 0 means SequenceType.MultiSegment
            // Equivalent to startIndex >= 0 && endIndex >= 0
            if ((startIndex | endIndex) >= 0)
            {
                ReadOnlySpan<T> span = ((ReadOnlySequenceSegment<T>)startObject).Memory.Span;
                if (isMultiSegment)
                {
                    return span.Slice(startIndex);
                }
                return span.Slice(startIndex, endIndex - startIndex);
            }
            else
            {
                return GetFirstSpanSlow(startObject, isMultiSegment);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ReadOnlySpan<T> GetFirstSpanSlow(object startObject, bool isMultiSegment)
        {
            if (isMultiSegment)
                SequenceThrowHelper.ThrowInvalidOperationException_EndPositionNotReached();

            int startIndex = _startInteger;
            int endIndex = _endInteger;

            Debug.Assert(startIndex < 0 || endIndex < 0);

            // A == 0 && B == 1 means SequenceType.Array
            if (startIndex >= 0)
            {
                Debug.Assert(endIndex < 0);
                ReadOnlySpan<T> span = (T[])startObject;
                return span.Slice(startIndex, (endIndex & ReadOnlySequenceFlags.IndexBitMask) - startIndex);
            }
            else
            {
                // The type == char check here is redundant. However, we still have it to allow
                // the JIT to see when that the code is unreachable and eliminate it.
                // A == 1 && B == 1 means SequenceType.String
                if (typeof(T) == typeof(char) && endIndex < 0)
                {
                    var memory = (ReadOnlyMemory<T>)(object)((string)startObject).AsMemory();
                    // No need to remove the FlagBitMask since (endIndex - startIndex) == (endIndex & ReadOnlySequenceFlags.IndexBitMask) - (startIndex & ReadOnlySequenceFlags.IndexBitMask)
                    return memory.Span.Slice(startIndex & ReadOnlySequenceFlags.IndexBitMask, endIndex - startIndex);
                }
                else // endIndex >= 0, A == 1 && B == 0 means SequenceType.MemoryManager
                {
                    startIndex &= ReadOnlySequenceFlags.IndexBitMask;
                    return ManagerMemory(startObject).Span.Slice(startIndex, endIndex - startIndex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SequencePosition Seek(long offset, SequenceArgument exceptionArgument = SequenceArgument.offset)
        {
            object? startObject = _startObject;
            object? endObject = _endObject;
            int startIndex = GetIndex(_startInteger);
            int endIndex = GetIndex(_endInteger);

            if (startObject != endObject)
            {
                Debug.Assert(startObject != null);
                var startSegment = (ReadOnlySequenceSegment<T>)startObject;

                int currentLength = startSegment.Memory.Length - startIndex;

                // Position in start segment, defer to single segment seek
                if (currentLength > offset || offset == 0)
                    goto IsSingleSegment;

                if (currentLength < 0)
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

                // End of segment. Move to start of next.
                return SeekMultiSegment(startSegment.Next!, endObject!, endIndex, offset - currentLength, exceptionArgument);
            }

            Debug.Assert(startObject == endObject);

            if (endIndex - startIndex < offset)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(exceptionArgument);

        // Single segment Seek
        IsSingleSegment:
            return new SequencePosition(startObject, startIndex + (int)offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SequencePosition Seek(in SequencePosition start, long offset)
        {
            object? startObject = start.GetObject();
            object? endObject = _endObject;
            int startIndex = start.GetInteger();
            int endIndex = GetIndex(_endInteger);

            if (startObject != endObject)
            {
                Debug.Assert(startObject != null);
                var startSegment = (ReadOnlySequenceSegment<T>)startObject;

                int currentLength = startSegment.Memory.Length - startIndex;

                // Position in start segment, defer to single segment seek
                if (currentLength > offset)
                    goto IsSingleSegment;

                if (currentLength < 0)
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

                // End of segment. Move to start of next.
                return SeekMultiSegment(startSegment.Next!, endObject!, endIndex, offset - currentLength, SequenceArgument.offset);
            }

            Debug.Assert(startObject == endObject);

            if (endIndex - startIndex < offset)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.offset);

        // Single segment Seek
        IsSingleSegment:
            return new SequencePosition(startObject, startIndex + (int)offset);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static SequencePosition SeekMultiSegment(ReadOnlySequenceSegment<T>? currentSegment, object endObject, int endIndex, long offset, SequenceArgument argument)
        {
            Debug.Assert(currentSegment != null); // currentSegment parameter is marked as nullable as the parameter is reused/reassigned in the body
            Debug.Assert(offset >= 0);

            while (currentSegment != null && currentSegment != endObject)
            {
                int memoryLength = currentSegment.Memory.Length;

                // Fully contained in this segment
                if (memoryLength > offset)
                    goto FoundSegment;

                // Move to next
                offset -= memoryLength;
                currentSegment = currentSegment.Next;
            }

            // Hit the end of the segments but didn't reach the count
            if (currentSegment == null || endIndex < offset)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(argument);

        FoundSegment:
            return new SequencePosition(currentSegment, (int)offset);
        }

        private void BoundsCheck(in SequencePosition position, bool positionIsNotNull)
        {
            uint sliceStartIndex = (uint)position.GetInteger();

            object? startObject = _startObject;
            object? endObject = _endObject;

            uint startIndex = (uint)GetIndex(_startInteger);
            uint endIndex = (uint)GetIndex(_endInteger);

            // Single-Segment Sequence
            if (startObject == endObject)
            {
                if (!InRange(sliceStartIndex, startIndex, endIndex))
                {
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
                }
            }
            else
            {
                // Multi-Segment Sequence
                // Storing this in a local since it is used twice within InRange()
                ulong startRange = (ulong)(((ReadOnlySequenceSegment<T>)startObject!).RunningIndex + startIndex);
                long runningIndex = 0;
                if (positionIsNotNull)
                {
                    Debug.Assert(position.GetObject() != null);
                    runningIndex = ((ReadOnlySequenceSegment<T>)position.GetObject()!).RunningIndex;
                }

                if (!InRange(
                    (ulong)(runningIndex + sliceStartIndex),
                    startRange,
                    (ulong)(((ReadOnlySequenceSegment<T>)endObject!).RunningIndex + endIndex)))
                {
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
                }
            }
        }

        private void BoundsCheck(uint sliceStartIndex, object? sliceStartObject, uint sliceEndIndex, object? sliceEndObject)
        {
            object? startObject = _startObject;
            object? endObject = _endObject;

            uint startIndex = (uint)GetIndex(_startInteger);
            uint endIndex = (uint)GetIndex(_endInteger);

            // Single-Segment Sequence
            if (startObject == endObject)
            {
                if (sliceStartObject != sliceEndObject ||
                    sliceStartObject != startObject ||
                    sliceStartIndex > sliceEndIndex ||
                    sliceStartIndex < startIndex ||
                    sliceEndIndex > endIndex)
                {
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
                }
            }
            else
            {
                // Multi-Segment Sequence
                // This optimization works because we know sliceStartIndex, sliceEndIndex, startIndex, and endIndex are all >= 0
                Debug.Assert(sliceStartIndex >= 0 && startIndex >= 0 && endIndex >= 0);

                ulong sliceStartRange = sliceStartIndex;
                ulong sliceEndRange = sliceEndIndex;

                if (sliceStartObject != null)
                {
                    sliceStartRange += (ulong)((ReadOnlySequenceSegment<T>)sliceStartObject).RunningIndex;
                }

                if (sliceEndObject != null)
                {
                    sliceEndRange += (ulong)((ReadOnlySequenceSegment<T>)sliceEndObject).RunningIndex;
                }

                if (sliceStartRange > sliceEndRange)
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

                if (sliceStartRange < (ulong)(((ReadOnlySequenceSegment<T>)startObject!).RunningIndex + startIndex)
                    || sliceEndRange > (ulong)(((ReadOnlySequenceSegment<T>)endObject!).RunningIndex + endIndex))
                {
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();
                }
            }
        }

        private static SequencePosition GetEndPosition(ReadOnlySequenceSegment<T> startSegment, object startObject, int startIndex, object endObject, int endIndex, long length)
        {
            int currentLength = startSegment.Memory.Length - startIndex;

            if (currentLength > length)
            {
                return new SequencePosition(startObject, startIndex + (int)length);
            }

            if (currentLength < 0)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException_PositionOutOfRange();

            return SeekMultiSegment(startSegment.Next, endObject, endIndex, length - currentLength, SequenceArgument.length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SequenceType GetSequenceType()
        {
            // We take high order bits of two indexes and move them
            // to a first and second position to convert to SequenceType

            // if (start < 0  and end < 0)
            // start >> 31 = -1, end >> 31 = -1
            // 2 * (-1) + (-1) = -3, result = (SequenceType)3

            // if (start < 0  and end >= 0)
            // start >> 31 = -1, end >> 31 = 0
            // 2 * (-1) + 0 = -2, result = (SequenceType)2

            // if (start >= 0  and end >= 0)
            // start >> 31 = 0, end >> 31 = 0
            // 2 * 0 + 0 = 0, result = (SequenceType)0

            // if (start >= 0  and end < 0)
            // start >> 31 = 0, end >> 31 = -1
            // 2 * 0 + (-1) = -1, result = (SequenceType)1

            return (SequenceType)(-(2 * (_startInteger >> 31) + (_endInteger >> 31)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetIndex(int Integer) => Integer & ReadOnlySequenceFlags.IndexBitMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySequenceImpl<T> SliceImpl(in SequencePosition start, in SequencePosition end)
        {
            // In this method we reset high order bits from indices
            // of positions that were passed in
            // and apply type bits specific for current ReadOnlySequence type

            return new ReadOnlySequenceImpl<T>(
                start.GetObject(),
                start.GetInteger() | (_startInteger & ReadOnlySequenceFlags.FlagBitMask),
                end.GetObject(),
                end.GetInteger() | (_endInteger & ReadOnlySequenceFlags.FlagBitMask)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySequenceImpl<T> SliceImpl(in SequencePosition start)
        {
            // In this method we reset high order bits from indices
            // of positions that were passed in
            // and apply type bits specific for current ReadOnlySequence type

            return new ReadOnlySequenceImpl<T>(
                start.GetObject(),
                start.GetInteger() | (_startInteger & ReadOnlySequenceFlags.FlagBitMask),
                _endObject,
                _endInteger
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetLength()
        {
            object? startObject = _startObject;
            object? endObject = _endObject;
            int startIndex = GetIndex(_startInteger);
            int endIndex = GetIndex(_endInteger);

            if (startObject != endObject)
            {
                var startSegment = (ReadOnlySequenceSegment<T>)startObject!;
                var endSegment = (ReadOnlySequenceSegment<T>)endObject!;
                // (End offset) - (start offset)
                return (endSegment.RunningIndex + endIndex) - (startSegment.RunningIndex + startIndex);
            }

            // Single segment length
            return endIndex - startIndex;
        }

        internal bool TryGetReadOnlySequenceSegment([NotNullWhen(true)] out ReadOnlySequenceSegment<T>? startSegment, out int startIndex, [NotNullWhen(true)] out ReadOnlySequenceSegment<T>? endSegment, out int endIndex)
        {
            object? startObject = _startObject;

            // Default or not MultiSegment
            if (startObject == null || GetSequenceType() != SequenceType.MultiSegment)
            {
                startSegment = null;
                startIndex = 0;
                endSegment = null;
                endIndex = 0;
                return false;
            }

            Debug.Assert(_endObject != null);

            startSegment = (ReadOnlySequenceSegment<T>)startObject;
            startIndex = GetIndex(_startInteger);
            endSegment = (ReadOnlySequenceSegment<T>)_endObject;
            endIndex = GetIndex(_endInteger);
            return true;
        }

        internal bool TryGetArray(out ArraySegment<T> segment)
        {
            if (GetSequenceType() != SequenceType.Array)
            {
                segment = default;
                return false;
            }

            Debug.Assert(_startObject != null);

            int startIndex = GetIndex(_startInteger);
            segment = new ArraySegment<T>((T[])_startObject, startIndex, GetIndex(_endInteger) - startIndex);
            return true;
        }

        internal bool TryGetString([NotNullWhen(true)] out string? text, out int start, out int length)
        {
            if (typeof(T) != typeof(char) || GetSequenceType() != SequenceType.String)
            {
                start = 0;
                length = 0;
                text = null;
                return false;
            }

            Debug.Assert(_startObject != null);

            start = GetIndex(_startInteger);
            length = GetIndex(_endInteger) - start;
            text = (string)_startObject;
            return true;
        }

        private static bool InRange(uint value, uint start, uint end)
        {
            // _sequenceStart and _sequenceEnd must be well-formed
            Debug.Assert(start <= int.MaxValue);
            Debug.Assert(end <= int.MaxValue);
            Debug.Assert(start <= end);

            // The case, value > int.MaxValue, is invalid, and hence it shouldn't be in the range.
            // If value > int.MaxValue, it is invariably greater than both 'start' and 'end'.
            // In that case, the experession simplifies to value <= end, which will return false.

            // The case, value < start, is invalid.
            // In that case, (value - start) would underflow becoming larger than int.MaxValue.
            // (end - start) can never underflow and hence must be within 0 and int.MaxValue.
            // So, we will correctly return false.

            // The case, value > end, is invalid.
            // In that case, the expression simplifies to value <= end, which will return false.
            // This is because end > start & value > end implies value > start as well.

            // In all other cases, value is valid, and we return true.

            // Equivalent to: return (start <= value && value <= end)
            return (value - start) <= (end - start);
        }

        private static bool InRange(ulong value, ulong start, ulong end)
        {
            // _sequenceStart and _sequenceEnd must be well-formed
            Debug.Assert(start <= long.MaxValue);
            Debug.Assert(end <= long.MaxValue);
            Debug.Assert(start <= end);

            // The case, value > long.MaxValue, is invalid, and hence it shouldn't be in the range.
            // If value > long.MaxValue, it is invariably greater than both 'start' and 'end'.
            // In that case, the experession simplifies to value <= end, which will return false.

            // The case, value < start, is invalid.
            // In that case, (value - start) would underflow becoming larger than long.MaxValue.
            // (end - start) can never underflow and hence must be within 0 and long.MaxValue.
            // So, we will correctly return false.

            // The case, value > end, is invalid.
            // In that case, the expression simplifies to value <= end, which will return false.
            // This is because end > start & value > end implies value > start as well.

            // In all other cases, value is valid, and we return true.

            // Equivalent to: return (start <= value && value <= start)
            return (value - start) <= (end - start);
        }

        /// <summary>
        /// Helper to efficiently prepare the <see cref="SequenceReader{T}"/>
        /// </summary>
        /// <param name="first">The first span in the sequence.</param>
        /// <param name="next">The next position.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetFirstSpan(out ReadOnlySpan<T> first, out SequencePosition next)
        {
            first = default;
            next = default;
            object? startObject = _startObject;
            int startIndex = _startInteger;

            if (startObject != null)
            {
                bool hasMultipleSegments = startObject != _endObject;
                int endIndex = _endInteger;

                if (startIndex >= 0)
                {
                    if (endIndex >= 0)
                    {
                        // Positive start and end index == ReadOnlySequenceSegment<T>
                        ReadOnlySequenceSegment<T> segment = (ReadOnlySequenceSegment<T>)startObject;
                        first = segment.Memory.Span;
                        if (hasMultipleSegments)
                        {
                            first = first.Slice(startIndex);
                            next = new SequencePosition(segment.Next, 0);
                        }
                        else
                        {
                            first = first.Slice(startIndex, endIndex - startIndex);
                        }
                    }
                    else
                    {
                        // Positive start and negative end index == T[]
                        if (hasMultipleSegments)
                            SequenceThrowHelper.ThrowInvalidOperationException_EndPositionNotReached();

                        first = new ReadOnlySpan<T>((T[])startObject, startIndex, (endIndex & ReadOnlySequenceFlags.IndexBitMask) - startIndex);
                    }
                }
                else
                {
                    first = GetFirstSpanSlow(startObject, startIndex, endIndex, hasMultipleSegments);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ReadOnlySpan<T> GetFirstSpanSlow(object startObject, int startIndex, int endIndex, bool hasMultipleSegments)
        {
            Debug.Assert(startIndex < 0);
            if (hasMultipleSegments)
                SequenceThrowHelper.ThrowInvalidOperationException_EndPositionNotReached();

            // The type == char check here is redundant. However, we still have it to allow
            // the JIT to see when that the code is unreachable and eliminate it.
            if (typeof(T) == typeof(char) && endIndex < 0)
            {
                // Negative start and negative end index == string
                var memory = (ReadOnlyMemory<T>)(object)((string)startObject).AsMemory(startIndex & ReadOnlySequenceFlags.IndexBitMask, endIndex - startIndex);
                return memory.Span;
            }
            else
            {
                // Negative start and positive end index == MemoryManager<T>
                startIndex &= ReadOnlySequenceFlags.IndexBitMask;
                return ManagerMemory(startObject).Span.Slice(startIndex, endIndex - startIndex);
            }
        }
    
        /// <summary>
        /// An enumerator over the <see cref="ReadOnlySequence{T}"/>
        /// </summary>
        [Shadows("System.Buffers.ReadOnlySequence`1+Enumerator")]
        public struct Enumerator
        {
            private readonly ReadOnlySequenceImpl<T> _sequence;
            private SequencePosition _next;
            private ReadOnlyMemory<T> _currentMemory;

            /// <summary>Initialize the enumerator.</summary>
            /// <param name="sequence">The <see cref="ReadOnlySequence{T}"/> to enumerate.</param>
            public Enumerator(in ReadOnlySequenceImpl<T> sequence)
            {
                _currentMemory = default;
                _next = sequence.Start;
                _sequence = sequence;
            }

            /// <summary>
            /// The current <see cref="ReadOnlyMemory{T}"/>
            /// </summary>
            public ReadOnlyMemory<T> Current => _currentMemory;

            /// <summary>
            /// Moves to the next <see cref="ReadOnlyMemory{T}"/> in the <see cref="ReadOnlySequence{T}"/>
            /// </summary>
            /// <returns></returns>
            public bool MoveNext()
            {
                if (_next.GetObject() == null)
                {
                    return false;
                }

                return _sequence.TryGet(ref _next, out _currentMemory);
            }
        }

        private static Memory<T> ManagerMemory(object manager) => throw new InvalidOperationException();

        private enum SequenceType
        {
            MultiSegment = 0x00,
            Array = 0x1,
            MemoryManager = 0x2,
            String = 0x3,
        }
    }

    internal static class ReadOnlySequenceFlags
    {
        /// <summary>
        /// Flag that allows encoding the <see cref="ReadOnlySequence{T}.SequenceType"/>.
        /// </summary>
        /// <seealso cref="ReadOnlySequence{T}.GetSequenceType"/>
        public const int FlagBitMask = 1 << 31;
        public const int IndexBitMask = ~FlagBitMask;

        public const int ArrayEndMask = FlagBitMask;

        public const int MemoryManagerStartMask = FlagBitMask;

        public const int StringStartMask = FlagBitMask;
        public const int StringEndMask = FlagBitMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ArrayToSequenceEnd(int endIndex) => endIndex | ArrayEndMask;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MemoryManagerToSequenceStart(int startIndex) => startIndex | MemoryManagerStartMask;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int StringToSequenceStart(int startIndex) => startIndex | StringStartMask;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int StringToSequenceEnd(int endIndex) => endIndex | StringEndMask;
    }


    [Shadows("System.Buffers.SequenceReader`1")]
    public ref partial struct SequenceReaderImpl<T> where T : unmanaged, IEquatable<T>
    {
        private SequencePosition _currentPosition;
        private SequencePosition _nextPosition;
        private bool _moreData;
        private readonly long _length;

        /// <summary>
        /// Create a <see cref="SequenceReader{T}"/> over the given <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SequenceReaderImpl(ReadOnlySequenceImpl<T> sequence)
        {
            CurrentSpanIndex = 0;
            Consumed = 0;
            Sequence = sequence;
            _currentPosition = sequence.Start;
            _length = -1;

            sequence.GetFirstSpan(out ReadOnlySpan<T> first, out _nextPosition);
            CurrentSpan = first;
            _moreData = first.Length > 0;

            if (!_moreData && !sequence.IsSingleSegment)
            {
                _moreData = true;
                GetNextSpan();
            }
        }

        /// <summary>
        /// True when there is no more data in the <see cref="Sequence"/>.
        /// </summary>
        public readonly bool End => !_moreData;

        /// <summary>
        /// The underlying <see cref="ReadOnlySequence{T}"/> for the reader.
        /// </summary>
        public ReadOnlySequenceImpl<T> Sequence { get; }

        /// <summary>
        /// Gets the unread portion of the <see cref="Sequence"/>.
        /// </summary>
        /// <value>
        /// The unread portion of the <see cref="Sequence"/>.
        /// </value>
        public readonly ReadOnlySequenceImpl<T> UnreadSequence => Sequence.Slice(Position);

        /// <summary>
        /// The current position in the <see cref="Sequence"/>.
        /// </summary>
        public readonly SequencePosition Position
            => Sequence.GetPosition(CurrentSpanIndex, _currentPosition);

        /// <summary>
        /// The current segment in the <see cref="Sequence"/> as a span.
        /// </summary>
        public ReadOnlySpan<T> CurrentSpan { get; private set; }

        /// <summary>
        /// The index in the <see cref="CurrentSpan"/>.
        /// </summary>
        public int CurrentSpanIndex { get; private set; }

        /// <summary>
        /// The unread portion of the <see cref="CurrentSpan"/>.
        /// </summary>
        public readonly ReadOnlySpan<T> UnreadSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CurrentSpan.Slice(CurrentSpanIndex);
        }

        /// <summary>
        /// The total number of <typeparamref name="T"/>'s processed by the reader.
        /// </summary>
        public long Consumed { get; private set; }

        /// <summary>
        /// Remaining <typeparamref name="T"/>'s in the reader's <see cref="Sequence"/>.
        /// </summary>
        public readonly long Remaining => Length - Consumed;

        /// <summary>
        /// Count of <typeparamref name="T"/> in the reader's <see cref="Sequence"/>.
        /// </summary>
        public readonly long Length
        {
            get
            {
                return Sequence.Length;
            }
        }

        /// <summary>
        /// Peeks at the next value without advancing the reader.
        /// </summary>
        /// <param name="value">The next value or default if at the end.</param>
        /// <returns>False if at the end of the reader.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TryPeek(out T value)
        {
            if (_moreData)
            {
                value = CurrentSpan[CurrentSpanIndex];
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Peeks at the next value at specific offset without advancing the reader.
        /// </summary>
        /// <param name="offset">The offset from current position.</param>
        /// <param name="value">The next value, or the default value if at the end of the reader.</param>
        /// <returns><c>true</c> if the reader is not at its end and the peek operation succeeded; <c>false</c> if at the end of the reader.</returns>
        public readonly bool TryPeek(long offset, out T value)
        {
            if (offset < 0)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException_OffsetOutOfRange();

            // If we've got data and offset is not out of bounds
            if (!_moreData || Remaining <= offset)
            {
                value = default;
                return false;
            }

            // Sum CurrentSpanIndex + offset could overflow as is but the value of offset should be very large
            // because we check Remaining <= offset above so to overflow we should have a ReadOnlySequence close to 8 exabytes
            Debug.Assert(CurrentSpanIndex + offset >= 0);

            // If offset doesn't fall inside current segment move to next until we find correct one
            if ((CurrentSpanIndex + offset) <= CurrentSpan.Length - 1)
            {
                Debug.Assert(offset <= int.MaxValue);

                value = CurrentSpan[CurrentSpanIndex + (int)offset];
                return true;
            }
            else
            {
                long remainingOffset = offset - (CurrentSpan.Length - CurrentSpanIndex);
                SequencePosition nextPosition = _nextPosition;
                ReadOnlyMemory<T> currentMemory;

                while (Sequence.TryGet(ref nextPosition, out currentMemory, advance: true))
                {
                    // Skip empty segment
                    if (currentMemory.Length > 0)
                    {
                        if (remainingOffset >= currentMemory.Length)
                        {
                            // Subtract current non consumed data
                            remainingOffset -= currentMemory.Length;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                value = currentMemory.Span[(int)remainingOffset];
                return true;
            }
        }

        /// <summary>
        /// Read the next value and advance the reader.
        /// </summary>
        /// <param name="value">The next value or default if at the end.</param>
        /// <returns>False if at the end of the reader.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRead(out T value)
        {
            if (End)
            {
                value = default;
                return false;
            }

            value = CurrentSpan[CurrentSpanIndex];
            CurrentSpanIndex++;
            Consumed++;

            if (CurrentSpanIndex >= CurrentSpan.Length)
            {
                GetNextSpan();
            }

            return true;
        }

        /// <summary>
        /// Move the reader back the specified number of items.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if trying to rewind a negative amount or more than <see cref="Consumed"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rewind(long count)
        {
            if ((ulong)count > (ulong)Consumed)
            {
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.count);
            }

            if (count == 0)
            {
                return;
            }

            Consumed -= count;

            if (CurrentSpanIndex >= count)
            {
                CurrentSpanIndex -= (int)count;
                _moreData = true;
            }
            else
            {
                // Current segment doesn't have enough data, scan backward through segments
                RetreatToPreviousSpan(Consumed);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RetreatToPreviousSpan(long consumed)
        {
            ResetReader();
            Advance(consumed);
        }

        private void ResetReader()
        {
            CurrentSpanIndex = 0;
            Consumed = 0;
            _currentPosition = Sequence.Start;
            _nextPosition = _currentPosition;

            if (Sequence.TryGet(ref _nextPosition, out ReadOnlyMemory<T> memory, advance: true))
            {
                _moreData = true;

                if (memory.Length == 0)
                {
                    CurrentSpan = default;
                    // No data in the first span, move to one with data
                    GetNextSpan();
                }
                else
                {
                    CurrentSpan = memory.Span;
                }
            }
            else
            {
                // No data in any spans and at end of sequence
                _moreData = false;
                CurrentSpan = default;
            }
        }

        /// <summary>
        /// Get the next segment with available data, if any.
        /// </summary>
        private void GetNextSpan()
        {
            if (!Sequence.IsSingleSegment)
            {
                SequencePosition previousNextPosition = _nextPosition;
                while (Sequence.TryGet(ref _nextPosition, out ReadOnlyMemory<T> memory, advance: true))
                {
                    _currentPosition = previousNextPosition;
                    if (memory.Length > 0)
                    {
                        CurrentSpan = memory.Span;
                        CurrentSpanIndex = 0;
                        return;
                    }
                    else
                    {
                        CurrentSpan = default;
                        CurrentSpanIndex = 0;
                        previousNextPosition = _nextPosition;
                    }
                }
            }
            _moreData = false;
        }

        /// <summary>
        /// Move the reader ahead the specified number of items.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(long count)
        {
            const long TooBigOrNegative = unchecked((long)0xFFFFFFFF80000000);
            if ((count & TooBigOrNegative) == 0 && CurrentSpan.Length - CurrentSpanIndex > (int)count)
            {
                CurrentSpanIndex += (int)count;
                Consumed += count;
            }
            else
            {
                // Can't satisfy from the current span
                AdvanceToNextSpan(count);
            }
        }

        /// <summary>
        /// Unchecked helper to avoid unnecessary checks where you know count is valid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceCurrentSpan(long count)
        {
            Debug.Assert(count >= 0);

            Consumed += count;
            CurrentSpanIndex += (int)count;
            if (CurrentSpanIndex >= CurrentSpan.Length)
                GetNextSpan();
        }

        /// <summary>
        /// Only call this helper if you know that you are advancing in the current span
        /// with valid count and there is no need to fetch the next one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceWithinSpan(long count)
        {
            Debug.Assert(count >= 0);

            Consumed += count;
            CurrentSpanIndex += (int)count;

            Debug.Assert(CurrentSpanIndex < CurrentSpan.Length);
        }

        private void AdvanceToNextSpan(long count)
        {
            if (count < 0)
            {
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.count);
            }

            Consumed += count;
            while (_moreData)
            {
                int remaining = CurrentSpan.Length - CurrentSpanIndex;

                if (remaining > count)
                {
                    CurrentSpanIndex += (int)count;
                    count = 0;
                    break;
                }

                // As there may not be any further segments we need to
                // push the current index to the end of the span.
                CurrentSpanIndex += remaining;
                count -= remaining;
                Debug.Assert(count >= 0);

                GetNextSpan();

                if (count == 0)
                {
                    break;
                }
            }

            if (count != 0)
            {
                // Not enough data left- adjust for where we actually ended and throw
                Consumed -= count;
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.count);
            }
        }

        /// <summary>
        /// Copies data from the current <see cref="Position"/> to the given <paramref name="destination"/> span if there
        /// is enough data to fill it.
        /// </summary>
        /// <remarks>
        /// This API is used to copy a fixed amount of data out of the sequence if possible. It does not advance
        /// the reader. To look ahead for a specific stream of data <see cref="IsNext(ReadOnlySpan{T}, bool)"/> can be used.
        /// </remarks>
        /// <param name="destination">Destination span to copy to.</param>
        /// <returns>True if there is enough data to completely fill the <paramref name="destination"/> span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TryCopyTo(Span<T> destination)
        {
            // This API doesn't advance to facilitate conditional advancement based on the data returned.
            // We don't provide an advance option to allow easier utilizing of stack allocated destination spans.
            // (Because we can make this method readonly we can guarantee that we won't capture the span.)

            ReadOnlySpan<T> firstSpan = UnreadSpan;
            if (firstSpan.Length >= destination.Length)
            {
                firstSpan.Slice(0, destination.Length).CopyTo(destination);
                return true;
            }

            // Not enough in the current span to satisfy the request, fall through to the slow path
            return TryCopyMultisegment(destination);
        }

        internal readonly bool TryCopyMultisegment(Span<T> destination)
        {
            // If we don't have enough to fill the requested buffer, return false
            if (Remaining < destination.Length)
                return false;

            ReadOnlySpan<T> firstSpan = UnreadSpan;
            Debug.Assert(firstSpan.Length < destination.Length);
            firstSpan.CopyTo(destination);
            int copied = firstSpan.Length;

            SequencePosition next = _nextPosition;
            while (Sequence.TryGet(ref next, out ReadOnlyMemory<T> nextSegment, true))
            {
                if (nextSegment.Length > 0)
                {
                    ReadOnlySpan<T> nextSpan = nextSegment.Span;
                    int toCopy = Math.Min(nextSpan.Length, destination.Length - copied);
                    nextSpan.Slice(0, toCopy).CopyTo(destination.Slice(copied));
                    copied += toCopy;
                    if (copied >= destination.Length)
                    {
                        break;
                    }
                }
            }

            return true;
        }
    
        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySpan<T> span, T delimiter, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);

            if (index != -1)
            {
                span = index == 0 ? default : remaining.Slice(0, index);
                AdvanceCurrentSpan(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            return TryReadToSlow(out span, delimiter, advancePastDelimiter);
        }

        private bool TryReadToSlow(out ReadOnlySpan<T> span, T delimiter, bool advancePastDelimiter)
        {
            if (!TryReadToInternal(out ReadOnlySequenceImpl<T> sequence, delimiter, advancePastDelimiter, CurrentSpan.Length - CurrentSpanIndex))
            {
                span = default;
                return false;
            }

            span = sequence.IsSingleSegment ? sequence.First.Span : BuffersExtensionsImpl.ToArray(sequence);
            return true;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>, ignoring delimiters that are
        /// preceded by <paramref name="delimiterEscape"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="delimiterEscape">If found prior to <paramref name="delimiter"/> it will skip that occurrence.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySpan<T> span, T delimiter, T delimiterEscape, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);

            if ((index > 0 && !remaining[index - 1].Equals(delimiterEscape)) || index == 0)
            {
                span = remaining.Slice(0, index);
                AdvanceCurrentSpan(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            // This delimiter might be skipped, go down the slow path
            return TryReadToSlow(out span, delimiter, delimiterEscape, index, advancePastDelimiter);
        }

        private bool TryReadToSlow(out ReadOnlySpan<T> span, T delimiter, T delimiterEscape, int index, bool advancePastDelimiter)
        {
            if (!TryReadToSlow(out ReadOnlySequenceImpl<T> sequence, delimiter, delimiterEscape, index, advancePastDelimiter))
            {
                span = default;
                return false;
            }

            Debug.Assert(sequence.Length > 0);
            span = sequence.IsSingleSegment ? sequence.First.Span : BuffersExtensionsImpl.ToArray(sequence);
            return true;
        }

        private bool TryReadToSlow(out ReadOnlySequenceImpl<T> sequence, T delimiter, T delimiterEscape, int index, bool advancePastDelimiter)
        {
            SequenceReaderImpl<T> copy = this;

            ReadOnlySpan<T> remaining = UnreadSpan;
            bool priorEscape = false;

            do
            {
                if (index >= 0)
                {
                    if (index == 0 && priorEscape)
                    {
                        // We were in the escaped state, so skip this delimiter
                        priorEscape = false;
                        Advance(index + 1);
                        remaining = UnreadSpan;
                        goto Continue;
                    }
                    else if (index > 0 && remaining[index - 1].Equals(delimiterEscape))
                    {
                        // This delimiter might be skipped

                        // Count our escapes
                        int escapeCount = 1;
                        int i = index - 2;
                        for (; i >= 0; i--)
                        {
                            if (!remaining[i].Equals(delimiterEscape))
                                break;
                        }
                        if (i < 0 && priorEscape)
                        {
                            // Started and ended with escape, increment once more
                            escapeCount++;
                        }
                        escapeCount += index - 2 - i;

                        if ((escapeCount & 1) != 0)
                        {
                            // An odd escape count means we're currently escaped,
                            // skip the delimiter and reset escaped state.
                            Advance(index + 1);
                            priorEscape = false;
                            remaining = UnreadSpan;
                            goto Continue;
                        }
                    }

                    // Found the delimiter. Move to it, slice, then move past it.
                    AdvanceCurrentSpan(index);

                    sequence = Sequence.Slice(copy.Position, Position);
                    if (advancePastDelimiter)
                    {
                        Advance(1);
                    }
                    return true;
                }
                else
                {
                    // No delimiter, need to check the end of the span for odd number of escapes then advance
                    if (remaining.EndsWith(delimiterEscape))
                    {
                        int escapeCount = 1;
                        int i = remaining.Length - 2;
                        for (; i >= 0; i--)
                        {
                            if (!remaining[i].Equals(delimiterEscape))
                                break;
                        }

                        escapeCount += remaining.Length - 2 - i;
                        if (i < 0 && priorEscape)
                            priorEscape = (escapeCount & 1) == 0;   // equivalent to incrementing escapeCount before setting priorEscape
                        else
                            priorEscape = (escapeCount & 1) != 0;
                    }
                    else
                    {
                        priorEscape = false;
                    }
                }

                // Nothing in the current span, move to the end, checking for the skip delimiter
                AdvanceCurrentSpan(remaining.Length);
                remaining = CurrentSpan;

            Continue:
                index = remaining.IndexOf(delimiter);
            } while (!End);

            // Didn't find anything, reset our original state.
            this = copy;
            sequence = default;
            return false;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>.
        /// </summary>
        /// <param name="sequence">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySequenceImpl<T> sequence, T delimiter, bool advancePastDelimiter = true)
        {
            return TryReadToInternal(out sequence, delimiter, advancePastDelimiter);
        }

        private bool TryReadToInternal(out ReadOnlySequenceImpl<T> sequence, T delimiter, bool advancePastDelimiter, int skip = 0)
        {
            Debug.Assert(skip >= 0);
            SequenceReaderImpl<T> copy = this;
            if (skip > 0)
                Advance(skip);
            ReadOnlySpan<T> remaining = UnreadSpan;

            while (_moreData)
            {
                int index = remaining.IndexOf(delimiter);
                if (index != -1)
                {
                    // Found the delimiter. Move to it, slice, then move past it.
                    if (index > 0)
                    {
                        AdvanceCurrentSpan(index);
                    }

                    sequence = Sequence.Slice(copy.Position, Position);
                    if (advancePastDelimiter)
                    {
                        Advance(1);
                    }
                    return true;
                }

                AdvanceCurrentSpan(remaining.Length);
                remaining = CurrentSpan;
            }

            // Didn't find anything, reset our original state.
            this = copy;
            sequence = default;
            return false;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>, ignoring delimiters that are
        /// preceded by <paramref name="delimiterEscape"/>.
        /// </summary>
        /// <param name="sequence">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="delimiterEscape">If found prior to <paramref name="delimiter"/> it will skip that occurrence.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySequenceImpl<T> sequence, T delimiter, T delimiterEscape, bool advancePastDelimiter = true)
        {
            SequenceReaderImpl<T> copy = this;

            ReadOnlySpan<T> remaining = UnreadSpan;
            bool priorEscape = false;

            while (_moreData)
            {
                int index = remaining.IndexOf(delimiter);
                if (index != -1)
                {
                    if (index == 0 && priorEscape)
                    {
                        // We were in the escaped state, so skip this delimiter
                        priorEscape = false;
                        Advance(index + 1);
                        remaining = UnreadSpan;
                        continue;
                    }
                    else if (index > 0 && remaining[index - 1].Equals(delimiterEscape))
                    {
                        // This delimiter might be skipped

                        // Count our escapes
                        int escapeCount = 0;
                        for (int i = index; i > 0 && remaining[i - 1].Equals(delimiterEscape); i--, escapeCount++)
                            ;
                        if (escapeCount == index && priorEscape)
                        {
                            // Started and ended with escape, increment once more
                            escapeCount++;
                        }

                        priorEscape = false;
                        if ((escapeCount & 1) != 0)
                        {
                            // Odd escape count means we're in the escaped state, so skip this delimiter
                            Advance(index + 1);
                            remaining = UnreadSpan;
                            continue;
                        }
                    }

                    // Found the delimiter. Move to it, slice, then move past it.
                    if (index > 0)
                    {
                        Advance(index);
                    }

                    sequence = Sequence.Slice(copy.Position, Position);
                    if (advancePastDelimiter)
                    {
                        Advance(1);
                    }
                    return true;
                }

                // No delimiter, need to check the end of the span for odd number of escapes then advance
                {
                    int escapeCount = 0;
                    for (int i = remaining.Length; i > 0 && remaining[i - 1].Equals(delimiterEscape); i--, escapeCount++)
                        ;
                    if (priorEscape && escapeCount == remaining.Length)
                    {
                        escapeCount++;
                    }
                    priorEscape = escapeCount % 2 != 0;
                }

                // Nothing in the current span, move to the end, checking for the skip delimiter
                Advance(remaining.Length);
                remaining = CurrentSpan;
            }

            // Didn't find anything, reset our original state.
            this = copy;
            sequence = default;
            return false;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiters"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiters">The delimiters to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the first found instance of any of the given <paramref name="delimiters"/>.</param>
        /// <returns>True if any of the <paramref name="delimiters"/> were found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadToAny(out ReadOnlySpan<T> span, scoped ReadOnlySpan<T> delimiters, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = delimiters.Length == 2
                ? remaining.IndexOfAny(delimiters[0], delimiters[1])
                : remaining.IndexOfAny(delimiters);

            if (index != -1)
            {
                span = remaining.Slice(0, index);
                Advance(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            return TryReadToAnySlow(out span, delimiters, advancePastDelimiter);
        }

        private bool TryReadToAnySlow(out ReadOnlySpan<T> span, scoped ReadOnlySpan<T> delimiters, bool advancePastDelimiter)
        {
            if (!TryReadToAnyInternal(out ReadOnlySequenceImpl<T> sequence, delimiters, advancePastDelimiter, CurrentSpan.Length - CurrentSpanIndex))
            {
                span = default;
                return false;
            }

            span = sequence.IsSingleSegment ? sequence.First.Span : BuffersExtensionsImpl.ToArray(sequence);
            return true;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiters"/>.
        /// </summary>
        /// <param name="sequence">The read data, if any.</param>
        /// <param name="delimiters">The delimiters to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the first found instance of any of the given <paramref name="delimiters"/>.</param>
        /// <returns>True if any of the <paramref name="delimiters"/> were found.</returns>
        public bool TryReadToAny(out ReadOnlySequenceImpl<T> sequence, scoped ReadOnlySpan<T> delimiters, bool advancePastDelimiter = true)
        {
            return TryReadToAnyInternal(out sequence, delimiters, advancePastDelimiter);
        }

        private bool TryReadToAnyInternal(out ReadOnlySequenceImpl<T> sequence, scoped ReadOnlySpan<T> delimiters, bool advancePastDelimiter, int skip = 0)
        {
            SequenceReaderImpl<T> copy = this;
            if (skip > 0)
                Advance(skip);
            ReadOnlySpan<T> remaining = UnreadSpan;

            while (!End)
            {
                int index = delimiters.Length == 2
                    ? remaining.IndexOfAny(delimiters[0], delimiters[1])
                    : remaining.IndexOfAny(delimiters);

                if (index != -1)
                {
                    // Found one of the delimiters. Move to it, slice, then move past it.
                    if (index > 0)
                    {
                        AdvanceCurrentSpan(index);
                    }

                    sequence = Sequence.Slice(copy.Position, Position);
                    if (advancePastDelimiter)
                    {
                        Advance(1);
                    }
                    return true;
                }

                Advance(remaining.Length);
                remaining = CurrentSpan;
            }

            // Didn't find anything, reset our original state.
            this = copy;
            sequence = default;
            return false;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySpan<T> span, scoped ReadOnlySpan<T> delimiter, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);

            if (index >= 0)
            {
                span = remaining.Slice(0, index);
                AdvanceCurrentSpan(index + (advancePastDelimiter ? delimiter.Length : 0));
                return true;
            }

            // This delimiter might be skipped, go down the slow path
            return TryReadToSlow(out span, delimiter, advancePastDelimiter);
        }

        private bool TryReadToSlow(out ReadOnlySpan<T> span, scoped ReadOnlySpan<T> delimiter, bool advancePastDelimiter)
        {
            if (!TryReadTo(out ReadOnlySequenceImpl<T> sequence, delimiter, advancePastDelimiter))
            {
                span = default;
                return false;
            }

            Debug.Assert(sequence.Length > 0);
            span = sequence.IsSingleSegment ? sequence.First.Span : BuffersExtensionsImpl.ToArray(sequence);
            return true;
        }

        /// <summary>
        /// Try to read data until the entire given <paramref name="delimiter"/> matches.
        /// </summary>
        /// <param name="sequence">The read data, if any.</param>
        /// <param name="delimiter">The multi (T) delimiter.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySequenceImpl<T> sequence, scoped ReadOnlySpan<T> delimiter, bool advancePastDelimiter = true)
        {
            if (delimiter.Length == 0)
            {
                sequence = default;
                return true;
            }

            SequenceReaderImpl<T> copy = this;

            bool advanced = false;
            while (!End)
            {
                if (!TryReadTo(out sequence, delimiter[0], advancePastDelimiter: false))
                {
                    this = copy;
                    return false;
                }

                if (delimiter.Length == 1)
                {
                    if (advancePastDelimiter)
                    {
                        Advance(1);
                    }
                    return true;
                }

                if (IsNext(delimiter))
                {
                    // Probably a faster way to do this, potentially by avoiding the Advance in the previous TryReadTo call
                    if (advanced)
                    {
                        sequence = copy.Sequence.Slice(copy.Consumed, Consumed - copy.Consumed);
                    }

                    if (advancePastDelimiter)
                    {
                        Advance(delimiter.Length);
                    }
                    return true;
                }
                else
                {
                    Advance(1);
                    advanced = true;
                }
            }

            this = copy;
            sequence = default;
            return false;
        }

        /// <summary>
        /// Try to read data with given <paramref name="count"/>.
        /// </summary>
        /// <param name="count">Read count.</param>
        /// <param name="sequence">The read data, if successfully read requested <paramref name="count"/> data.</param>
        /// <returns><c>true</c> if remaining items in current <see cref="SequenceReader{T}" /> is enough for <paramref name="count"/>.</returns>
        public bool TryReadExact(int count, out ReadOnlySequenceImpl<T> sequence)
        {
            if (count < 0)
            {
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.count);
            }
            if (count > Remaining)
            {
                sequence = default;
                return false;
            }

            sequence = Sequence.Slice(Position, count);
            if (count != 0)
            {
                Advance(count);
            }
            return true;
        }

        /// <summary>
        /// Advance until the given <paramref name="delimiter"/>, if found.
        /// </summary>
        /// <param name="delimiter">The delimiter to search for.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the given <paramref name="delimiter"/> was found.</returns>
        public bool TryAdvanceTo(T delimiter, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);
            if (index != -1)
            {
                Advance(advancePastDelimiter ? index + 1 : index);
                return true;
            }

            return TryReadToInternal(out _, delimiter, advancePastDelimiter);
        }

        /// <summary>
        /// Advance until any of the given <paramref name="delimiters"/>, if found.
        /// </summary>
        /// <param name="delimiters">The delimiters to search for.</param>
        /// <param name="advancePastDelimiter">True to move past the first found instance of any of the given <paramref name="delimiters"/>.</param>
        /// <returns>True if any of the given <paramref name="delimiters"/> were found.</returns>
        public bool TryAdvanceToAny(scoped ReadOnlySpan<T> delimiters, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOfAny(delimiters);
            if (index != -1)
            {
                AdvanceCurrentSpan(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            return TryReadToAnyInternal(out _, delimiters, advancePastDelimiter);
        }

        /// <summary>
        /// Advance past consecutive instances of the given <paramref name="value"/>.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public long AdvancePast(T value)
        {
            long start = Consumed;

            do
            {
                // Advance past all matches in the current span
                int i;
                for (i = CurrentSpanIndex; i < CurrentSpan.Length && CurrentSpan[i].Equals(value); i++)
                {
                }

                int advanced = i - CurrentSpanIndex;
                if (advanced == 0)
                {
                    // Didn't advance at all in this span, exit.
                    break;
                }

                AdvanceCurrentSpan(advanced);

                // If we're at position 0 after advancing and not at the End,
                // we're in a new span and should continue the loop.
            } while (CurrentSpanIndex == 0 && !End);

            return Consumed - start;
        }

        /// <summary>
        /// Skip consecutive instances of any of the given <paramref name="values"/>.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public long AdvancePastAny(scoped ReadOnlySpan<T> values)
        {
            long start = Consumed;

            do
            {
                // Advance past all matches in the current span
                int i;
                for (i = CurrentSpanIndex; i < CurrentSpan.Length && values.Contains(CurrentSpan[i]); i++)
                {
                }

                int advanced = i - CurrentSpanIndex;
                if (advanced == 0)
                {
                    // Didn't advance at all in this span, exit.
                    break;
                }

                AdvanceCurrentSpan(advanced);

                // If we're at position 0 after advancing and not at the End,
                // we're in a new span and should continue the loop.
            } while (CurrentSpanIndex == 0 && !End);

            return Consumed - start;
        }

        /// <summary>
        /// Advance past consecutive instances of any of the given values.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public long AdvancePastAny(T value0, T value1, T value2, T value3)
        {
            long start = Consumed;

            do
            {
                // Advance past all matches in the current span
                int i;
                for (i = CurrentSpanIndex; i < CurrentSpan.Length; i++)
                {
                    T value = CurrentSpan[i];
                    if (!value.Equals(value0) && !value.Equals(value1) && !value.Equals(value2) && !value.Equals(value3))
                    {
                        break;
                    }
                }

                int advanced = i - CurrentSpanIndex;
                if (advanced == 0)
                {
                    // Didn't advance at all in this span, exit.
                    break;
                }

                AdvanceCurrentSpan(advanced);

                // If we're at position 0 after advancing and not at the End,
                // we're in a new span and should continue the loop.
            } while (CurrentSpanIndex == 0 && !End);

            return Consumed - start;
        }

        /// <summary>
        /// Advance past consecutive instances of any of the given values.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public long AdvancePastAny(T value0, T value1, T value2)
        {
            long start = Consumed;

            do
            {
                // Advance past all matches in the current span
                int i;
                for (i = CurrentSpanIndex; i < CurrentSpan.Length; i++)
                {
                    T value = CurrentSpan[i];
                    if (!value.Equals(value0) && !value.Equals(value1) && !value.Equals(value2))
                    {
                        break;
                    }
                }

                int advanced = i - CurrentSpanIndex;
                if (advanced == 0)
                {
                    // Didn't advance at all in this span, exit.
                    break;
                }

                AdvanceCurrentSpan(advanced);

                // If we're at position 0 after advancing and not at the End,
                // we're in a new span and should continue the loop.
            } while (CurrentSpanIndex == 0 && !End);

            return Consumed - start;
        }

        /// <summary>
        /// Advance past consecutive instances of any of the given values.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public long AdvancePastAny(T value0, T value1)
        {
            long start = Consumed;

            do
            {
                // Advance past all matches in the current span
                int i;
                for (i = CurrentSpanIndex; i < CurrentSpan.Length; i++)
                {
                    T value = CurrentSpan[i];
                    if (!value.Equals(value0) && !value.Equals(value1))
                    {
                        break;
                    }
                }

                int advanced = i - CurrentSpanIndex;
                if (advanced == 0)
                {
                    // Didn't advance at all in this span, exit.
                    break;
                }

                AdvanceCurrentSpan(advanced);

                // If we're at position 0 after advancing and not at the End,
                // we're in a new span and should continue the loop.
            } while (CurrentSpanIndex == 0 && !End);

            return Consumed - start;
        }

        /// <summary>
        /// Moves the reader to the end of the sequence.
        /// </summary>
        public void AdvanceToEnd()
        {
            if (_moreData)
            {
                Consumed = Length;
                CurrentSpan = default;
                CurrentSpanIndex = 0;
                _currentPosition = Sequence.End;
                _nextPosition = default;
                _moreData = false;
            }
        }

        /// <summary>
        /// Check to see if the given <paramref name="next"/> value is next.
        /// </summary>
        /// <param name="next">The value to compare the next items to.</param>
        /// <param name="advancePast">Move past the <paramref name="next"/> value if found.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNext(T next, bool advancePast = false)
        {
            if (End)
                return false;

            if (CurrentSpan[CurrentSpanIndex].Equals(next))
            {
                if (advancePast)
                {
                    AdvanceCurrentSpan(1);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Check to see if the given <paramref name="next"/> values are next.
        /// </summary>
        /// <param name="next">The span to compare the next items to.</param>
        /// <param name="advancePast">Move past the <paramref name="next"/> values if found.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNext(scoped ReadOnlySpan<T> next, bool advancePast = false)
        {
            ReadOnlySpan<T> unread = UnreadSpan;
            if (unread.StartsWith(next))
            {
                if (advancePast)
                {
                    AdvanceCurrentSpan(next.Length);
                }
                return true;
            }

            // Only check the slow path if there wasn't enough to satisfy next
            return unread.Length < next.Length && IsNextSlow(next, advancePast);
        }

        private bool IsNextSlow(scoped ReadOnlySpan<T> next, bool advancePast)
        {
            ReadOnlySpan<T> currentSpan = UnreadSpan;

            // We should only come in here if we need more data than we have in our current span
            Debug.Assert(currentSpan.Length < next.Length);

            int fullLength = next.Length;
            SequencePosition nextPosition = _nextPosition;

            while (next.StartsWith(currentSpan))
            {
                if (next.Length == currentSpan.Length)
                {
                    // Fully matched
                    if (advancePast)
                    {
                        Advance(fullLength);
                    }
                    return true;
                }

                // Need to check the next segment
                while (true)
                {
                    if (!Sequence.TryGet(ref nextPosition, out ReadOnlyMemory<T> nextSegment, advance: true))
                    {
                        // Nothing left
                        return false;
                    }

                    if (nextSegment.Length > 0)
                    {
                        next = next.Slice(currentSpan.Length);
                        currentSpan = nextSegment.Span;
                        if (currentSpan.Length > next.Length)
                        {
                            currentSpan = currentSpan.Slice(0, next.Length);
                        }
                        break;
                    }
                }
            }

            return false;
        }
    
    }


    /// <summary>
    /// Extension methods for <see cref="ReadOnlySequence{T}"/>
    /// </summary>
    [Shadows("System.Buffers.BuffersExtensions")]
    public static class BuffersExtensionsImpl
    {
        /// <summary>
        /// Returns position of first occurrence of item in the <see cref="ReadOnlySequence{T}"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SequencePosition? PositionOf<T>(in ReadOnlySequenceImpl<T> source, T value) where T : IEquatable<T>?
        {
            if (source.IsSingleSegment)
            {
                int index = source.First.Span.IndexOf(value);
                if (index != -1)
                {
                    return source.Seek(index);
                }

                return null;
            }
            else
            {
                return PositionOfMultiSegment(source, value);
            }
        }

        private static SequencePosition? PositionOfMultiSegment<T>(in ReadOnlySequenceImpl<T> source, T value) where T : IEquatable<T>?
        {
            SequencePosition position = source.Start;
            SequencePosition result = position;
            while (source.TryGet(ref position, out ReadOnlyMemory<T> memory))
            {
                int index = memory.Span.IndexOf(value);
                if (index != -1)
                {
                    return source.GetPosition(index, result);
                }
                else if (position.GetObject() == null)
                {
                    break;
                }

                result = position;
            }

            return null;
        }

        /// <summary>
        /// Copy the <see cref="ReadOnlySequence{T}"/> to the specified <see cref="Span{Byte}"/>.
        /// </summary>
        /// <param name="source">The source <see cref="ReadOnlySequence{T}"/>.</param>
        /// <param name="destination">The destination <see cref="Span{Byte}"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyTo<T>(in ReadOnlySequenceImpl<T> source, Span<T> destination)
        {
            if (source.IsSingleSegment)
            {
                ReadOnlySpan<T> span = source.First.Span;
                if (span.Length > destination.Length)
                    SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.destination);
                span.CopyTo(destination);
            }
            else
            {
                CopyToMultiSegment(source, destination);
            }
        }

        private static void CopyToMultiSegment<T>(in ReadOnlySequenceImpl<T> sequence, Span<T> destination)
        {
            if (sequence.Length > destination.Length)
                SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.destination);

            SequencePosition position = sequence.Start;
            while (sequence.TryGet(ref position, out ReadOnlyMemory<T> memory))
            {
                ReadOnlySpan<T> span = memory.Span;
                span.CopyTo(destination);
                if (position.GetObject() != null)
                {
                    destination = destination.Slice(span.Length);
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Converts the <see cref="ReadOnlySequence{T}"/> to an array
        /// </summary>
        public static T[] ToArray<T>(in ReadOnlySequenceImpl<T> sequence)
        {
            var array = new T[sequence.Length];
            CopyTo(sequence, array);
            return array;
        }

        /// <summary>
        /// Writes contents of <paramref name="value"/> to <paramref name="writer"/>
        /// </summary>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the <paramref name="writer"/> is shorter than the <paramref name="value"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<T>(IBufferWriter<T> writer, ReadOnlySpan<T> value)
        {
            Span<T> destination = writer.GetSpan();

            // Fast path, try copying to the available memory directly
            if (value.Length <= destination.Length)
            {
                value.CopyTo(destination);
                writer.Advance(value.Length);
            }
            else
            {
                WriteMultiSegment(writer, value, destination);
            }
        }

        private static void WriteMultiSegment<T>(IBufferWriter<T> writer, in ReadOnlySpan<T> source, Span<T> destination)
        {
            ReadOnlySpan<T> input = source;
            while (true)
            {
                int writeSize = Math.Min(destination.Length, input.Length);
                input.Slice(0, writeSize).CopyTo(destination);
                writer.Advance(writeSize);
                input = input.Slice(writeSize);
                if (input.Length > 0)
                {
                    destination = writer.GetSpan();

                    if (destination.IsEmpty)
                    {
                        SequenceThrowHelper.ThrowArgumentOutOfRangeException(SequenceArgument.writer);
                    }

                    continue;
                }

                return;
            }
        }
    }

    [Shadows("System.Buffers.SequenceReaderExtensions")]
    public static class SequenceReaderExtensionsImpl
    {
        private static bool TryReadBytes(ref SequenceReaderImpl<byte> reader, scoped Span<byte> destination)
        {
            if (!reader.TryCopyTo(destination)) return false;
            reader.Advance(destination.Length);
            return true;
        }

        public static bool TryReadLittleEndian(ref SequenceReaderImpl<byte> reader, out short value)
        {
            Span<byte> bytes = stackalloc byte[2];
            bool read = TryReadBytes(ref reader, bytes);
            value = read ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : (short)0;
            return read;
        }

        public static bool TryReadBigEndian(ref SequenceReaderImpl<byte> reader, out short value)
        {
            Span<byte> bytes = stackalloc byte[2];
            bool read = TryReadBytes(ref reader, bytes);
            value = read ? BinaryPrimitives.ReadInt16BigEndian(bytes) : (short)0;
            return read;
        }

        public static bool TryReadLittleEndian(ref SequenceReaderImpl<byte> reader, out int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            bool read = TryReadBytes(ref reader, bytes);
            value = read ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : 0;
            return read;
        }

        public static bool TryReadBigEndian(ref SequenceReaderImpl<byte> reader, out int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            bool read = TryReadBytes(ref reader, bytes);
            value = read ? BinaryPrimitives.ReadInt32BigEndian(bytes) : 0;
            return read;
        }

        public static bool TryReadLittleEndian(ref SequenceReaderImpl<byte> reader, out long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            bool read = TryReadBytes(ref reader, bytes);
            value = read ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : 0L;
            return read;
        }

        public static bool TryReadBigEndian(ref SequenceReaderImpl<byte> reader, out long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            bool read = TryReadBytes(ref reader, bytes);
            value = read ? BinaryPrimitives.ReadInt64BigEndian(bytes) : 0L;
            return read;
        }
    }
}
