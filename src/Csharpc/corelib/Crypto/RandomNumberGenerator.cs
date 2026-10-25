// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/System/
// Security/Cryptography/RandomNumberGenerator{,Implementation}.cs and CryptographicOperations.cs; see
// THIRD-PARTY-NOTICES.md. The bytes come from the OS (getentropy; BCryptGenRandom-equivalent on Windows),
// as .NET's do.
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    [Shadows("System.Security.Cryptography.RandomNumberGenerator")]
    public abstract class RandomNumberGeneratorImpl : IDisposable
    {
        private static RandomNumberGenerator s_singleton;

        protected RandomNumberGeneratorImpl() { }

        public static RandomNumberGenerator Create() => s_singleton ??= (RandomNumberGenerator)(object)new SystemRandomNumberGenerator();

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing) { }

        public abstract void GetBytes(byte[] data);

        public virtual void GetBytes(byte[] data, int offset, int count)
        {
            VerifyGetBytes(data, offset, count);
            if (count > 0)
            {
                if (offset == 0 && count == data.Length) GetBytes(data);
                else
                {
                    var tempData = new byte[count];
                    GetBytes(tempData);
                    Buffer.BlockCopy(tempData, 0, data, offset, count);
                }
            }
        }

        public virtual void GetBytes(Span<byte> data)
        {
            byte[] array = new byte[data.Length];
            GetBytes(array, 0, data.Length);
            new ReadOnlySpan<byte>(array, 0, data.Length).CopyTo(data);
            Array.Clear(array, 0, data.Length);
        }

        public virtual void GetNonZeroBytes(byte[] data) => throw new NotImplementedException();

        public virtual void GetNonZeroBytes(Span<byte> data)
        {
            byte[] array = new byte[data.Length];
            GetNonZeroBytes(array);
            new ReadOnlySpan<byte>(array, 0, data.Length).CopyTo(data);
            Array.Clear(array, 0, data.Length);
        }

        public static void Fill(Span<byte> data) => FillSpan(data);

        public static int GetInt32(int fromInclusive, int toExclusive)
        {
            if (fromInclusive >= toExclusive)
                throw new ArgumentException("Range of random number does not contain at least one possibility.");
            // The same masked rejection sampling as .NET (no modulo bias).
            uint range = (uint)toExclusive - (uint)fromInclusive - 1;
            if (range == 0) return fromInclusive;
            uint mask = range;
            mask |= mask >> 1;
            mask |= mask >> 2;
            mask |= mask >> 4;
            mask |= mask >> 8;
            mask |= mask >> 16;
            uint result;
            Span<byte> bytes = stackalloc byte[4];
            do
            {
                FillSpan(bytes);
                result = mask & (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);
            }
            while (result > range);
            return (int)result + fromInclusive;
        }

        public static int GetInt32(int toExclusive)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toExclusive);
            return GetInt32(0, toExclusive);
        }

        public static byte[] GetBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            byte[] ret = new byte[count];
            FillSpan(ret);
            return ret;
        }

        internal static unsafe void FillSpan(Span<byte> data)
        {
            if (data.Length > 0)
                fixed (byte* ptr = data) CoreDebug.RandomBytes(ptr, data.Length);
        }

        internal static void FillNonZeroBytes(Span<byte> data)
        {
            while (data.Length > 0)
            {
                FillSpan(data);
                int first0Byte = data.IndexOf((byte)0);
                if (first0Byte < 0) return;
                // Keep the bytes before the first zero; refill from there.
                data = data.Slice(first0Byte);
                int nonZero = 0;
                for (int i = 1; i < data.Length; i++)
                    if (data[i] != 0) data[nonZero++] = data[i];
                data = data.Slice(nonZero);
            }
        }

        internal static void VerifyGetBytes(byte[] data, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > data.Length - offset)
                throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }
    }

    /// <summary>RandomNumberGeneratorImplementation: the OS generator RandomNumberGenerator.Create() hands out.</summary>
    internal sealed class SystemRandomNumberGenerator : RandomNumberGeneratorImpl
    {
        public override void GetBytes(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            FillSpan(data);
        }

        public override void GetBytes(byte[] data, int offset, int count)
        {
            VerifyGetBytes(data, offset, count);
            FillSpan(new Span<byte>(data, offset, count));
        }

        public override void GetBytes(Span<byte> data) => FillSpan(data);

        public override void GetNonZeroBytes(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            FillNonZeroBytes(data);
        }

        public override void GetNonZeroBytes(Span<byte> data) => FillNonZeroBytes(data);
    }

    [ShadowMembers("System.Security.Cryptography.CryptographicOperations")]
    internal static class CryptographicOperationsMembers
    {
        // Every byte is compared whatever the earlier ones were: the time doesn't depend on where they differ.
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length) return false;
            int accum = 0;
            for (int i = 0; i < left.Length; i++) accum |= left[i] - right[i];
            return accum == 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void ZeroMemory(Span<byte> buffer) => buffer.Clear();
    }
}
