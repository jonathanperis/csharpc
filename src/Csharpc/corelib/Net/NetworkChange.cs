// System.Net.NetworkInformation.NetworkChange. csharpc has no netlink/route-socket listener: subscribing
// works (HttpClient subscribes to drop its Alt-Svc cache on a network change), but the events are never raised.
// Windows and macOS only: on Linux these types are the framework's IL over the libSystem.Native contract
// (corelib/Net/SystemNative.NetworkInformation.cs, docs/il-first/System.Net.Sockets.md).
#if CSHARPC_TARGET_WINDOWS || CSHARPC_TARGET_MACOS
#nullable disable
using System;
using System.Net.NetworkInformation;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.NetworkInformation.NetworkChange")]
    public class NetworkChangeImpl
    {
        public static bool IsSupported => true;

        public static event NetworkAddressChangedEventHandler NetworkAddressChanged
        {
            add { }
            remove { }
        }

        public static event NetworkAvailabilityChangedEventHandler NetworkAvailabilityChanged
        {
            add { }
            remove { }
        }
    }
}
#endif
