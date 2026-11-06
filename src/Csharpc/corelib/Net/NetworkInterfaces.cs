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
#else
            throw new NotSupportedException("Enumerating network interfaces (NetworkInterface.GetAllNetworkInterfaces) is not supported by csharpc yet on this platform.");
#endif
        }

        private static List<AdapterInfo> LoadWindows()
        {
            throw new NotSupportedException("Enumerating network interfaces (NetworkInterface.GetAllNetworkInterfaces) is not supported by csharpc yet.");
        }
    }
}
