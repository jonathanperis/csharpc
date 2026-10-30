// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Collections.BitArray (System.Private.CoreLib in .NET 11): dotnet/runtime's BitArray.cs with its
// vectorized loops replaced by their scalar remainders — same storage (bytes, allocated in whole Int32s),
// same checks, messages and version bumps. Every csharpc target is little-endian. See THIRD-PARTY-NOTICES.md.
#nullable enable
using System;
using System.Collections;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.BitArray")]
    public sealed class BitArrayImpl : ICollection, ICloneable
    {
        private const int BitsPerInt32 = 32;
        private const int BitsPerByte = 8;

        internal byte[] _array;
        private int _bitLength;
        private int _version;

        public BitArrayImpl(int length) : this(length, false) { }

        public BitArrayImpl(int length, bool defaultValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            _array = AllocateByteArray(length);
            _bitLength = length;
            if (defaultValue)
            {
                Array.Fill(_array, (byte)0xFF);
                ClearHighExtraBits();
            }
        }

        public BitArrayImpl(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            _array = CreateArray(bytes, out _bitLength);
        }

        public BitArrayImpl(ReadOnlySpan<byte> bytes) { _array = CreateArray(bytes, out _bitLength); }

        private static byte[] CreateArray(ReadOnlySpan<byte> bytes, out int bitLength)
        {
            if (bytes.Length > int.MaxValue / BitsPerByte)
                throw new ArgumentException(ArrayTooLarge(BitsPerByte), nameof(bytes));
            bitLength = bytes.Length * BitsPerByte;
            byte[] array = AllocateByteArray(bitLength);
            bytes.CopyTo(array);
            return array;
        }

        public BitArrayImpl(bool[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _array = CreateArray(values, out _bitLength);
        }

        public BitArrayImpl(ReadOnlySpan<bool> values) { _array = CreateArray(values, out _bitLength); }

        private static byte[] CreateArray(ReadOnlySpan<bool> values, out int bitLength)
        {
            bitLength = values.Length;
            byte[] array = AllocateByteArray(bitLength);
            for (int i = 0; i < values.Length; i++)
                if (values[i]) array[i / BitsPerByte] |= (byte)(1 << (i % BitsPerByte));
            return array;
        }

        public BitArrayImpl(int[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _array = CreateArray(values, out _bitLength);
        }

        public BitArrayImpl(ReadOnlySpan<int> values) { _array = CreateArray(values, out _bitLength); }

        private static byte[] CreateArray(ReadOnlySpan<int> values, out int bitLength)
        {
            if (values.Length > int.MaxValue / BitsPerInt32)
                throw new ArgumentException(ArrayTooLarge(BitsPerInt32), nameof(values));
            bitLength = values.Length * BitsPerInt32;
            byte[] array = AllocateByteArray(bitLength);
            for (int i = 0; i < values.Length; i++) SetInt32(array, i, values[i]);
            return array;
        }

        public BitArrayImpl(BitArray bits)
        {
            ArgumentNullException.ThrowIfNull(bits);
            var other = Of(bits);
            _bitLength = other._bitLength;
            _array = AllocateByteArray(_bitLength);
            Array.Copy(other._array, _array, _array.Length);
        }

        private static BitArrayImpl Of(BitArray bits) => (BitArrayImpl)(object)bits;

        public bool this[int index]
        {
            get => Get(index);
            set => Set(index, value);
        }

        public bool Get(int index)
        {
            if ((uint)index >= (uint)_bitLength) ThrowArgumentOutOfRangeException(index);
            return (_array[index / BitsPerByte] & (1 << (index % BitsPerByte))) != 0;
        }

        public void Set(int index, bool value)
        {
            if ((uint)index >= (uint)_bitLength) ThrowArgumentOutOfRangeException(index);
            byte bitMask = (byte)(1 << (index % BitsPerByte));
            if (value) _array[index / BitsPerByte] |= bitMask;
            else _array[index / BitsPerByte] &= (byte)~bitMask;
            _version++;
        }

        public void SetAll(bool value)
        {
            int byteLength = GetByteArrayLengthFromBitLength(_bitLength);
            if (value)
            {
                _array.AsSpan(0, byteLength).Fill(0xFF);
                ClearHighExtraBits();
            }
            else
            {
                _array.AsSpan(0, byteLength).Clear();
            }
            _version++;
        }

        public BitArray And(BitArray value) => Invoke(value, 0);
        public BitArray Or(BitArray value) => Invoke(value, 1);
        public BitArray Xor(BitArray value) => Invoke(value, 2);

        public BitArray Not()
        {
            int ints = GetInt32ArrayLengthFromBitLength(_bitLength);
            for (int i = 0; i < ints; i++) SetInt32(_array, i, ~GetInt32(_array, i));
            _version++;
            ClearHighExtraBits(); // applying ~ to last Int32 may set extra bits we're trying to keep clear
            return (BitArray)(object)this;
        }

        private BitArray Invoke(BitArray value, int op)
        {
            ArgumentNullException.ThrowIfNull(value);
            var other = Of(value);
            byte[] thisArray = _array;
            byte[] valueArray = other._array;
            int count = GetByteArrayLengthFromBitLength(Length);
            if (Length != other.Length || (uint)count > (uint)thisArray.Length || (uint)count > (uint)valueArray.Length)
                throw new ArgumentException("Array lengths must be the same.");
            int ints = RoundUpToMultipleSizeOfInt32(count) / sizeof(int);
            for (int i = 0; i < ints; i++)
            {
                int a = GetInt32(thisArray, i), b = GetInt32(valueArray, i);
                SetInt32(thisArray, i, op == 0 ? a & b : op == 1 ? a | b : a ^ b);
            }
            _version++;
            return (BitArray)(object)this;
        }

        public BitArray RightShift(int count)
        {
            if (count <= 0)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                _version++;
                return (BitArray)(object)this;
            }
            int toIndex = 0;
            int ints = GetInt32ArrayLengthFromBitLength(_bitLength);
            if (count < _bitLength)
            {
                int fromIndex = count / 32, shiftCount = count % 32;
                int extraBits = (int)((uint)_bitLength % 32);
                if (shiftCount == 0)
                {
                    uint mask = uint.MaxValue >> (BitsPerInt32 - extraBits);
                    SetInt32(_array, ints - 1, GetInt32(_array, ints - 1) & (int)mask);
                    for (int i = 0; i < ints - fromIndex; i++) SetInt32(_array, i, GetInt32(_array, fromIndex + i));
                    toIndex = ints - fromIndex;
                }
                else
                {
                    int lastIndex = ints - 1;
                    while (fromIndex < lastIndex)
                    {
                        uint right = (uint)GetInt32(_array, fromIndex) >> shiftCount;
                        int left = GetInt32(_array, ++fromIndex) << (BitsPerInt32 - shiftCount);
                        SetInt32(_array, toIndex++, left | (int)right);
                    }
                    uint mask = uint.MaxValue >> (BitsPerInt32 - extraBits);
                    mask &= (uint)GetInt32(_array, fromIndex);
                    SetInt32(_array, toIndex++, (int)(mask >> shiftCount));
                }
            }
            for (int i = toIndex; i < ints; i++) SetInt32(_array, i, 0);
            _version++;
            return (BitArray)(object)this;
        }

        public BitArray LeftShift(int count)
        {
            if (count <= 0)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                _version++;
                return (BitArray)(object)this;
            }
            int lengthToClear;
            if (count < _bitLength)
            {
                int lastIndex = (int)((uint)(_bitLength - 1) / BitsPerInt32);
                lengthToClear = count / BitsPerInt32;
                int shiftCount = count % BitsPerInt32;
                if (shiftCount == 0)
                {
                    for (int i = lastIndex - lengthToClear; i >= 0; i--) SetInt32(_array, i + lengthToClear, GetInt32(_array, i));
                }
                else
                {
                    int fromindex = lastIndex - lengthToClear;
                    while (fromindex > 0)
                    {
                        int left = GetInt32(_array, fromindex) << shiftCount;
                        uint right = (uint)GetInt32(_array, --fromindex) >> (BitsPerInt32 - shiftCount);
                        SetInt32(_array, lastIndex, left | (int)right);
                        lastIndex--;
                    }
                    SetInt32(_array, lastIndex, GetInt32(_array, fromindex) << shiftCount);
                }
                ClearHighExtraBits();
            }
            else
            {
                lengthToClear = GetInt32ArrayLengthFromBitLength(_bitLength); // Clear all
            }
            for (int i = 0; i < lengthToClear; i++) SetInt32(_array, i, 0);
            _version++;
            return (BitArray)(object)this;
        }

        public int Length
        {
            get => _bitLength;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                int newByteLength = GetAlignedByteArrayLength(value);
                if (newByteLength > _array.Length)
                {
                    Array.Resize(ref _array, newByteLength);
                }
                else
                {
                    int currentByteLength = GetByteArrayLengthFromBitLength(_bitLength);
                    if (newByteLength > currentByteLength)
                    {
                        _array.AsSpan(currentByteLength).Clear();
                    }
                    else
                    {
                        // If we'll be shrinking by a significant amount, re-allocate to avoid wasting too much space.
                        const int ShrinkThreshold = 1024;
                        if (newByteLength < _array.Length - ShrinkThreshold) Array.Resize(ref _array, newByteLength);
                    }
                }
                _bitLength = value;
                ClearHighExtraBits();
                _version++;
            }
        }

        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (array.Rank != 1)
                throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", nameof(array));
            if (array is int[] intArray)
            {
                int intLength = GetInt32ArrayLengthFromBitLength(_bitLength);
                if (array.Length - index < intLength)
                    throw new ArgumentException(InvalidOffLen);
                for (int i = 0; i < intLength; i++) intArray[index + i] = GetInt32(_array, i);
                uint extraBits = (uint)_bitLength % BitsPerInt32;
                if (intLength > 0 && extraBits != 0)
                    intArray[index + intLength - 1] = GetInt32(_array, intLength - 1) & ((1 << (int)extraBits) - 1);
            }
            else if (array is byte[] byteArray)
            {
                int byteLength = GetByteArrayLengthFromBitLength(_bitLength);
                if ((array.Length - index) < byteLength)
                    throw new ArgumentException(InvalidOffLen);
                if (byteLength > 0)
                {
                    _array.AsSpan(0, byteLength).CopyTo(byteArray.AsSpan(index));
                    uint extraBits = (uint)_bitLength % BitsPerByte;
                    if (extraBits != 0)
                        byteArray[index + byteLength - 1] = (byte)(_array[byteLength - 1] & ((1 << (int)extraBits) - 1));
                }
            }
            else if (array is bool[] boolArray)
            {
                if (boolArray.Length - index < _bitLength)
                    throw new ArgumentException(InvalidOffLen);
                for (int i = 0; i < _bitLength; i++)
                    boolArray[index + i] = (_array[i / BitsPerByte] & (1 << (i % BitsPerByte))) != 0;
            }
            else
            {
                throw new ArgumentException("Only supported array types for CopyTo on BitArrays are Boolean[], Int32[] and Byte[].", nameof(array));
            }
        }

        public bool HasAllSet()
        {
            uint extraBits = (uint)_bitLength % BitsPerByte;
            int byteCount = GetByteArrayLengthFromBitLength(_bitLength);
            if (extraBits != 0) byteCount--;
            for (int i = 0; i < byteCount; i++) if (_array[i] != 0xFF) return false;
            if (extraBits == 0) return true;
            byte mask = (byte)((1 << (int)extraBits) - 1);
            return (_array[byteCount] & mask) == mask;
        }

        public bool HasAnySet()
        {
            uint extraBits = (uint)_bitLength % BitsPerByte;
            int byteCount = GetByteArrayLengthFromBitLength(_bitLength);
            if (extraBits != 0) byteCount--;
            for (int i = 0; i < byteCount; i++) if (_array[i] != 0) return true;
            if (extraBits == 0) return false;
            byte mask = (byte)((1 << (int)extraBits) - 1);
            return (_array[byteCount] & mask) != 0;
        }

        public int PopCount()
        {
            int count = 0;
            for (int i = 0; i < _array.Length / sizeof(int); i++) count += int.PopCount(GetInt32(_array, i));
            return count;
        }

        public int Count => _bitLength;
        public object SyncRoot => this;
        public bool IsSynchronized => false;
        public bool IsReadOnly => false;
        public object Clone() => new BitArrayImpl((BitArray)(object)this);
        public IEnumerator GetEnumerator() => new BitArrayEnumeratorSimple(this);

        private static int GetInt32(byte[] a, int i) =>
            a[i * 4] | (a[i * 4 + 1] << 8) | (a[i * 4 + 2] << 16) | (a[i * 4 + 3] << 24);

        private static void SetInt32(byte[] a, int i, int value)
        {
            a[i * 4] = (byte)value;
            a[i * 4 + 1] = (byte)(value >> 8);
            a[i * 4 + 2] = (byte)(value >> 16);
            a[i * 4 + 3] = (byte)(value >> 24);
        }

        private void ClearHighExtraBits()
        {
            uint index = (uint)_bitLength / BitsPerInt32, extraBits = (uint)_bitLength % BitsPerInt32;
            if (extraBits != 0) SetInt32(_array, (int)index, GetInt32(_array, (int)index) & ((1 << (int)extraBits) - 1));
        }

        private static int GetInt32ArrayLengthFromBitLength(int bitLength) => (int)(((uint)bitLength + 31u) >> 5);
        internal static int GetByteArrayLengthFromBitLength(int bitLength) => (int)(((uint)bitLength + 7u) >> 3);
        private static int RoundUpToMultipleSizeOfInt32(int value) => (value + (sizeof(int) - 1)) & ~(sizeof(int) - 1);
        private static int GetAlignedByteArrayLength(int bitLength) => RoundUpToMultipleSizeOfInt32(GetByteArrayLengthFromBitLength(bitLength));
        private static byte[] AllocateByteArray(int bitLength) => bitLength != 0 ? new byte[GetAlignedByteArrayLength(bitLength)] : Array.Empty<byte>();

        private const string InvalidOffLen = "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
        private static string ArrayTooLarge(int bits) => "The input array length must not exceed Int32.MaxValue / " + bits + ". Otherwise BitArray.Length would exceed Int32.MaxValue.";

        private static void ThrowArgumentOutOfRangeException(int index) =>
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");

        private sealed class BitArrayEnumeratorSimple : IEnumerator, ICloneable
        {
            private readonly BitArrayImpl _bitArray;
            private readonly int _version;
            private int _index;
            private object _currentElement = false;

            internal BitArrayEnumeratorSimple(BitArrayImpl bitArray)
            {
                _bitArray = bitArray;
                _index = -1;
                _version = bitArray._version;
            }

            public object Clone() => MemberwiseClone();

            public bool MoveNext()
            {
                if (_version != _bitArray._version)
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                if (_index < (_bitArray._bitLength - 1))
                {
                    _index++;
                    _currentElement = _bitArray.Get(_index);
                    return true;
                }
                _index = _bitArray._bitLength;
                return false;
            }

            public object Current
            {
                get
                {
                    if ((uint)_index >= (uint)_bitArray._bitLength)
                        throw new InvalidOperationException(_index == -1 ? "Enumeration has not started. Call MoveNext." : "Enumeration already finished.");
                    return _currentElement;
                }
            }

            public void Reset()
            {
                if (_version != _bitArray._version)
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                _index = -1;
            }
        }
    }
}
