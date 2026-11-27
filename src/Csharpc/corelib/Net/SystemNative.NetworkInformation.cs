// libSystem.Native's network-information C API, the part the framework's System.Net.NetworkInformation IL P/Invokes on Linux
// (getifaddrs-based interface lists, the netlink change listener, the TCP state map), served by csharpc over its own libc layer, as
// SystemNative.Net.cs serves the sockets. The semantics are pal_interfaceaddresses.c's, pal_networkchange.c's, pal_maphardwaretype.c's
// and pal_tcpstate.c's (dotnet/runtime src/native/libs/System.Native, release/11.0). macOS keeps the corelib's NetworkInterface and
// NetworkChange (its framework IL also reads sysctl statistics this contract does not provide yet); Windows keeps them too.
// Security (docs/il-first/System.Net.Sockets.md): the helper and netlink sockets are close-on-exec (pal_*.c creates them without
// SOCK_CLOEXEC; stronger here, not observable); interface names are copied up to their NUL within the 16-byte field (pal_interfaceaddresses.c
// copies 16 bytes whatever the name's length); netmasks are read from a copy (mask2prefix shifts the bytes in place) and a missing one
// counts as a full prefix; every netlink message is bounded by the length recvmsg returned and the 4 KiB buffer.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class SystemNative
    {
        [DllImport("libc")] private static extern byte* if_indextoname(uint index, byte* name);

        private const int IFF_RUNNING = 0x40, IFF_ALLMULTI = 0x200, IFF_MULTICAST = 0x1000, IF_NAMESIZE = 16;
        private const nuint SIOCGIFMTU = 0x8921, SIOCETHTOOL = 0x8946;
        private const int Error_ECONNABORTED = 0x1000D;

        // System.Net.NetworkInformation.NetworkInterfaceType.
        private const ushort NitUnknown = 1, NitEthernet = 6, NitTokenRing = 9, NitFddi = 15, NitPpp = 23, NitLoopback = 24, NitSlip = 28,
            NitAtm = 37, NitWireless80211 = 71, NitTunnel = 131;

        private static ushort MapHardwareType(ushort arphrd) => arphrd switch
        {
            1 or 2 => NitEthernet,                  // ARPHRD_ETHER, ARPHRD_EETHER
            4 => NitTokenRing,                      // ARPHRD_PRONET
            19 => NitAtm,                           // ARPHRD_ATM
            256 or 257 or 258 or 259 => NitSlip,    // ARPHRD_SLIP, CSLIP, SLIP6, CSLIP6
            512 => NitPpp,                          // ARPHRD_PPP
            768 or 769 => NitTunnel,                // ARPHRD_TUNNEL, ARPHRD_TUNNEL6
            772 => NitLoopback,                     // ARPHRD_LOOPBACK
            774 => NitFddi,                         // ARPHRD_FDDI
            801 or 802 or 803 => NitWireless80211,  // ARPHRD_IEEE80211, _PRISM, _RADIOTAP
            _ => NitUnknown,
        };

        /// <summary>mask2prefix over a copy of the netmask bytes: whole 0xff bytes, then the bits of the next byte shifted out until it is zero.</summary>
        private static byte MaskToPrefix(byte* mask, int length)
        {
            if (mask == null) return (byte)(length * 8);
            byte* copy = stackalloc byte[16];
            Buffer.MemoryCopy(mask, copy, 16, length);
            byte len = 0;
            int i = 0;
            while (i < length && copy[i] == 0xff) { len += 8; i++; }
            if (i < length)
                while (copy[i] != 0) { len++; copy[i] = (byte)(copy[i] << 1); }
            if (len == 0 && length == 4) len = 32;
            return len;
        }

        private static byte* IfaName(void* ifa) => *(byte**)((byte*)ifa + 8);
        private static byte* IfaNetmask(void* ifa) => *(byte**)((byte*)ifa + 32);

        private static byte NetmaskPrefix(void* ifa, int family)
        {
            byte* netmask = IfaNetmask(ifa);
            return family == AF_INET ? MaskToPrefix(netmask == null ? null : netmask + 4, 4) : MaskToPrefix(netmask == null ? null : netmask + 8, 16);
        }

        // IpAddressInfo: InterfaceIndex, AddressBytes[16], NumAddressBytes, PrefixLength, padding (24 bytes).
        // LinkLayerAddressInfo: InterfaceIndex, AddressBytes[12], NumAddressBytes, padding, HardwareType (20 bytes).
        // struct sockaddr_ll: sll_hatype at 10, sll_halen at 13, sll_addr[8] at 14.

        /// <summary>SystemNative_EnumerateInterfaceAddresses: each getifaddrs entry reported through the framework's callbacks with its interface's name and index.</summary>
        [RuntimeExport("SystemNative_EnumerateInterfaceAddresses", Trigger + "SystemNative_EnumerateInterfaceAddresses")]
        public static int EnumerateInterfaceAddresses(void* context, delegate* unmanaged<void*, byte*, byte*, void> onIpv4Found,
            delegate* unmanaged<void*, byte*, byte*, uint*, void> onIpv6Found, delegate* unmanaged<void*, byte*, byte*, void> onLinkLayerFound)
        {
            void* head;
            if (getifaddrs(&head) == -1) return -1;
            byte* actualName = stackalloc byte[IF_NAMESIZE];
            byte* info = stackalloc byte[24];
            for (void* current = head; current != null; current = IfaNext(current))
            {
                byte* name = IfaName(current);
                byte* address = IfaAddr(current);
                if (address == null || name == null) continue;
                uint index = if_nametoindex(name);
                if (index == 0)
                {
                    int saved = Errno;
                    if (saved == ENODEV) continue;
                    freeifaddrs(head);
                    return Fail(saved);
                }
                if (if_indextoname(index, actualName) == null)
                {
                    int saved = Errno;
                    freeifaddrs(head);
                    return Fail(saved);
                }
                int family = ReadFamily(address);
                new Span<byte>(info, 24).Clear();
                if (family == AF_INET)
                {
                    if (onIpv4Found == null) continue;
                    *(uint*)info = index;
                    *(uint*)(info + 4) = *(uint*)(address + 4);
                    info[20] = 4;
                    info[21] = NetmaskPrefix(current, AF_INET);
                    onIpv4Found(context, actualName, info);
                }
                else if (family == AF_INET6)
                {
                    if (onIpv6Found == null) continue;
                    *(uint*)info = index;
                    Buffer.MemoryCopy(address + 8, info + 4, 16, 16);
                    info[20] = 16;
                    info[21] = NetmaskPrefix(current, AF_INET6);
                    uint scopeId = *(uint*)(address + 24);
                    onIpv6Found(context, actualName, info, &scopeId);
                }
                else if (family == AF_PACKET)
                {
                    if (onLinkLayerFound == null) continue;
                    byte halen = Math.Min(address[13], (byte)8);
                    *(uint*)info = index;
                    Buffer.MemoryCopy(address + 14, info + 4, 12, halen);
                    info[16] = halen;
                    *(ushort*)(info + 18) = MapHardwareType(*(ushort*)(address + 10));
                    onLinkLayerFound(context, name, info);
                }
            }
            freeifaddrs(head);
            return 0;
        }

        // NetworkInterfaceInfo: Name[16], Speed (8), InterfaceIndex, Mtu, HardwareType (2), OperationalState, NumAddressBytes,
        // AddressBytes[12], SupportsMulticast, padding: 56 bytes.
        private const int NiiSize = 56;

        /// <summary>
        /// SystemNative_GetNetworkInterfaces: one calloc'ed block of (entries + IPv4 + IPv6 addresses) NetworkInterfaceInfo slots, the
        /// interfaces first (at most one per getifaddrs entry), then the IpAddressInfo list (one per address, counted first); the MTU from
        /// SIOCGIFMTU and the link state and speed from ethtool on a helper datagram socket.
        /// </summary>
        [RuntimeExport("SystemNative_GetNetworkInterfaces", Trigger + "SystemNative_GetNetworkInterfaces")]
        public static int GetNetworkInterfaces(int* interfaceCount, byte** interfaceList, int* addressCount, byte** addressList)
        {
            if (interfaceCount == null || interfaceList == null || addressCount == null || addressList == null) return Fail(EINVAL);
            void* head;
            if (getifaddrs(&head) == -1) return -1;
            int count = 0, ip4count = 0, ip6count = 0;
            for (void* e = head; e != null; e = IfaNext(e))
            {
                count++;
                byte* address = IfaAddr(e);
                if (address == null) continue;
                int family = ReadFamily(address);
                if (family == AF_INET) ip4count++;
                else if (family == AF_INET6) ip6count++;
            }
            int entries = count + ip4count + ip6count;
            byte* block = (byte*)calloc((nuint)Math.Max(entries, 1), NiiSize);
            if (block == null)
            {
                freeifaddrs(head);
                return Fail(ENOMEM_PLATFORM);
            }
            byte* addresses = block + count * NiiSize;
            byte* ai = addresses;
            int ifcount = 0, socketFd = -1;
            byte* ifr = stackalloc byte[40];
            uint* ecmd = stackalloc uint[11];
            for (void* e = head; e != null; e = IfaNext(e))
            {
                byte* name = IfaName(e);
                if (name == null) continue;
                uint index = if_nametoindex(name);
                if (index == 0)
                {
                    int saved = Errno;
                    if (saved == ENODEV) continue;
                    freeifaddrs(head);
                    free(block);
                    if (socketFd != -1) close(socketFd);
                    return Fail(saved);
                }
                byte* nii = null;
                for (int i = 0; i < ifcount; i++)
                    if (*(uint*)(block + i * NiiSize + 24) == index) { nii = block + i * NiiSize; break; }
                uint flags = IfaFlags(e);
                if (nii == null)
                {
                    nii = block + ifcount++ * NiiSize;
                    for (int i = 0; i < 16 && name[i] != 0; i++) nii[i] = name[i];
                    *(uint*)(nii + 24) = index;
                    *(long*)(nii + 16) = -1;
                    *(ushort*)(nii + 32) = (flags & IFF_LOOPBACK) == IFF_LOOPBACK ? NitLoopback : NitUnknown;
                    if ((flags & (IFF_MULTICAST | IFF_ALLMULTI)) != 0) nii[48] = 1;
                    nii[34] = (byte)((flags & (IFF_UP | IFF_RUNNING)) == (IFF_UP | IFF_RUNNING) ? 1 /* Up */ : 2 /* Down */);
                }
                byte* address = IfaAddr(e);
                if (address == null) continue;
                int family = ReadFamily(address);
                if (family == AF_INET)
                {
                    *(uint*)ai = index;
                    *(uint*)(ai + 4) = *(uint*)(address + 4);
                    ai[20] = 4;
                    ai[21] = NetmaskPrefix(e, AF_INET);
                    ai += 24;
                }
                else if (family == AF_INET6)
                {
                    *(uint*)ai = index;
                    Buffer.MemoryCopy(address + 8, ai + 4, 16, 16);
                    ai[20] = 16;
                    ai[21] = NetmaskPrefix(e, AF_INET6);
                    ai += 24;
                }
                else if (family == AF_PACKET)
                {
                    byte halen = Math.Min(address[13], (byte)8);
                    ushort hardwareType = MapHardwareType(*(ushort*)(address + 10));
                    *(ushort*)(nii + 32) = hardwareType;
                    nii[35] = halen;
                    Buffer.MemoryCopy(address + 14, nii + 36, 12, halen);
                    new Span<byte>(ifr, 40).Clear();
                    for (int i = 0; i < IF_NAMESIZE - 1 && nii[i] != 0; i++) ifr[i] = nii[i];
                    if (socketFd == -1) socketFd = socket_(AF_INET, SOCK_DGRAM | SOCK_CLOEXEC, IPPROTO_IP);
                    if (socketFd > -1)
                    {
                        if (ioctl_ptr(socketFd, SIOCGIFMTU, ifr) == 0) *(int*)(nii + 28) = *(int*)(ifr + 16);
                        if (hardwareType != NitUnknown && hardwareType != NitTunnel && hardwareType != NitLoopback)
                        {
                            // struct ethtool_cmd (44 bytes): ETHTOOL_GLINK answers in its second word, ETHTOOL_GSET fills speed (u16 at 12)
                            // and speed_hi (u16 at 28).
                            new Span<uint>(ecmd, 11).Clear();
                            ecmd[0] = 0xa; // ETHTOOL_GLINK
                            *(uint**)(ifr + 16) = ecmd;
                            if (ioctl_ptr(socketFd, SIOCETHTOOL, ifr) == 0)
                            {
                                if (ecmd[1] == 0) nii[34] = 2; // Down
                                ecmd[0] = 0x1; // ETHTOOL_GSET
                                if (ioctl_ptr(socketFd, SIOCETHTOOL, ifr) == 0)
                                {
                                    long speed = (uint)(*(ushort*)((byte*)ecmd + 28) << 16 | *(ushort*)((byte*)ecmd + 12));
                                    if (speed > 0) speed *= 1000000;
                                    *(long*)(nii + 16) = speed;
                                }
                            }
                        }
                    }
                }
            }
            *interfaceCount = ifcount;
            *addressCount = ip4count + ip6count;
            *interfaceList = block;
            *addressList = addresses;
            freeifaddrs(head);
            if (socketFd != -1) close(socketFd);
            return 0;
        }

        private const int ENOMEM_PLATFORM = 12;

        [RuntimeExport("SystemNative_MapTcpState", Trigger + "SystemNative_MapTcpState")]
        public static int MapTcpState(int tcpState) => tcpState switch
        {
            // glibc's netinet/tcp.h enum, mapped to System.Net.NetworkInformation.TcpState as pal_tcpstate.c (TCP_CLOSE is Closing there).
            1 => 5, 2 => 3, 3 => 4, 4 => 6, 5 => 7, 6 => 11, 7 => 9, 8 => 8, 9 => 10, 10 => 2, 11 => 9,
            _ => 0,
        };

        /// <summary>SystemNative_CreateNetworkChangeListenerSocket: a NETLINK_ROUTE socket bound to the link, IPv4 address and IPv4/IPv6 route groups.</summary>
        [RuntimeExport("SystemNative_CreateNetworkChangeListenerSocket", Trigger + "SystemNative_CreateNetworkChangeListenerSocket")]
        public static int CreateNetworkChangeListenerSocket(nint* retSocket)
        {
            if (retSocket == null) return Error_EFAULT;
            const int AF_NETLINK = 16, NETLINK_ROUTE = 0;
            const uint Groups = 0x1 /* RTMGRP_LINK */ | 0x10 /* RTMGRP_IPV4_IFADDR */ | 0x40 /* RTMGRP_IPV4_ROUTE */ | 0x400 /* RTMGRP_IPV6_ROUTE */;
            int sock = socket_(AF_NETLINK, SOCK_RAW | SOCK_CLOEXEC, NETLINK_ROUTE);
            if (sock == -1) { *retSocket = -1; return PalErrno(); }
            byte* sa = stackalloc byte[12];
            new Span<byte>(sa, 12).Clear();
            *(ushort*)sa = AF_NETLINK;
            *(uint*)(sa + 8) = Groups;
            if (bind(sock, sa, 12) != 0)
            {
                int error = PalErrno();
                close(sock);
                *retSocket = -1;
                return error;
            }
            *retSocket = sock;
            return Error_SUCCESS;
        }

        /// <summary>SystemNative_ReadEvents: one recvmsg of at most 4 KiB from the netlink socket, each message within it reported to the framework.</summary>
        [RuntimeExport("SystemNative_ReadEvents", Trigger + "SystemNative_ReadEvents")]
        public static int ReadEvents(nint sock, delegate* unmanaged<nint, int, void> onNetworkChange)
        {
            const int BufferSize = 4096, NlmsgHeader = 16;
            const int NLMSG_ERROR = 2, NLMSG_DONE = 3, RTM_NEWLINK = 16, RTM_NEWADDR = 20, RTM_DELADDR = 21, RTM_NEWROUTE = 24, RTM_DELROUTE = 25;
            const int RT_TABLE_MAIN = 254;
            byte* buffer = stackalloc byte[BufferSize];
            byte* sanl = stackalloc byte[12];
            nint* iov = stackalloc nint[2];
            iov[0] = (nint)buffer;
            iov[1] = BufferSize;
            byte* msg = stackalloc byte[MsghdrSize];
            new Span<byte>(msg, MsghdrSize).Clear();
            *(byte**)msg = sanl;
            *(uint*)(msg + 8) = 12;
            *(nint**)(msg + 16) = iov;
            *(nuint*)(msg + MsgIovLenOffset) = 1;
            nint len;
            while ((len = recvmsg(Fd(sock), msg, 0)) < 0 && Errno == EINTR) { }
            if (len == 0) return Error_ECONNABORTED;
            if (len == -1) return PalErrno();
            long remaining = len;
            for (byte* hdr = buffer; remaining >= NlmsgHeader && *(uint*)hdr >= NlmsgHeader && *(uint*)hdr <= remaining;)
            {
                switch (*(ushort*)(hdr + 4))
                {
                    case NLMSG_DONE:
                    case NLMSG_ERROR:
                        return Error_SUCCESS;
                    case RTM_NEWADDR:
                        onNetworkChange(sock, 0 /* AddressAdded */);
                        break;
                    case RTM_DELADDR:
                        onNetworkChange(sock, 1 /* AddressRemoved */);
                        break;
                    case RTM_NEWLINK:
                        // struct ifinfomsg: ifi_family first; only AF_UNSPEC is an availability change.
                        onNetworkChange(sock, *(uint*)hdr > NlmsgHeader && hdr[NlmsgHeader] == AF_UNSPEC ? 2 /* AvailabilityChanged */ : -1 /* None */);
                        break;
                    case RTM_NEWROUTE:
                    case RTM_DELROUTE:
                        // struct rtmsg: rtm_table is its fifth byte.
                        if (*(uint*)hdr > NlmsgHeader + 4 && hdr[NlmsgHeader + 4] == RT_TABLE_MAIN)
                        {
                            onNetworkChange(sock, 2 /* AvailabilityChanged */);
                            return Error_SUCCESS;
                        }
                        break;
                }
                uint aligned = (*(uint*)hdr + 3) & ~3u;
                remaining -= aligned;
                hdr += aligned;
            }
            return Error_SUCCESS;
        }
    }
}
#endif
