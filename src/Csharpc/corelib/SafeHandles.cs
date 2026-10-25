// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/
// Runtime/InteropServices/SafeHandle.cs, Buffers/MemoryHandle.cs and Microsoft/Win32/SafeHandles/
// SafeHandle{ZeroOrMinusOne,MinusOne}IsInvalid.cs; see THIRD-PARTY-NOTICES.md.
// SafeHandle's reference counting and its closed/disposed state machine are .NET's: framework IL compiled
// by csharpc (System.IO.Compression's zlib streams) derives from it and its [LibraryImport] marshalling
// adds and releases references around every native call. csharpc runs no finalizers (docs/04-runtime.md):
// a handle that is never disposed is released when the process exits, not when it is collected.
#nullable disable
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.SafeHandle")]
    public abstract class SafeHandleImpl : IDisposable
    {
        protected IntPtr handle;
        private volatile int _state;
        private readonly bool _ownsHandle;

        private const int Closed = 0b01;
        private const int Disposed = 0b10;
        private const int RefCount = unchecked(~0b11); // 2 bits for closed/disposed; the count gets 30 bits
        private const int RefCountOne = 1 << 2;

        protected SafeHandleImpl(IntPtr invalidHandleValue, bool ownsHandle)
        {
            handle = invalidHandleValue;
            _state = RefCountOne; // ref count 1, neither closed nor disposed
            _ownsHandle = ownsHandle;
        }

        internal bool OwnsHandle => _ownsHandle;
        protected internal void SetHandle(IntPtr handle) => this.handle = handle;
        public IntPtr DangerousGetHandle() => handle;
        public bool IsClosed => (_state & Closed) == Closed;
        public abstract bool IsInvalid { get; }
        public void Close() => Dispose();

        public void Dispose()
        {
            Dispose(disposing: true);
        }

        protected virtual void Dispose(bool disposing) => InternalRelease(disposeOrFinalizeOperation: true);

        public void SetHandleAsInvalid()
        {
            int old;
            do old = _state;
            while (Interlocked.CompareExchange(ref _state, old | Closed, old) != old);
        }

        protected abstract bool ReleaseHandle();

        public void DangerousAddRef(ref bool success)
        {
            int oldState, newState;
            do
            {
                oldState = _state;
                if ((oldState & Closed) != 0) throw new ObjectDisposedException(GetType().FullName);
                newState = oldState + RefCountOne;
            } while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);
            success = true;
        }

        internal void DangerousAddRef()
        {
            bool success = false;
            DangerousAddRef(ref success);
        }

        public void DangerousRelease() => InternalRelease(disposeOrFinalizeOperation: false);

        private void InternalRelease(bool disposeOrFinalizeOperation)
        {
            bool performRelease;
            int oldState, newState;
            do
            {
                oldState = _state;
                // Dispose (or finalization) happens once.
                if (disposeOrFinalizeOperation && (oldState & Disposed) != 0) return;
                if ((oldState & RefCount) == 0) throw new ObjectDisposedException(GetType().FullName);
                // The last reference releases the handle (if it is owned and valid).
                performRelease = (oldState & (RefCount | Closed)) == RefCountOne && _ownsHandle && !IsInvalid;
                newState = oldState - RefCountOne;
                if ((oldState & RefCount) == RefCountOne) newState |= Closed;
                if (disposeOrFinalizeOperation) newState |= Disposed;
            } while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);
            if (performRelease)
            {
                // The release must not change the last P/Invoke error the caller may read next.
                int lastError = Marshal.GetLastPInvokeError();
                ReleaseHandle();
                Marshal.SetLastPInvokeError(lastError);
            }
        }
    }

    [Shadows("Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid")]
    public abstract class SafeHandleZeroOrMinusOneIsInvalidImpl : SafeHandleImpl
    {
        protected SafeHandleZeroOrMinusOneIsInvalidImpl(bool ownsHandle) : base(IntPtr.Zero, ownsHandle) { }
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
    }

    [Shadows("Microsoft.Win32.SafeHandles.SafeHandleMinusOneIsInvalid")]
    public abstract class SafeHandleMinusOneIsInvalidImpl : SafeHandleImpl
    {
        protected SafeHandleMinusOneIsInvalidImpl(bool ownsHandle) : base(new IntPtr(-1), ownsHandle) { }
        public override bool IsInvalid => handle == new IntPtr(-1);
    }

#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
    /// <summary>A file descriptor (FileStream.SafeFileHandle, File.GetUnixFileMode(handle)); closed on release when owned.</summary>
    [Shadows("Microsoft.Win32.SafeHandles.SafeFileHandle")]
    public sealed class SafeFileHandleImpl : SafeHandleZeroOrMinusOneIsInvalidImpl
    {
        [DllImport("libc")] private static extern int close(int fd);

        public SafeFileHandleImpl() : base(true) { }

        public SafeFileHandleImpl(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle) => SetHandle(preexistingHandle);

        public override bool IsInvalid
        {
            get
            {
                long h = (long)handle;
                return h < 0 || h > int.MaxValue;
            }
        }

        public bool IsAsync => false;

        protected override bool ReleaseHandle() => close((int)handle) == 0;
    }
#endif

    /// <summary>
    /// The [LibraryImport] marshaller for SafeHandle parameters and results (Marshalling/SafeHandleMarshaller.cs):
    /// a reference is held across the native call, and a returned handle goes into a new T.
    /// </summary>
    [Shadows("System.Runtime.InteropServices.Marshalling.SafeHandleMarshaller`1")]
    public static class SafeHandleMarshallerImpl<T> where T : SafeHandle
    {
        [Shadows("System.Runtime.InteropServices.Marshalling.SafeHandleMarshaller`1+ManagedToUnmanagedIn")]
        public struct ManagedToUnmanagedIn
        {
            private bool _addRefd;
            private T _handle;

            public void FromManaged(T handle)
            {
                _handle = handle;
                handle.DangerousAddRef(ref _addRefd);
            }

            public IntPtr ToUnmanaged() => _handle.DangerousGetHandle();

            public void Free()
            {
                if (_addRefd) _handle.DangerousRelease();
            }
        }

        [Shadows("System.Runtime.InteropServices.Marshalling.SafeHandleMarshaller`1+ManagedToUnmanagedRef")]
        public struct ManagedToUnmanagedRef
        {
            private bool _addRefd;
            private bool _callInvoked;
            private T _handle;
            private IntPtr _originalHandleValue;
            private T _newHandle;
            private T _handleToReturn;

            public ManagedToUnmanagedRef()
            {
                _addRefd = false;
                _callInvoked = false;
                _newHandle = Activator.CreateInstance<T>();
            }

            public void FromManaged(T handle)
            {
                _handle = handle;
                handle.DangerousAddRef(ref _addRefd);
                _originalHandleValue = handle.DangerousGetHandle();
            }

            public IntPtr ToUnmanaged() => _originalHandleValue;

            public void FromUnmanaged(IntPtr value)
            {
                if (value == _originalHandleValue) _handleToReturn = _handle;
                else
                {
                    ((SafeHandleImpl)(object)_newHandle).SetHandle(value);
                    _handleToReturn = _newHandle;
                }
            }

            public void OnInvoked() => _callInvoked = true;
            public T ToManagedFinally() => _handleToReturn;

            public void Free()
            {
                if (_addRefd) _handle.DangerousRelease();
                if (!_callInvoked) _newHandle.Dispose();
            }
        }

        [Shadows("System.Runtime.InteropServices.Marshalling.SafeHandleMarshaller`1+ManagedToUnmanagedOut")]
        public struct ManagedToUnmanagedOut
        {
            private bool _initialized;
            private T _newHandle;

            public ManagedToUnmanagedOut()
            {
                _initialized = false;
                _newHandle = Activator.CreateInstance<T>();
            }

            public void FromUnmanaged(IntPtr value)
            {
                _initialized = true;
                ((SafeHandleImpl)(object)_newHandle).SetHandle(value);
            }

            public T ToManaged() => _newHandle;

            public void Free()
            {
                if (!_initialized) _newHandle.Dispose();
            }
        }
    }

    /// <summary>System.Buffers.MemoryHandle: what Memory&lt;T&gt;.Pin returns (objects never move: the pointer is the data).</summary>
    [Shadows("System.Buffers.MemoryHandle")]
    public unsafe struct MemoryHandleImpl : IDisposable
    {
        private void* _pointer;
        private GCHandle _handle;
        private IPinnable _pinnable;

        public MemoryHandleImpl(void* pointer, GCHandle handle = default, IPinnable pinnable = default)
        {
            _pointer = pointer;
            _handle = handle;
            _pinnable = pinnable;
        }

        public void* Pointer => _pointer;

        public void Dispose()
        {
            if (_handle.IsAllocated) _handle.Free();
            if (_pinnable != null)
            {
                _pinnable.Unpin();
                _pinnable = null;
            }
            _pointer = null;
        }
    }
}
