// System.Runtime.InteropServices.GCHandle<T> (.NET 9): the typed strong handle that framework code such as the
// Apple SslStream backend (SafeDeleteSslContext) passes to native callbacks as an opaque connection pointer.
// A thin wrapper of GCHandle, like PinnedGCHandle<T>.
// Shape after dotnet/runtime (release/11.0) src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandleOfT.cs, MIT.
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.GCHandle`1")]
    public struct GCHandleOfTImpl<T> : IDisposable, IEquatable<GCHandle<T>> where T : class
    {
        private IntPtr _handle;

        public GCHandleOfTImpl(T target)
        {
            _handle = GCHandle.ToIntPtr(GCHandle.Alloc(target, GCHandleType.Normal));
        }

        private GCHandleOfTImpl(IntPtr handle)
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

        public void Dispose()
        {
            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                _handle = IntPtr.Zero;
                H(handle).Free();
            }
        }

        public static GCHandleOfTImpl<T> FromIntPtr(IntPtr value) => new GCHandleOfTImpl<T>(value);
        public static IntPtr ToIntPtr(GCHandleOfTImpl<T> value) => value._handle;

        public override bool Equals(object obj) => obj is GCHandle<T> other && Equals(other);
        public bool Equals(GCHandle<T> other) => _handle == GCHandle<T>.ToIntPtr(other);
        public override int GetHashCode() => _handle.GetHashCode();
    }
}
