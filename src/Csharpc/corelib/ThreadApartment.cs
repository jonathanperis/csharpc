// Thread.GetApartmentState/SetApartmentState/TrySetApartmentState. COM apartments exist only on Windows;
// everywhere else .NET reports Unknown, accepts Unknown and refuses STA and MTA with
// PlatformNotSupportedException (TrySetApartmentState returns false). csharpc has no COM apartments on
// Windows either: asking for one there is an explicit NotSupportedException rather than a thread that
// claims an apartment it doesn't have.
using System;
using System.Threading;

namespace CsharpcCoreLib
{
    internal static class ThreadApartment
    {
        public static ApartmentState Get() => ApartmentState.Unknown;

        public static void Set(ApartmentState state)
        {
            if (!TrySet(state)) throw Refuse();
        }

        public static bool TrySet(ApartmentState state)
        {
            if ((uint)state > (uint)ApartmentState.Unknown)
                throw new ArgumentOutOfRangeException("state", "Enum value was out of legal range.");
            if (state == ApartmentState.Unknown) return true;
#if CSHARPC_TARGET_WINDOWS
            throw Refuse();
#else
            return false;
#endif
        }

        private static Exception Refuse() =>
#if CSHARPC_TARGET_WINDOWS
            new NotSupportedException("COM apartments are not supported by csharpc yet.");
#else
            new PlatformNotSupportedException("COM Interop is not supported on this platform.");
#endif
    }
}
