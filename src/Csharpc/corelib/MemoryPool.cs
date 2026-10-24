// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Buffers.MemoryPool<T> and its shared ArrayMemoryPool<T>, ported from dotnet/runtime (release/11.0),
// src/libraries/System.Memory/src/System/Buffers/{MemoryPool,ArrayMemoryPool,ArrayMemoryPool.ArrayMemoryPoolBuffer}.cs;
// see THIRD-PARTY-NOTICES.md. The buffers come from ArrayPool<T>.Shared (RuntimeServices.cs), so their
// lengths are .NET's (the default size is 4 KB worth of elements, rounded up to the pool's bucket size).
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [Shadows("System.Buffers.MemoryPool`1")]
    public abstract class MemoryPoolImpl<T> : IDisposable
    {
        private static MemoryPool<T> s_shared;

        public static MemoryPool<T> Shared => s_shared ??= new ArrayMemoryPoolImpl<T>();

        public abstract IMemoryOwner<T> Rent(int minBufferSize = -1);

        public abstract int MaxBufferSize { get; }

        protected MemoryPoolImpl() { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected abstract void Dispose(bool disposing);
    }

    internal sealed class ArrayMemoryPoolImpl<T> : MemoryPool<T>
    {
        private const int ArrayMaxLength = 0x7FFFFFC7;  // Array.MaxLength

        public sealed override int MaxBufferSize => ArrayMaxLength;

        public sealed override unsafe IMemoryOwner<T> Rent(int minimumBufferSize = -1)
        {
#pragma warning disable CS8500 // sizeof(T) of a reference type is the reference size, as in .NET
            if (minimumBufferSize == -1)
                minimumBufferSize = 1 + (4095 / sizeof(T));
#pragma warning restore CS8500
            else if (((uint)minimumBufferSize) > ArrayMaxLength)
                throw new ArgumentOutOfRangeException("minimumBufferSize");

            return new ArrayMemoryPoolBuffer(minimumBufferSize);
        }

        protected sealed override void Dispose(bool disposing) { }  // a shared pool: nothing to release

        private sealed class ArrayMemoryPoolBuffer : IMemoryOwner<T>
        {
            private T[] _array;

            public ArrayMemoryPoolBuffer(int size)
            {
                _array = ArrayPool<T>.Shared.Rent(size);
            }

            public Memory<T> Memory
            {
                get
                {
                    T[] array = _array;
                    // ObjectDisposedException.ThrowIf(array is null, this): the object name is .NET's type name.
                    if (array == null)
                        throw new ObjectDisposedException("System.Buffers.ArrayMemoryPool`1+ArrayMemoryPoolBuffer[[" + typeof(T).AssemblyQualifiedName + "]]");
                    return new Memory<T>(array);
                }
            }

            public void Dispose()
            {
                T[] array = _array;
                if (array != null)
                {
                    _array = null;
                    ArrayPool<T>.Shared.Return(array);
                }
            }
        }
    }
}
