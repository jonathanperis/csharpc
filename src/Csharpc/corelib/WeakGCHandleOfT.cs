// System.Runtime.InteropServices.WeakGCHandle<T> (.NET 10): the typed weak handle that framework code such as the
// OpenSSL SslStream backend (SafeDeleteSslContext's reference to its SslAuthenticationOptions) hands to native
// callbacks as an opaque pointer. A thin wrapper of GCHandle, like GCHandle<T>.
// Shape after dotnet/runtime (release/11.0) src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/WeakGCHandle.T.cs, MIT.
#nullable disable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.WeakGCHandle`1")]
    public struct WeakGCHandleImpl<T> : IEquatable<WeakGCHandle<T>>, IDisposable where T : class
    {
        private IntPtr _handle;

        public WeakGCHandleImpl(T target, bool trackResurrection = false)
        {
            _handle = GCHandle.ToIntPtr(GCHandle.Alloc(target, trackResurrection ? GCHandleType.WeakTrackResurrection : GCHandleType.Weak));
        }

        private WeakGCHandleImpl(IntPtr handle)
        {
            _handle = handle;
        }

        private static GCHandle H(IntPtr handle) => GCHandle.FromIntPtr(handle);

        public bool IsAllocated => _handle != IntPtr.Zero;

        public bool TryGetTarget([NotNullWhen(true)] out T target)
        {
            target = (T)H(_handle).Target;
            return target != null;
        }

        public void SetTarget(T target)
        {
            GCHandle handle = H(_handle);
            handle.Target = target;
        }

        public static WeakGCHandleImpl<T> FromIntPtr(IntPtr value) => new WeakGCHandleImpl<T>(value);
        public static IntPtr ToIntPtr(WeakGCHandleImpl<T> value) => value._handle;

        public void Dispose()
        {
            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                _handle = IntPtr.Zero;
                H(handle).Free();
            }
        }

        public override bool Equals(object obj) => obj is WeakGCHandle<T> other && Equals(other);
        public bool Equals(WeakGCHandle<T> other) => _handle == WeakGCHandle<T>.ToIntPtr(other);
        public override int GetHashCode() => _handle.GetHashCode();
    }
}
