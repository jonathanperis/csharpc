// System.Net.NetworkInformation.NetworkInterface, reduced to what callers need to enumerate local addresses:
// GetAllNetworkInterfaces() and each interface's GetIPProperties().UnicastAddresses (System.Net.Http's
// HttpWindowsProxy lists them for a "<local>" proxy bypass). The interfaces come from the operating system's
// adapter list where csharpc reads it (Windows: GetAdaptersAddresses); elsewhere enumerating them is an explicit
// NotSupportedException, and every other member of these types has no implementation (CSLLVM0002).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.NetworkInformation.NetworkInterface")]
    public abstract class NetworkInterfaceImpl
    {
        public static NetworkInterface[] GetAllNetworkInterfaces()
        {
            var adapters = NetworkInterfaceProvider.Load();
            var result = new NetworkInterface[adapters.Count];
            for (int i = 0; i < result.Length; i++) result[i] = (NetworkInterface)(object)adapters[i];
            return result;
        }

        public abstract IPInterfaceProperties GetIPProperties();
    }

    [Shadows("System.Net.NetworkInformation.IPInterfaceProperties")]
    public abstract class IPInterfacePropertiesImpl
    {
        public abstract UnicastIPAddressInformationCollection UnicastAddresses { get; }
    }

    [Shadows("System.Net.NetworkInformation.IPAddressInformation")]
    public abstract class IPAddressInformationImpl
    {
        public abstract IPAddress Address { get; }
    }

    [Shadows("System.Net.NetworkInformation.UnicastIPAddressInformation")]
    public abstract class UnicastIPAddressInformationImpl : IPAddressInformationImpl
    {
    }

    [Shadows("System.Net.NetworkInformation.UnicastIPAddressInformationCollection")]
    public class UnicastIPAddressInformationCollectionImpl : ICollection<UnicastIPAddressInformation>
    {
        private readonly List<UnicastIPAddressInformation> _addresses = new List<UnicastIPAddressInformation>();

        public virtual int Count => _addresses.Count;
        public virtual bool IsReadOnly => true;

        public virtual UnicastIPAddressInformation this[int index] => _addresses[index];

        internal void AddInternal(UnicastIPAddressInformation address) => _addresses.Add(address);

        public virtual void CopyTo(UnicastIPAddressInformation[] array, int offset) => _addresses.CopyTo(array, offset);
        public virtual void Add(UnicastIPAddressInformation address) => throw new NotSupportedException("This collection is read-only.");
        public virtual void Clear() => throw new NotSupportedException("This collection is read-only.");
        public virtual bool Contains(UnicastIPAddressInformation address) => _addresses.Contains(address);
        public virtual bool Remove(UnicastIPAddressInformation address) => throw new NotSupportedException("This collection is read-only.");
        public virtual IEnumerator<UnicastIPAddressInformation> GetEnumerator() => _addresses.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _addresses.GetEnumerator();
    }

    internal sealed class AdapterInfo : NetworkInterfaceImpl
    {
        private readonly IPInterfaceProperties _properties;

        public AdapterInfo(IPInterfaceProperties properties) { _properties = properties; }

        public override IPInterfaceProperties GetIPProperties() => _properties;
    }

    internal sealed class AdapterProperties : IPInterfacePropertiesImpl
    {
        private readonly UnicastIPAddressInformationCollection _unicast;

        public AdapterProperties(UnicastIPAddressInformationCollection unicast) { _unicast = unicast; }

        public override UnicastIPAddressInformationCollection UnicastAddresses => _unicast;
    }

    internal sealed class AdapterAddress : UnicastIPAddressInformationImpl
    {
        private readonly IPAddress _address;

        public AdapterAddress(IPAddress address) { _address = address; }

        public override IPAddress Address => _address;
    }

    internal static unsafe class NetworkInterfaceProvider
    {
        public static List<AdapterInfo> Load()
        {
#if CSHARPC_TARGET_WINDOWS
            return LoadWindows();
#elif CSHARPC_TARGET_LINUX || CSHARPC_TARGET_MACOS
            return LoadUnix();
#else
            throw new NotSupportedException("Enumerating network interfaces (NetworkInterface.GetAllNetworkInterfaces) is not supported by csharpc yet on this platform.");
#endif
        }

#if CSHARPC_TARGET_LINUX || CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern int getifaddrs(byte** list);
        [DllImport("libc")] private static extern void freeifaddrs(byte* list);

        // getifaddrs: one `struct ifaddrs` per address (next at 0, name at 8, flags at 16, address at 24 on both
        // systems); an interface's addresses are grouped by name, in the order they come. A sockaddr is a
        // family (Linux: 16 bits at 0, macOS: 8 bits at 1 after the length byte), then the same sockaddr_in
        // (address at 4) and sockaddr_in6 (address at 8, scope id at 24) layouts.
        private static List<AdapterInfo> LoadUnix()
        {
#if CSHARPC_TARGET_MACOS
            const int AF_INET6 = 30;
#else
            const int AF_INET6 = 10;
#endif
            byte* list;
            if (getifaddrs(&list) != 0) throw new InvalidOperationException("getifaddrs failed (errno " + Marshal.GetLastSystemError() + ").");
            try
            {
                var names = new List<string>();
                var collections = new List<UnicastIPAddressInformationCollectionImpl>();
                for (byte* entry = list; entry != null; entry = *(byte**)entry)
                {
                    string name = Marshal.PtrToStringUTF8((IntPtr)(*(byte**)(entry + 8)));
                    int index = names.IndexOf(name);
                    if (index < 0)
                    {
                        names.Add(name);
                        collections.Add(new UnicastIPAddressInformationCollectionImpl());
                        index = names.Count - 1;
                    }
                    byte* sockaddr = *(byte**)(entry + 24);
                    if (sockaddr == null) continue;
#if CSHARPC_TARGET_MACOS
                    int family = sockaddr[1];
#else
                    int family = *(ushort*)sockaddr;
#endif
                    IPAddress address = null;
                    if (family == 2) address = new IPAddress(new byte[] { sockaddr[4], sockaddr[5], sockaddr[6], sockaddr[7] });
                    else if (family == AF_INET6)
                    {
                        var bytes = new byte[16];
                        for (int i = 0; i < 16; i++) bytes[i] = sockaddr[8 + i];
                        address = new IPAddress(bytes, *(uint*)(sockaddr + 24));
                    }
                    if (address != null) collections[index].AddInternal((UnicastIPAddressInformation)(object)new AdapterAddress(address));
                }
                var adapters = new List<AdapterInfo>();
                foreach (var unicast in collections)
                    adapters.Add(new AdapterInfo((IPInterfaceProperties)(object)new AdapterProperties((UnicastIPAddressInformationCollection)(object)unicast)));
                return adapters;
            }
            finally
            {
                freeifaddrs(list);
            }
        }
#endif

#if CSHARPC_TARGET_WINDOWS
        [DllImport("iphlpapi")]
        private static extern uint GetAdaptersAddresses(uint family, uint flags, void* reserved, byte* addresses, uint* size);

        // GetAdaptersAddresses without anycast, multicast and DNS-server lists: the IP_ADAPTER_ADDRESSES chain
        // (64-bit layout: Next at 8, FirstUnicastAddress at 24) and each IP_ADAPTER_UNICAST_ADDRESS (Next at 8,
        // the SOCKADDR pointer at 16). All the information csharpc exposes is the unicast addresses.
        private static List<AdapterInfo> LoadWindows()
        {
            const uint AF_UNSPEC = 0, GAA_FLAG_SKIP_ANYCAST = 2, GAA_FLAG_SKIP_MULTICAST = 4, GAA_FLAG_SKIP_DNS_SERVER = 8;
            const uint ERROR_SUCCESS = 0, ERROR_BUFFER_OVERFLOW = 111;
            const uint flags = GAA_FLAG_SKIP_ANYCAST | GAA_FLAG_SKIP_MULTICAST | GAA_FLAG_SKIP_DNS_SERVER;
            uint size = 15 * 1024;
            byte* buffer = null;
            try
            {
                uint error;
                int attempts = 0;
                do
                {
                    if (buffer != null) Libc.free(buffer);
                    buffer = (byte*)Libc.Alloc(size);
                    error = GetAdaptersAddresses(AF_UNSPEC, flags, null, buffer, &size);
                } while (error == ERROR_BUFFER_OVERFLOW && ++attempts < 5);
                if (error != ERROR_SUCCESS)
                    throw new InvalidOperationException("GetAdaptersAddresses failed with error " + error + ".");

                var adapters = new List<AdapterInfo>();
                for (byte* adapter = buffer; adapter != null; adapter = *(byte**)(adapter + 8))
                {
                    var unicast = new UnicastIPAddressInformationCollectionImpl();
                    for (byte* entry = *(byte**)(adapter + 24); entry != null; entry = *(byte**)(entry + 8))
                    {
                        IPAddress address = ReadAddress(*(byte**)(entry + 16));
                        if (address != null) unicast.AddInternal((UnicastIPAddressInformation)(object)new AdapterAddress(address));
                    }
                    adapters.Add(new AdapterInfo((IPInterfaceProperties)(object)new AdapterProperties((UnicastIPAddressInformationCollection)(object)unicast)));
                }
                return adapters;
            }
            finally
            {
                if (buffer != null) Libc.free(buffer);
            }
        }

        // A SOCKADDR_IN (family 2: address at 4) or SOCKADDR_IN6 (family 23: address at 8, scope id at 24).
        private static IPAddress ReadAddress(byte* sockaddr)
        {
            if (sockaddr == null) return null;
            int family = *(ushort*)sockaddr;
            if (family == 2) return new IPAddress(new byte[] { sockaddr[4], sockaddr[5], sockaddr[6], sockaddr[7] });
            if (family == 23)
            {
                var bytes = new byte[16];
                for (int i = 0; i < 16; i++) bytes[i] = sockaddr[8 + i];
                return new IPAddress(bytes, *(uint*)(sockaddr + 24));
            }
            return null;
        }
#else
        private static List<AdapterInfo> LoadWindows() => throw new NotSupportedException();
#endif
    }
}
