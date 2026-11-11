// The socket-address contract of System.Net.Primitives, whose types (IPAddress, IPEndPoint, SocketAddress) come from the
// framework's IL (ADR 0023). Upstream's SocketAddressPal and InterfaceInfoPal P/Invoke libSystem.Native (Windows: managed code and
// iphlpapi), which csharpc does not link: these two internal classes are the socket engine's layout of struct sockaddr instead
// (NetNative.Encode/Decode use the same one), with libSystem.Native's semantics. Constants per target OS, never the host's.
#nullable disable
using System;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.SocketAddressPal")]
    internal static unsafe class SocketAddressPalImpl
    {
        // sizeof(sockaddr_in), sizeof(sockaddr_in6), sizeof(sockaddr_un), sizeof(sockaddr_storage).
        public static readonly int IPv4AddressSize = 16;
        public static readonly int IPv6AddressSize = 28;
#if CSHARPC_TARGET_MACOS
        public static readonly int UdsAddressSize = 106;
#else
        public static readonly int UdsAddressSize = 110;
#endif
        public static readonly int MaxAddressSize = 128;

#if CSHARPC_TARGET_WINDOWS
        // Windows: upstream's SocketAddressPal is managed code; AddressFamily's values are Winsock's, any that fits sa_family is kept.
        private static int ToPlatform(AddressFamily family) =>
            (uint)family <= ushort.MaxValue ? (int)family : throw new PlatformNotSupportedException();

        private static AddressFamily ToPal(int family) => (AddressFamily)family;
#else
#if CSHARPC_TARGET_MACOS
        private const int AF_UNIX = 1, AF_INET6 = 30;
#else
        // Linux, and WASI, which has no sockets in preview 1 and takes Linux's layout as NetNative does.
        private const int AF_UNIX = 1, AF_INET6 = 10, AF_PACKET = 17, AF_CAN = 29;
#endif

        // The PAL (AddressFamily) <-> platform conversions of pal_networking.c: what has no platform value is EAFNOSUPPORT,
        // which SocketAddressPal.ThrowOnFailure turns into PlatformNotSupportedException.
        private static int ToPlatform(AddressFamily family)
        {
            switch (family)
            {
                case AddressFamily.Unspecified: return 0;
                case AddressFamily.Unix: return AF_UNIX;
                case AddressFamily.InterNetwork: return 2;
                case AddressFamily.InterNetworkV6: return AF_INET6;
#if !CSHARPC_TARGET_MACOS
                case AddressFamily.Packet: return AF_PACKET;
                case AddressFamily.ControllerAreaNetwork: return AF_CAN;
#endif
                default: throw new PlatformNotSupportedException();
            }
        }

        private static AddressFamily ToPal(int family)
        {
            switch (family)
            {
                case 0: return AddressFamily.Unspecified;
                case AF_UNIX: return AddressFamily.Unix;
                case 2: return AddressFamily.InterNetwork;
                case AF_INET6: return AddressFamily.InterNetworkV6;
#if !CSHARPC_TARGET_MACOS
                case AF_PACKET: return AddressFamily.Packet;
                case AF_CAN: return AddressFamily.ControllerAreaNetwork;
#endif
                default: throw new PlatformNotSupportedException();
            }
        }
#endif

        // libSystem.Native checks every buffer against the size of the field it touches (EFAULT, which ThrowOnFailure
        // reports as ArgumentOutOfRangeException("socketAddressSize")).
        private static void Need(int length, int size)
        {
            if (length < size) throw new ArgumentOutOfRangeException("socketAddressSize");
        }

#if CSHARPC_TARGET_MACOS
        // BSD: sa_len (one byte), then sa_family (one byte).
        public static AddressFamily GetAddressFamily(ReadOnlySpan<byte> buffer)
        {
            Need(buffer.Length, 2);
            return ToPal(buffer[1]);
        }

        public static void SetAddressFamily(Span<byte> buffer, AddressFamily family)
        {
            Need(buffer.Length, 2);
            buffer[1] = (byte)ToPlatform(family);
        }
#else
        public static AddressFamily GetAddressFamily(ReadOnlySpan<byte> buffer)
        {
            Need(buffer.Length, 2);
            return ToPal(MemoryMarshal.Read<ushort>(buffer));
        }

        public static void SetAddressFamily(Span<byte> buffer, AddressFamily family)
        {
            Need(buffer.Length, 2);
            ushort value = (ushort)ToPlatform(family);
            MemoryMarshal.Write(buffer, in value);
        }
#endif

        // sin_port / sin6_port: network byte order at offset 2.
        public static ushort GetPort(ReadOnlySpan<byte> buffer)
        {
            Need(buffer.Length, 4);
            return (ushort)(buffer[2] << 8 | buffer[3]);
        }

        public static void SetPort(Span<byte> buffer, ushort port)
        {
            Need(buffer.Length, 4);
            buffer[2] = (byte)(port >> 8);
            buffer[3] = (byte)port;
        }

        // sin_addr at offset 4, as the in_addr_t in memory (network order read as a native uint).
        public static uint GetIPv4Address(ReadOnlySpan<byte> buffer)
        {
            Need(buffer.Length, IPv4AddressSize);
            return MemoryMarshal.Read<uint>(buffer.Slice(4));
        }

        public static void SetIPv4Address(Span<byte> buffer, uint address)
        {
            Need(buffer.Length, IPv4AddressSize);
            MemoryMarshal.Write(buffer.Slice(4), in address);
        }

        // sockaddr_in6: sin6_flowinfo at 4, sin6_addr at 8, sin6_scope_id (native uint) at 24.
        public static void GetIPv6Address(ReadOnlySpan<byte> buffer, Span<byte> address, out uint scope)
        {
            Need(buffer.Length, IPv6AddressSize);
            if (address.Length < 16) throw new ArgumentOutOfRangeException("socketAddressSize");
            buffer.Slice(8, 16).CopyTo(address);
            scope = MemoryMarshal.Read<uint>(buffer.Slice(24));
        }

        public static void SetIPv6Address(Span<byte> buffer, Span<byte> address, uint scope)
        {
            fixed (byte* a = address) SetIPv6Address(buffer, a, address.Length, scope);
        }

        public static void SetIPv6Address(Span<byte> buffer, byte* address, int addressLength, uint scope)
        {
            Need(buffer.Length, IPv6AddressSize);
            if (addressLength != 16) throw new ArgumentOutOfRangeException("socketAddressSize");
            uint flowInfo = 0;
            MemoryMarshal.Write(buffer.Slice(4), in flowInfo);
            new ReadOnlySpan<byte>(address, 16).CopyTo(buffer.Slice(8));
            MemoryMarshal.Write(buffer.Slice(24), in scope);
        }
    }

    /// <summary>The interface name of a scoped IPv6 address ("fe80::1%eth0"): its index, or 0 when there is no such interface.</summary>
    [Shadows("System.Net.NetworkInformation.InterfaceInfoPal")]
    internal static unsafe class InterfaceInfoPalImpl
    {
#if CSHARPC_TARGET_WINDOWS
        [DllImport("iphlpapi")] private static extern uint if_nametoindex(byte* name);
#elif !CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern uint if_nametoindex(byte* name);
#endif

        public static uint InterfaceNameToIndex<TChar>(ReadOnlySpan<TChar> interfaceName) where TChar : unmanaged, IBinaryInteger<TChar>
        {
#if CSHARPC_TARGET_WASI
            return 0;
#else
            // UTF-8 with a terminating NUL, as upstream passes it; a name with an embedded NUL names a shorter interface there too.
            byte[] name;
            if (typeof(TChar) == typeof(byte))
            {
                var bytes = MemoryMarshal.Cast<TChar, byte>(interfaceName);
                name = new byte[bytes.Length + 1];
                bytes.CopyTo(name);
            }
            else
            {
                var chars = MemoryMarshal.Cast<TChar, char>(interfaceName);
                name = new byte[Encoding.UTF8.GetByteCount(chars) + 1];
                Encoding.UTF8.GetBytes(chars, name);
            }
            fixed (byte* p = name) return if_nametoindex(p);
#endif
        }
    }
}
