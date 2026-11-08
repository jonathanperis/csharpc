// System.Runtime.InteropServices.PinnedGCHandle<T> (.NET 9): the typed pinned handle that framework code
// such as the CNG interop uses to hand a managed buffer to native code. A thin wrapper of GCHandle: objects
// never move (ADR 0004), so pinning only keeps the target alive and allows taking its address.
// Shape after dotnet/runtime (release/11.0) src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/PinnedGCHandle.cs, MIT.
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.PinnedGCHandle`1")]
    public unsafe struct PinnedGCHandleImpl<T> : IDisposable, IEquatable<PinnedGCHandle<T>> where T : class
    {
        private IntPtr _handle;

        public PinnedGCHandleImpl(T target)
        {
            _handle = GCHandle.ToIntPtr(GCHandle.Alloc(target, GCHandleType.Pinned));
        }

        private PinnedGCHandleImpl(IntPtr handle)
        {
            _handle = handle;
        }

        private static GCHandle H(IntPtr handle) => GCHandle.FromIntPtr(handle);

        public T Target
        {
            get => (T)H(_handle).Target;
            set { GCHandle handle = H(_handle); handle.Target = value; }
        }

        public bool IsAllocated => _handle != IntPtr.Zero;

        /// <summary>The address of the object's data, as .NET defines it: just past the method table (an array's length field, a class's first field).</summary>
        public void* GetAddressOfObjectData() => CoreIntrinsics.ObjectAddress(H(_handle).Target) + sizeof(IntPtr);

        public void Dispose()
        {
            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                _handle = IntPtr.Zero;
                H(handle).Free();
            }
        }

        public static PinnedGCHandleImpl<T> FromIntPtr(IntPtr value) => new PinnedGCHandleImpl<T>(value);
        public static IntPtr ToIntPtr(PinnedGCHandleImpl<T> value) => value._handle;

        public override bool Equals(object obj) => obj is PinnedGCHandle<T> other && Equals(other);
        public bool Equals(PinnedGCHandle<T> other) => _handle == PinnedGCHandle<T>.ToIntPtr(other);
        public override int GetHashCode() => _handle.GetHashCode();
    }
}
