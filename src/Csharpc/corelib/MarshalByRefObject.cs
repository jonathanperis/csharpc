// System.MarshalByRefObject: the base of Stream, TextReader, TextWriter and System.ComponentModel's
// Component. Remoting doesn't exist in .NET (Core); its lifetime members throw as there, and stream
// classes compiled from IL (Kestrel's) override InitializeLifetimeService.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.MarshalByRefObject")]
    public abstract class MarshalByRefObjectImpl
    {
        protected MarshalByRefObjectImpl() { }

        [Obsolete("This Remoting API is not supported and throws PlatformNotSupportedException.")]
        public object GetLifetimeService() => throw new PlatformNotSupportedException("Remoting is not supported on this platform.");

        [Obsolete("This Remoting API is not supported and throws PlatformNotSupportedException.")]
        public virtual object InitializeLifetimeService() => throw new PlatformNotSupportedException("Remoting is not supported on this platform.");

        protected MarshalByRefObject MemberwiseClone(bool cloneIdentity) => (MarshalByRefObject)MemberwiseClone();
    }
}
