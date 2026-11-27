// libSystem.Native's networking C API, the part the framework's System.Net.Sockets, System.Net.NameResolution and System.Net.Primitives
// IL P/Invokes on Linux and macOS, served by csharpc over its own libc layer (the OS boundary stays csharpc's: ADR 0024; linking the
// pack's shim stays an owner decision, docs/21-ownership.md). The framework's managed socket engine (SocketAsyncEngine, SocketAsyncContext)
// runs unchanged above it: this file is csharpc's socket engine below that line, epoll on Linux and kqueue on macOS. Each function is
// exported under libSystem.Native's symbol name ([RuntimeExport]; ModuleCodeGen.PInvoke binds the framework's P/Invokes to it).
//
// The semantics are pal_networking.c's, pal_networking_common.h's and pal_io_common.h's (dotnet/runtime src/native/libs, release/11.0),
// function by function: the PAL enums (address family, socket type, protocol, option level and name, flags, events) are converted to the
// target's values, never the host's; results are the PAL error numbers (Interop.Error) the framework reads; EINTR is retried exactly
// where pal_networking.c retries it. Security (docs/il-first/System.Net.Sockets.md, "Security review"): every descriptor is created
// close-on-exec (SOCK_CLOEXEC, accept4(SOCK_CLOEXEC), epoll_create1(EPOLL_CLOEXEC); FD_CLOEXEC by fcntl on macOS, as there); every write
// goes to a buffer whose size the caller passed and is checked first (socket addresses against socketAddressLen, option values against
// optionLen, messages against their IOVectorCount and ControlBufferLen, event buffers against count); host names are the framework's
// NUL-terminated UTF-8. WASI (preview 1) has no sockets: only the socket-address accessors, which are plain memory, exist there, and
// the calls into the kernel throw the framework's PlatformNotSupportedException.
#if !CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class SystemNative
    {
        // ---- PAL numbering (pal_networking.h, pal_error_common.h) ----------------------------------------------------
        private const int Error_SUCCESS = 0, Error_EAFNOSUPPORT = 0x10005, Error_EFAULT = 0x10015, Error_EINVAL = 0x1001C,
            Error_ENOMEM = 0x10031, Error_ENOPROTOOPT = 0x10033, Error_EPROTONOSUPPORT = 0x10045, Error_EPROTOTYPE = 0x10046,
            Error_ENOTSUP = 0x1003D;
        private const int PalAF_UNKNOWN = -1, PalAF_UNSPEC = 0, PalAF_UNIX = 1, PalAF_INET = 2, PalAF_INET6 = 23, PalAF_LINK = 13,
            PalAF_PACKET = 65536, PalAF_CAN = 65537;
        private const int PalSock_UNKNOWN = -1;
        private const int PalPT_UNKNOWN = -1, PalPT_UNSPECIFIED = 0, PalPT_ICMP = 1, PalPT_TCP = 6, PalPT_UDP = 17, PalPT_ICMPV6 = 58,
            PalPT_RAW = 255, PalPT_IGMP = 2, PalPT_NONE = 59, PalPT_DSTOPTS = 60, PalPT_ROUTING = 43, PalPT_FRAGMENT = 44;
        private const int PalSOL_SOCKET = 0xffff, PalSOL_IP = 0, PalSOL_IPV6 = 41, PalSOL_TCP = 6, PalSOL_UDP = 17;
        private const int PalSO_REUSEADDR = 0x0004, PalSO_EXCLUSIVEADDRUSE = ~0x0004, PalSO_ACCEPTCONN = 0x0002, PalSO_TYPE = 0x1008,
            PalSO_IP_DONTFRAGMENT = 14, PalSO_IP_MULTICAST_IF = 9;
        private const int PalMSG_OOB = 0x1, PalMSG_PEEK = 0x2, PalMSG_DONTROUTE = 0x4, PalMSG_TRUNC = 0x100, PalMSG_CTRUNC = 0x200,
            PalMSG_DONTWAIT = 0x1000, PalMSG_ERRQUEUE = 0x2000;
        private const int SA_READ = 0x01, SA_WRITE = 0x02, SA_READCLOSE = 0x04, SA_CLOSE = 0x08, SA_ERROR = 0x10;
        private const int PAL_POLLIN = 0x1, PAL_POLLPRI = 0x2, PAL_POLLOUT = 0x4, PAL_POLLERR = 0x8, PAL_POLLHUP = 0x10, PAL_POLLNVAL = 0x20;
        private const int EAI_PAL_AGAIN = 1, EAI_PAL_BADFLAGS = 2, EAI_PAL_FAIL = 3, EAI_PAL_FAMILY = 4, EAI_PAL_NONAME = 5, EAI_PAL_BADARG = 6,
            EAI_PAL_MEMORY = 8, EAI_PAL_SYSTEM = 9;

        // ---- target layouts and constants ------------------------------------------------------------------------------
#if CSHARPC_TARGET_MACOS
        // BSD sockaddr: sa_len (one byte), then sa_family (one byte).
        private const int SaFamilyOffset = 1, SaFamilySize = 1;
        private const int AF_UNIX = 1, AF_INET = 2, AF_INET6 = 30, AF_LINK = 18, AF_PACKET = -1, AF_CAN = -1;
        private const int SockaddrUnSize = 106;
#else
        // Linux (and WASI, which has no sockets and takes Linux's layout for the address accessors, as before the flip).
        private const int SaFamilyOffset = 0, SaFamilySize = 2;
        private const int AF_UNIX = 1, AF_INET = 2, AF_INET6 = 10, AF_LINK = -1, AF_PACKET = 17, AF_CAN = 29;
        private const int SockaddrUnSize = 110;
#endif
        private const int AF_UNSPEC = 0;
        private const int SockaddrInSize = 16, SockaddrIn6Size = 28, SockaddrStorageSize = 128, SunPathOffset = 2;

        private static bool InBounds(int length, int offset, int size) => length >= 0 && offset + size <= length;

        private static int ReadFamily(byte* address) => SaFamilySize == 1 ? address[SaFamilyOffset] : *(ushort*)(address + SaFamilyOffset);

        private static void WriteFamily(byte* address, int family)
        {
            if (SaFamilySize == 1) address[SaFamilyOffset] = (byte)family;
            else *(ushort*)(address + SaFamilyOffset) = (ushort)family;
        }

        private static bool TryFamilyPlatformToPal(int platform, out int pal)
        {
            if (platform == AF_UNSPEC) pal = PalAF_UNSPEC;
            else if (platform == AF_UNIX) pal = PalAF_UNIX;
            else if (platform == AF_INET) pal = PalAF_INET;
            else if (platform == AF_INET6) pal = PalAF_INET6;
            else if (AF_PACKET >= 0 && platform == AF_PACKET) pal = PalAF_PACKET;
            else if (AF_LINK >= 0 && platform == AF_LINK) pal = PalAF_LINK;
            else if (AF_CAN >= 0 && platform == AF_CAN) pal = PalAF_CAN;
            else { pal = platform; return false; }
            return true;
        }

        private static bool TryFamilyPalToPlatform(int pal, out int platform)
        {
            switch (pal)
            {
                case PalAF_UNSPEC: platform = AF_UNSPEC; return true;
                case PalAF_UNIX: platform = AF_UNIX; return true;
                case PalAF_INET: platform = AF_INET; return true;
                case PalAF_INET6: platform = AF_INET6; return true;
                case PalAF_PACKET when AF_PACKET >= 0: platform = AF_PACKET; return true;
                case PalAF_LINK when AF_LINK >= 0: platform = AF_LINK; return true;
                case PalAF_CAN when AF_CAN >= 0: platform = AF_CAN; return true;
                default: platform = (ushort)pal; return false;
            }
        }

        // ---- socket addresses (pal_networking.c): plain memory, every target --------------------------------------------
        [RuntimeExport("SystemNative_GetSocketAddressSizes", Trigger + "SystemNative_GetSocketAddressSizes")]
        public static int GetSocketAddressSizes(int* ipv4, int* ipv6, int* uds, int* max)
        {
            if (ipv4 == null || ipv6 == null || uds == null || max == null) return Error_EFAULT;
            *ipv4 = SockaddrInSize; *ipv6 = SockaddrIn6Size; *uds = SockaddrUnSize; *max = SockaddrStorageSize;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_GetMaximumAddressSize", Trigger + "SystemNative_GetMaximumAddressSize")]
        public static int GetMaximumAddressSize() => SockaddrStorageSize;

        [RuntimeExport("SystemNative_GetDomainSocketSizes", Trigger + "SystemNative_GetDomainSocketSizes")]
        public static void GetDomainSocketSizes(int* pathOffset, int* pathSize, int* addressSize)
        {
            if (pathOffset == null || pathSize == null || addressSize == null) return;
            *pathOffset = SunPathOffset; *pathSize = SockaddrUnSize - SunPathOffset; *addressSize = SockaddrUnSize;
        }

        [RuntimeExport("SystemNative_GetAddressFamily", Trigger + "SystemNative_GetAddressFamily")]
        public static int GetAddressFamily(byte* socketAddress, int socketAddressLen, int* addressFamily)
        {
            if (socketAddress == null || addressFamily == null || socketAddressLen < 0) return Error_EFAULT;
            if (!InBounds(socketAddressLen, SaFamilyOffset, SaFamilySize)) return Error_EFAULT;
            if (!TryFamilyPlatformToPal(ReadFamily(socketAddress), out int pal)) pal = PalAF_UNKNOWN;
            *addressFamily = pal;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_SetAddressFamily", Trigger + "SystemNative_SetAddressFamily")]
        public static int SetAddressFamily(byte* socketAddress, int socketAddressLen, int addressFamily)
        {
            if (socketAddress == null || socketAddressLen < 0 || !InBounds(socketAddressLen, SaFamilyOffset, SaFamilySize)) return Error_EFAULT;
            bool known = TryFamilyPalToPlatform(addressFamily, out int platform);
            // pal_networking.c writes the (truncated) value through sa_family even when it has no platform value.
            WriteFamily(socketAddress, platform);
            return known ? Error_SUCCESS : Error_EAFNOSUPPORT;
        }

        [RuntimeExport("SystemNative_GetPort", Trigger + "SystemNative_GetPort")]
        public static int GetPort(byte* socketAddress, int socketAddressLen, ushort* port)
        {
            if (socketAddress == null || !InBounds(socketAddressLen, SaFamilyOffset, SaFamilySize)) return Error_EFAULT;
            int family = ReadFamily(socketAddress);
            int need = family == AF_INET ? SockaddrInSize : family == AF_INET6 ? SockaddrIn6Size : 0;
            if (need == 0) return Error_EAFNOSUPPORT;
            if (socketAddressLen < need) return Error_EFAULT;
            *port = (ushort)(socketAddress[2] << 8 | socketAddress[3]); // sin_port / sin6_port, network order
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_SetPort", Trigger + "SystemNative_SetPort")]
        public static int SetPort(byte* socketAddress, int socketAddressLen, ushort port)
        {
            if (socketAddress == null || !InBounds(socketAddressLen, SaFamilyOffset, SaFamilySize)) return Error_EFAULT;
            int family = ReadFamily(socketAddress);
            int need = family == AF_INET ? SockaddrInSize : family == AF_INET6 ? SockaddrIn6Size : 0;
            if (need == 0) return Error_EAFNOSUPPORT;
            if (socketAddressLen < need) return Error_EFAULT;
            socketAddress[2] = (byte)(port >> 8);
            socketAddress[3] = (byte)port;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_GetIPv4Address", Trigger + "SystemNative_GetIPv4Address")]
        public static int GetIPv4Address(byte* socketAddress, int socketAddressLen, uint* address)
        {
            if (socketAddress == null || address == null || socketAddressLen < SockaddrInSize) return Error_EFAULT;
            if (ReadFamily(socketAddress) != AF_INET) return Error_EINVAL;
            *address = *(uint*)(socketAddress + 4);
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_SetIPv4Address", Trigger + "SystemNative_SetIPv4Address")]
        public static int SetIPv4Address(byte* socketAddress, int socketAddressLen, uint address)
        {
            if (socketAddress == null || socketAddressLen < SockaddrInSize) return Error_EFAULT;
            if (ReadFamily(socketAddress) != AF_INET) return Error_EINVAL;
            WriteFamily(socketAddress, AF_INET);
            *(uint*)(socketAddress + 4) = address;
            return Error_SUCCESS;
        }

        // sockaddr_in6: sin6_flowinfo at 4, sin6_addr at 8, sin6_scope_id at 24 (both layouts).
        [RuntimeExport("SystemNative_GetIPv6Address", Trigger + "SystemNative_GetIPv6Address")]
        public static int GetIPv6Address(byte* socketAddress, int socketAddressLen, byte* address, int addressLen, uint* scopeId)
        {
            if (socketAddress == null || address == null || scopeId == null || socketAddressLen < SockaddrIn6Size || addressLen < 16) return Error_EFAULT;
            if (ReadFamily(socketAddress) != AF_INET6) return Error_EINVAL;
            Buffer.MemoryCopy(socketAddress + 8, address, 16, 16);
            *scopeId = *(uint*)(socketAddress + 24);
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_SetIPv6Address", Trigger + "SystemNative_SetIPv6Address")]
        public static int SetIPv6Address(byte* socketAddress, int socketAddressLen, byte* address, int addressLen, uint scopeId)
        {
            if (socketAddress == null || address == null || socketAddressLen < SockaddrIn6Size || addressLen < 16) return Error_EFAULT;
            if (ReadFamily(socketAddress) != AF_INET6) return Error_EINVAL;
            Buffer.MemoryCopy(address, socketAddress + 8, 16, 16);
            WriteFamily(socketAddress, AF_INET6);
            *(uint*)(socketAddress + 4) = 0;
            *(uint*)(socketAddress + 24) = scopeId;
            return Error_SUCCESS;
        }

#if CSHARPC_TARGET_WASI
        // ---- WASI: no sockets in preview 1 -------------------------------------------------------------------------------
        private static PlatformNotSupportedException NoSockets() => new PlatformNotSupportedException("System.Net.Sockets is not supported on this platform.");

        [RuntimeExport("SystemNative_InterfaceNameToIndex", Trigger + "SystemNative_InterfaceNameToIndex")]
        public static uint InterfaceNameToIndex(byte* interfaceName) => 0;

        [RuntimeExport("SystemNative_Socket", Trigger + "SystemNative_Socket")]
        public static int Socket(int addressFamily, int socketType, int protocolType, nint* createdSocket) => throw NoSockets();

        [RuntimeExport("SystemNative_GetHostEntryForName", Trigger + "SystemNative_GetHostEntryForName")]
        public static int GetHostEntryForName(byte* address, int addressFamily, HostEntry* entry) => throw NoSockets();

        [RuntimeExport("SystemNative_GetHostName", Trigger + "SystemNative_GetHostName")]
        public static int GetHostName(byte* name, int nameLength) => throw NoSockets();

        [RuntimeExport("SystemNative_CreateSocketEventPort", Trigger + "SystemNative_CreateSocketEventPort")]
        public static int CreateSocketEventPort(nint* port) => throw NoSockets();

        [StructLayout(LayoutKind.Sequential)]
        internal struct HostEntry { public byte* CanonicalName; public byte** Aliases; public byte* IPAddressList; public int IPAddressCount; }
#else
        // ---- libc -------------------------------------------------------------------------------------------------------
        [DllImport("libc", EntryPoint = "socket")] private static extern int socket_(int domain, int type, int protocol);
        [DllImport("libc")] private static extern int bind(int fd, byte* address, uint length);
        [DllImport("libc")] private static extern int connect(int fd, byte* address, uint length);
        [DllImport("libc")] private static extern int listen(int fd, int backlog);
        [DllImport("libc")] private static extern int shutdown(int fd, int how);
        [DllImport("libc")] private static extern int getpeername(int fd, byte* address, uint* length);
        [DllImport("libc")] private static extern int getsockname(int fd, byte* address, uint* length);
        [DllImport("libc")] private static extern int getsockopt(int fd, int level, int name, void* value, uint* length);
        [DllImport("libc")] private static extern int setsockopt(int fd, int level, int name, void* value, uint length);
        [DllImport("libc")] private static extern nint recv(int fd, void* buffer, nuint length, int flags);
        [DllImport("libc")] private static extern nint send(int fd, void* buffer, nuint length, int flags);
        [DllImport("libc")] private static extern nint recvmsg(int fd, void* message, int flags);
        [DllImport("libc")] private static extern nint sendmsg(int fd, void* message, int flags);
        [DllImport("libc")] private static extern int getaddrinfo(byte* node, byte* service, void* hints, void** result);
        [DllImport("libc")] private static extern void freeaddrinfo(void* info);
        [DllImport("libc")] private static extern int getnameinfo(byte* address, uint addressLength, byte* host, uint hostLength, byte* service, uint serviceLength, int flags);
        [DllImport("libc")] private static extern int gethostname(byte* name, nuint length);
        [DllImport("libc")] private static extern int getifaddrs(void** addresses);
        [DllImport("libc")] private static extern void freeifaddrs(void* addresses);
        [DllImport("libc")] private static extern int strcasecmp(byte* a, byte* b);
        [DllImport("libc")] private static extern nuint strlen(byte* s);
        [DllImport("libc")] private static extern uint if_nametoindex(byte* name);
        [DllImport("libc")] private static extern void* malloc(nuint size);
        [DllImport("libc")] private static extern void* calloc(nuint count, nuint size);
        [DllImport("libc")] private static extern void free(void* pointer);
        [DllImport("__Internal", EntryPoint = "csrt_ioctl_ptr")] private static extern int ioctl_ptr(int fd, nuint request, void* argument);
#if CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern int accept(int fd, byte* address, uint* length);
        [DllImport("libc")] private static extern int getdomainname(byte* name, int length);
        [DllImport("libc")] private static extern int kqueue();
        [DllImport("libc")] private static extern int kevent(int kq, void* changes, int changeCount, void* events, int eventCount, void* timeout);
        [DllImport("libc")] private static extern int select(int count, void* read, void* write, void* error, void* timeout);
        [DllImport("libc")] private static extern int connectx(int fd, void* endpoints, uint associd, uint flags, void* iov, uint iovcnt, nuint* length, void* connid);
        [DllImport("libc")] private static extern int disconnectx(int fd, uint associd, uint connid);
        [DllImport("libc", EntryPoint = "sendfile")] private static extern int sendfile_bsd(int fd, int s, long offset, long* length, void* headers, int flags);
        [DllImport("libc")] private static extern int proc_pidfdinfo(int pid, int fd, int flavor, void* buffer, int size);
        [DllImport("libc")] private static extern int getpid();
        [DllImport("libc")] private static extern long sysconf(int name);
#else
        [DllImport("libc")] private static extern int accept4(int fd, byte* address, uint* length, int flags);
        [DllImport("libc")] private static extern int getdomainname(byte* name, nuint length);
        [DllImport("libc")] private static extern int epoll_create1(int flags);
        [DllImport("libc")] private static extern int epoll_ctl(int epfd, int op, int fd, void* evt);
        [DllImport("libc")] private static extern int epoll_wait(int epfd, void* events, int maxEvents, int timeout);
#endif

#if CSHARPC_TARGET_MACOS
        private const int ENODEV = 19, EPROTOTYPE = 41;
        private const int SOCK_CLOEXEC = 0, F_GETFD = 1, F_SETFD = 2, FD_CLOEXEC = 1;
        private const int SOL_SOCKET = 0xffff, SO_DEBUG = 0x1, SO_ACCEPTCONN = 0x2, SO_REUSEADDR = 0x4, SO_KEEPALIVE = 0x8, SO_DONTROUTE = 0x10,
            SO_BROADCAST = 0x20, SO_LINGER = 0x80, SO_OOBINLINE = 0x100, SO_REUSEPORT = 0x200, SO_SNDBUF = 0x1001, SO_RCVBUF = 0x1002,
            SO_SNDLOWAT = 0x1003, SO_RCVLOWAT = 0x1004, SO_SNDTIMEO = 0x1005, SO_RCVTIMEO = 0x1006, SO_ERROR = 0x1007, SO_TYPE = 0x1008,
            SO_LINGER_SEC = 0x1080, SO_DOMAIN = -1, SO_PROTOCOL = -1;
        private const int LingerOptionName = SO_LINGER_SEC;
        private const int IP_OPTIONS = 1, IP_HDRINCL = 2, IP_TOS = 3, IP_TTL = 4, IP_MULTICAST_IF = 9, IP_MULTICAST_TTL = 10, IP_MULTICAST_LOOP = 11,
            IP_ADD_MEMBERSHIP = 12, IP_DROP_MEMBERSHIP = 13, IP_DONTFRAG = 28, IP_MTU_DISCOVER = -1, IP_ADD_SOURCE_MEMBERSHIP = 70,
            IP_DROP_SOURCE_MEMBERSHIP = 71, IP_BLOCK_SOURCE = 72, IP_UNBLOCK_SOURCE = 73, IP_PKTINFO = 26, IP_MULTICAST_IFINDEX = 66;
        private const int IPV6_UNICAST_HOPS = 4, IPV6_MULTICAST_IF = 9, IPV6_MULTICAST_HOPS = 10, IPV6_MULTICAST_LOOP = 11, IPV6_ADD_MEMBERSHIP = 12,
            IPV6_DROP_MEMBERSHIP = 13, IPV6_V6ONLY = 27, IPV6_RECVPKTINFO = 61, IPV6_PKTINFO = 46, IPV6_HOPLIMIT = 47;
        private const int TCP_NODELAY = 1, TCP_KEEPIDLE = 0x10 /* TCP_KEEPALIVE */, TCP_KEEPINTVL = 0x101, TCP_KEEPCNT = 0x102, TCP_FASTOPEN = 0x105;
        private const int MSG_OOB = 0x1, MSG_PEEK = 0x2, MSG_DONTROUTE = 0x4, MSG_TRUNC = 0x10, MSG_CTRUNC = 0x20, MSG_DONTWAIT = 0x80, MSG_ERRQUEUE = 0;
        private const int SOCK_RDM = 4;
        private const nuint FIONREAD = 0x4004667f, SIOCATMARK = 0x40047307;
        private const int EAI_AGAIN = 2, EAI_BADFLAGS = 3, EAI_FAIL = 4, EAI_FAMILY = 5, EAI_MEMORY = 6, EAI_NODATA = 7, EAI_NONAME = 8, EAI_SYSTEM = 11;
        private const int NI_NUMERICHOST = 0x2, NI_NAMEREQD = 0x4;
        // struct msghdr: name, namelen, iov, int iovlen, control, socklen_t controllen, flags.
        private const int MsghdrSize = 48, MsgIovLenOffset = 24, MsgControlOffset = 32, MsgControlLenOffset = 40, MsgFlagsOffset = 44;
        // struct cmsghdr: socklen_t len, level, type; data 4-aligned.
        private const int CmsgHeaderSize = 12, CmsgAlign = 4;
        private const int IpMreqSize = 8;
        // struct addrinfo: ai_canonname before ai_addr.
        private const int AiCanonNameOffset = 24, AiAddrOffset = 32;
        private const int KeventSize = 32;
        private const short EVFILT_READ = -1, EVFILT_WRITE = -2;
        private const ushort EV_ADD = 0x1, EV_DELETE = 0x2, EV_CLEAR = 0x20, EV_EOF = 0x8000, EV_ERROR = 0x4000;
        private const int SocketEventElementSize = KeventSize;
        private const int DualModeIPv4PacketInfo = 0;
#else
        private const int ENODEV = 19, EPROTOTYPE = 91;
        private const int SOCK_CLOEXEC = 0x80000, F_GETFD = 1, F_SETFD = 2, FD_CLOEXEC = 1;
        private const int SOL_SOCKET = 1, SO_DEBUG = 1, SO_REUSEADDR = 2, SO_TYPE = 3, SO_ERROR = 4, SO_DONTROUTE = 5, SO_BROADCAST = 6,
            SO_SNDBUF = 7, SO_RCVBUF = 8, SO_KEEPALIVE = 9, SO_OOBINLINE = 10, SO_LINGER = 13, SO_REUSEPORT = 15, SO_RCVLOWAT = 18,
            SO_SNDLOWAT = 19, SO_RCVTIMEO = 20, SO_SNDTIMEO = 21, SO_ACCEPTCONN = 30, SO_PROTOCOL = 38, SO_DOMAIN = 39;
        private const int LingerOptionName = SO_LINGER;
        private const int IP_TOS = 1, IP_TTL = 2, IP_HDRINCL = 3, IP_OPTIONS = 4, IP_PKTINFO = 8, IP_MTU_DISCOVER = 10, IP_RECVERR = 11,
            IP_MULTICAST_IF = 32, IP_MULTICAST_TTL = 33, IP_MULTICAST_LOOP = 34, IP_ADD_MEMBERSHIP = 35, IP_DROP_MEMBERSHIP = 36,
            IP_UNBLOCK_SOURCE = 37, IP_BLOCK_SOURCE = 38, IP_ADD_SOURCE_MEMBERSHIP = 39, IP_DROP_SOURCE_MEMBERSHIP = 40, IP_DONTFRAG = -1,
            IP_MULTICAST_IFINDEX = -1, IP_PMTUDISC_DONT = 0, IP_PMTUDISC_DO = 2;
        private const int IPV6_UNICAST_HOPS = 16, IPV6_MULTICAST_IF = 17, IPV6_MULTICAST_HOPS = 18, IPV6_MULTICAST_LOOP = 19, IPV6_ADD_MEMBERSHIP = 20,
            IPV6_DROP_MEMBERSHIP = 21, IPV6_V6ONLY = 26, IPV6_RECVPKTINFO = 49, IPV6_PKTINFO = 50, IPV6_HOPLIMIT = 52;
        private const int TCP_NODELAY = 1, TCP_KEEPIDLE = 4, TCP_KEEPINTVL = 5, TCP_KEEPCNT = 6, TCP_FASTOPEN = 23, TCP_FASTOPEN_CONNECT = 30;
        private const int MSG_OOB = 0x1, MSG_PEEK = 0x2, MSG_DONTROUTE = 0x4, MSG_CTRUNC = 0x8, MSG_TRUNC = 0x20, MSG_DONTWAIT = 0x40, MSG_ERRQUEUE = 0x2000;
        private const int SOCK_RDM = 4;
        private const nuint FIONREAD = 0x541B, SIOCATMARK = 0x8905;
        private const int EAI_AGAIN = -3, EAI_BADFLAGS = -1, EAI_FAIL = -4, EAI_FAMILY = -6, EAI_MEMORY = -10, EAI_NODATA = -5, EAI_NONAME = -2, EAI_SYSTEM = -11;
        private const int NI_NUMERICHOST = 1, NI_NAMEREQD = 8;
        // struct msghdr (glibc, 64-bit): name, namelen, iov, size_t iovlen, control, size_t controllen, flags.
        private const int MsghdrSize = 56, MsgIovLenOffset = 24, MsgControlOffset = 32, MsgControlLenOffset = 40, MsgFlagsOffset = 48;
        // struct cmsghdr: size_t len, level, type; data 8-aligned.
        private const int CmsgHeaderSize = 16, CmsgAlign = 8;
        private const int IpMreqSize = 12; // struct ip_mreqn
        // struct addrinfo: ai_addr before ai_canonname.
        private const int AiCanonNameOffset = 32, AiAddrOffset = 24;
        private const uint EPOLLIN = 0x1, EPOLLOUT = 0x4, EPOLLERR = 0x8, EPOLLHUP = 0x10, EPOLLRDHUP = 0x2000, EPOLLET = 0x80000000;
        private const int EPOLL_CTL_ADD = 1, EPOLL_CTL_DEL = 2, EPOLL_CTL_MOD = 3, EPOLL_CLOEXEC = 0x80000;
#if CSHARPC_TARGET_ARM64
        // struct epoll_event is naturally aligned on arm64 (data at 8), packed on x86-64 (data at 4).
        private const int EpollEventSize = 16, EpollDataOffset = 8;
#else
        private const int EpollEventSize = 12, EpollDataOffset = 4;
#endif
        private const int SocketEventElementSize = 16; // max(sizeof(epoll_event), sizeof(SocketEvent))
        private const int DualModeIPv4PacketInfo = 1;
#endif
        private const int SOCK_STREAM = 1, SOCK_DGRAM = 2, SOCK_RAW = 3, SOCK_SEQPACKET = 5;
        private const int IPPROTO_IP = 0, IPPROTO_ICMP = 1, IPPROTO_IGMP = 2, IPPROTO_TCP = 6, IPPROTO_UDP = 17, IPPROTO_IPV6 = 41,
            IPPROTO_ROUTING = 43, IPPROTO_FRAGMENT = 44, IPPROTO_ICMPV6 = 58, IPPROTO_NONE = 59, IPPROTO_DSTOPTS = 60, IPPROTO_RAW = 255;
        private const int SHUT_RD = 0, SHUT_WR = 1, SHUT_RDWR = 2;
        private const int AI_CANONNAME = 2, IFF_UP = 0x1, IFF_LOOPBACK = 0x8, HostNameMax = 255;
        private const int IOV_MAX_NET = 1024;

        private static int PalErrno() => ToPal(Errno);

        // ---- names (pal_networking.c) ---------------------------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        internal struct HostEntry { public byte* CanonicalName; public byte** Aliases; public byte* IPAddressList; public int IPAddressCount; }

        // IPAddress: 16 address bytes, IsIPv6, ScopeId (24 bytes).
        private const int PalIPAddressSize = 24;

        private static int GaiToPal(int error)
        {
            if (error == 0) return 0;
            if (error == EAI_AGAIN) return EAI_PAL_AGAIN;
            if (error == EAI_BADFLAGS) return EAI_PAL_BADFLAGS;
            if (error == EAI_FAIL) return EAI_PAL_FAIL;
            if (error == EAI_FAMILY) return EAI_PAL_FAMILY;
            if (error == EAI_MEMORY) return EAI_PAL_MEMORY;
            if (error == EAI_NONAME || error == EAI_NODATA) return EAI_PAL_NONAME;
            if (error == EAI_SYSTEM) return EAI_PAL_SYSTEM;
            return -1;
        }

        /// <summary>CopySockAddrToIPAddress: an AF_INET/AF_INET6 address into one PAL IPAddress (exactly 24 bytes); -1 for other families.</summary>
        private static int CopySockAddr(byte* address, int family, byte* ipAddress)
        {
            if (family == AF_INET)
            {
                *(uint*)ipAddress = *(uint*)(address + 4);
                *(uint*)(ipAddress + 16) = 0;
                return 0;
            }
            if (family == AF_INET6)
            {
                Buffer.MemoryCopy(address + 8, ipAddress, 16, 16);
                *(uint*)(ipAddress + 16) = 1;
                *(uint*)(ipAddress + 20) = *(uint*)(address + 24);
                return 0;
            }
            return -1;
        }

        // struct ifaddrs: next, name, flags, addr, netmask (same offsets on Linux and macOS).
        private static void* IfaNext(void* ifa) => *(void**)ifa;
        private static uint IfaFlags(void* ifa) => *(uint*)((byte*)ifa + 16);
        private static byte* IfaAddr(void* ifa) => *(byte**)((byte*)ifa + 24);

        /// <summary>
        /// SystemNative_GetHostEntryForName: getaddrinfo(AI_CANONNAME) for the name, plus the local interfaces' addresses when the name is
        /// this host's (getifaddrs, loopback ones only when the host has nothing else of that family), into a malloc'ed list sized by a first
        /// counting pass; the second pass writes at most that many entries.
        /// </summary>
        [RuntimeExport("SystemNative_GetHostEntryForName", Trigger + "SystemNative_GetHostEntryForName")]
        public static int GetHostEntryForName(byte* address, int addressFamily, HostEntry* entry)
        {
            if (address == null || entry == null) return EAI_PAL_BADARG;
            if (!TryFamilyPalToPlatform(addressFamily, out int platformFamily)) return EAI_PAL_FAMILY;
            byte* hint = stackalloc byte[48];
            new Span<byte>(hint, 48).Clear();
            *(int*)hint = AI_CANONNAME;
            *(int*)(hint + 4) = platformFamily;
            void* info = null;
            int result = getaddrinfo(address, null, hint, &info);
            if (result != 0) return GaiToPal(result);

            int ret = 0;
            void* addrs = null;
            entry->CanonicalName = null;
            entry->Aliases = null;
            entry->IPAddressList = null;
            entry->IPAddressCount = 0;
            int capacity = 0;
            for (byte* ai = (byte*)info; ai != null; ai = *(byte**)(ai + 40))
            {
                byte* canon = *(byte**)(ai + AiCanonNameOffset);
                if (entry->CanonicalName == null && canon != null)
                {
                    nuint length = strlen(canon) + 1;
                    byte* copy = (byte*)malloc(length);
                    if (copy == null) { ret = GaiToPal(EAI_MEMORY); goto cleanup; }
                    Buffer.MemoryCopy(canon, copy, (long)length, (long)length);
                    entry->CanonicalName = copy;
                }
                int family = *(int*)(ai + 4);
                if (family == AF_INET || family == AF_INET6) capacity++;
            }

            bool includeIPv4Loopback = true, includeIPv6Loopback = true;
            byte* name = stackalloc byte[HostNameMax + 1];
            if (gethostname(name, HostNameMax) == 0)
            {
                name[HostNameMax] = 0;
                if (strcasecmp(address, name) == 0 && getifaddrs(&addrs) == 0)
                {
                    for (void* ifa = addrs; ifa != null; ifa = IfaNext(ifa))
                    {
                        byte* ifaAddr = IfaAddr(ifa);
                        if (ifaAddr == null || (IfaFlags(ifa) & IFF_UP) == 0) continue;
                        int interfaceFamily = ReadFamily(ifaAddr);
                        if (platformFamily != AF_UNSPEC && interfaceFamily != platformFamily) continue;
                        if (interfaceFamily == AF_INET)
                        {
                            if ((IfaFlags(ifa) & IFF_LOOPBACK) == 0) includeIPv4Loopback = false;
                            capacity++;
                        }
                        else if (interfaceFamily == AF_INET6)
                        {
                            if ((IfaFlags(ifa) & IFF_LOOPBACK) == 0) includeIPv6Loopback = false;
                            capacity++;
                        }
                    }
                }
            }

            if (capacity > 0)
            {
                byte* list = (byte*)calloc((nuint)capacity, PalIPAddressSize);
                if (list == null) { ret = GaiToPal(EAI_MEMORY); goto cleanup; }
                entry->IPAddressList = list;
                int count = 0;
                for (byte* ai = (byte*)info; ai != null && count < capacity; ai = *(byte**)(ai + 40))
                    if (CopySockAddr(*(byte**)(ai + AiAddrOffset), *(int*)(ai + 4), list + count * PalIPAddressSize) == 0) count++;
                if (addrs != null)
                {
                    for (void* ifa = addrs; ifa != null && count < capacity; ifa = IfaNext(ifa))
                    {
                        byte* ifaAddr = IfaAddr(ifa);
                        if (ifaAddr == null || (IfaFlags(ifa) & IFF_UP) == 0) continue;
                        int interfaceFamily = ReadFamily(ifaAddr);
                        if (platformFamily != AF_UNSPEC && interfaceFamily != platformFamily) continue;
                        bool loopback = (IfaFlags(ifa) & IFF_LOOPBACK) != 0;
                        if ((!includeIPv4Loopback && interfaceFamily == AF_INET && loopback) || (!includeIPv6Loopback && interfaceFamily == AF_INET6 && loopback))
                            continue;
                        if (CopySockAddr(ifaAddr, interfaceFamily, list + count * PalIPAddressSize) == 0) count++;
                    }
                }
                entry->IPAddressCount = count;
            }

        cleanup:
            if (info != null) freeaddrinfo(info);
            if (addrs != null) freeifaddrs(addrs);
            if (ret != 0) FreeHostEntry(entry);
            return ret;
        }

        [RuntimeExport("SystemNative_FreeHostEntry", Trigger + "SystemNative_FreeHostEntry")]
        public static void FreeHostEntry(HostEntry* entry)
        {
            if (entry == null) return;
            free(entry->CanonicalName);
            free(entry->IPAddressList);
            entry->CanonicalName = null;
            entry->IPAddressList = null;
            entry->IPAddressCount = 0;
        }

        /// <summary>SystemNative_GetNameInfo: getnameinfo over a sockaddr built from the 4 or 16 address bytes; host and service are written by libc within the lengths passed.</summary>
        [RuntimeExport("SystemNative_GetNameInfo", Trigger + "SystemNative_GetNameInfo")]
        public static int GetNameInfo(byte* address, uint addressLength, byte isIPv6, byte* host, uint hostLength, byte* service, uint serviceLength, int flags)
        {
            if (address == null || addressLength != (isIPv6 != 0 ? 16u : 4u)) return GaiToPal(EAI_FAIL);
            int nativeFlags = ((flags & 0x1) != 0 ? NI_NAMEREQD : 0) | ((flags & 0x2) != 0 ? NI_NUMERICHOST : 0);
            byte* sockaddr = stackalloc byte[SockaddrIn6Size];
            new Span<byte>(sockaddr, SockaddrIn6Size).Clear();
            int length;
            if (isIPv6 != 0)
            {
                WriteFamily(sockaddr, AF_INET6);
                Buffer.MemoryCopy(address, sockaddr + 8, 16, 16);
                length = SockaddrIn6Size;
            }
            else
            {
                WriteFamily(sockaddr, AF_INET);
                *(uint*)(sockaddr + 4) = *(uint*)address;
                length = SockaddrInSize;
            }
#if CSHARPC_TARGET_MACOS
            sockaddr[0] = (byte)length; // sa_len
#endif
            return GaiToPal(getnameinfo(sockaddr, (uint)length, host, hostLength, service, serviceLength, nativeFlags));
        }

        [RuntimeExport("SystemNative_GetDomainName", Trigger + "SystemNative_GetDomainName")]
        public static int GetDomainName(byte* name, int nameLength)
        {
            if (name == null || nameLength <= 0) return Fail(EINVAL);
#if CSHARPC_TARGET_MACOS
            return getdomainname(name, nameLength);
#else
            return getdomainname(name, (nuint)nameLength);
#endif
        }

        [RuntimeExport("SystemNative_GetHostName", Trigger + "SystemNative_GetHostName")]
        public static int GetHostName(byte* name, int nameLength)
        {
            if (name == null || nameLength <= 0) return Fail(EINVAL);
            return gethostname(name, (nuint)nameLength);
        }

        [RuntimeExport("SystemNative_InterfaceNameToIndex", Trigger + "SystemNative_InterfaceNameToIndex")]
        public static uint InterfaceNameToIndex(byte* interfaceName)
        {
            if (interfaceName == null) return 0;
            if (interfaceName[0] == (byte)'%') interfaceName++;
            return if_nametoindex(interfaceName);
        }

        // ---- sockets (pal_networking.c) ---------------------------------------------------------------------------------------
        private static bool TrySocketTypePalToPlatform(int pal, out int platform)
        {
            switch (pal)
            {
                case 1: platform = SOCK_STREAM; return true;
                case 2: platform = SOCK_DGRAM; return true;
                case 3: platform = SOCK_RAW; return true;
                case 4: platform = SOCK_RDM; return true;
                case 5: platform = SOCK_SEQPACKET; return true;
                default: platform = pal; return false;
            }
        }

        private static bool TrySocketTypePlatformToPal(int platform, out int pal)
        {
            if (platform == SOCK_STREAM) pal = 1;
            else if (platform == SOCK_DGRAM) pal = 2;
            else if (platform == SOCK_RAW) pal = 3;
            else if (platform == SOCK_RDM) pal = 4;
            else if (platform == SOCK_SEQPACKET) pal = 5;
            else { pal = platform; return false; }
            return true;
        }

        private static bool TryProtocolPalToPlatform(int palFamily, int pal, out int platform)
        {
            platform = pal;
            if (palFamily == PalAF_PACKET && AF_PACKET >= 0 || palFamily == PalAF_LINK && AF_LINK >= 0) return true;
            if (palFamily == PalAF_CAN && AF_CAN >= 0)
            {
                if (pal == PalPT_UNSPECIFIED) { platform = 0; return true; }
                if (pal == PalPT_RAW) { platform = 1 /* CAN_RAW */; return true; }
                return false;
            }
            if (pal == PalPT_UNSPECIFIED) { platform = 0; return true; }
            if (palFamily == PalAF_INET)
            {
                switch (pal)
                {
                    case PalPT_ICMP: platform = IPPROTO_ICMP; return true;
                    case PalPT_TCP: platform = IPPROTO_TCP; return true;
                    case PalPT_UDP: platform = IPPROTO_UDP; return true;
                    case PalPT_IGMP: platform = IPPROTO_IGMP; return true;
                    case PalPT_RAW: platform = IPPROTO_RAW; return true;
                    default: return false;
                }
            }
            if (palFamily == PalAF_INET6)
            {
                switch (pal)
                {
                    case PalPT_ICMPV6: case PalPT_ICMP: platform = IPPROTO_ICMPV6; return true;
                    case PalPT_TCP: platform = IPPROTO_TCP; return true;
                    case PalPT_UDP: platform = IPPROTO_UDP; return true;
                    case PalPT_IGMP: platform = IPPROTO_IGMP; return true;
                    case PalPT_RAW: platform = IPPROTO_RAW; return true;
                    case PalPT_DSTOPTS: platform = IPPROTO_DSTOPTS; return true;
                    case PalPT_NONE: platform = IPPROTO_NONE; return true;
                    case PalPT_ROUTING: platform = IPPROTO_ROUTING; return true;
                    case PalPT_FRAGMENT: platform = IPPROTO_FRAGMENT; return true;
                    default: return false;
                }
            }
            return false;
        }

        private static bool TryProtocolPlatformToPal(int palFamily, int platform, out int pal)
        {
            pal = platform;
            if (palFamily == PalAF_PACKET && AF_PACKET >= 0 || palFamily == PalAF_LINK && AF_LINK >= 0) return true;
            if (palFamily == PalAF_CAN && AF_CAN >= 0)
            {
                if (platform == 0) { pal = PalPT_UNSPECIFIED; return true; }
                if (platform == 1) { pal = PalPT_RAW; return true; }
                return false;
            }
            if (platform == 0) { pal = PalPT_UNSPECIFIED; return true; }
            if (palFamily == PalAF_INET)
            {
                switch (platform)
                {
                    case IPPROTO_ICMP: pal = PalPT_ICMP; return true;
                    case IPPROTO_TCP: pal = PalPT_TCP; return true;
                    case IPPROTO_UDP: pal = PalPT_UDP; return true;
                    case IPPROTO_IGMP: pal = PalPT_IGMP; return true;
                    case IPPROTO_RAW: pal = PalPT_RAW; return true;
                    default: return false;
                }
            }
            if (palFamily == PalAF_INET6)
            {
                switch (platform)
                {
                    case IPPROTO_ICMPV6: pal = PalPT_ICMPV6; return true;
                    case IPPROTO_TCP: pal = PalPT_TCP; return true;
                    case IPPROTO_UDP: pal = PalPT_UDP; return true;
                    case IPPROTO_IGMP: pal = PalPT_IGMP; return true;
                    case IPPROTO_RAW: pal = PalPT_RAW; return true;
                    case IPPROTO_DSTOPTS: pal = PalPT_DSTOPTS; return true;
                    case IPPROTO_NONE: pal = PalPT_NONE; return true;
                    case IPPROTO_ROUTING: pal = PalPT_ROUTING; return true;
                    case IPPROTO_FRAGMENT: pal = PalPT_FRAGMENT; return true;
                    default: return false;
                }
            }
            return false;
        }

        /// <summary>SystemNative_Socket: socket(2) with SOCK_CLOEXEC (macOS: FD_CLOEXEC right after, best effort, as there).</summary>
        [RuntimeExport("SystemNative_Socket", Trigger + "SystemNative_Socket")]
        public static int Socket(int addressFamily, int socketType, int protocolType, nint* createdSocket)
        {
            if (createdSocket == null) return Error_EFAULT;
            if (!TryFamilyPalToPlatform(addressFamily, out int family)) { *createdSocket = -1; return Error_EAFNOSUPPORT; }
            if (!TrySocketTypePalToPlatform(socketType, out int type)) { *createdSocket = -1; return Error_EPROTOTYPE; }
            if (!TryProtocolPalToPlatform(addressFamily, protocolType, out int protocol)) { *createdSocket = -1; return Error_EPROTONOSUPPORT; }
            int fd = socket_(family, type | SOCK_CLOEXEC, protocol);
            *createdSocket = fd;
            if (fd == -1) return PalErrno();
#if CSHARPC_TARGET_MACOS
            fcntl_int(fd, F_SETFD, FD_CLOEXEC);
#endif
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_GetSocketType", Trigger + "SystemNative_GetSocketType")]
        public static int GetSocketType(nint socket, int* addressFamily, int* socketType, int* protocolType, int* isListening)
        {
            if (addressFamily == null || socketType == null || protocolType == null || isListening == null) return Error_EFAULT;
            int fd = Fd(socket);
#if CSHARPC_TARGET_MACOS
            // proc_pidfdinfo(PROC_PIDFDSOCKETINFO): struct socket_fdinfo is proc_fileinfo (24 bytes) then socket_info, whose soi_type,
            // soi_protocol, soi_family and soi_options follow vinfo_stat (136 bytes) and two 64-bit words.
            const int PROC_PIDFDSOCKETINFO = 3, SoiType = 24 + 136 + 16, SoiProtocol = SoiType + 4, SoiFamily = SoiType + 8, SoiOptions = SoiType + 12;
            byte* info = stackalloc byte[1024];
            int got = proc_pidfdinfo(getpid(), fd, PROC_PIDFDSOCKETINFO, info, 1024);
            if (got < SoiOptions + 2) return Error_EFAULT;
            if (!TryFamilyPlatformToPal(*(int*)(info + SoiFamily), out int pal)) pal = PalAF_UNKNOWN;
            *addressFamily = pal;
            if (!TrySocketTypePlatformToPal(*(int*)(info + SoiType), out int palType)) palType = PalSock_UNKNOWN;
            *socketType = palType;
            if (!TryProtocolPlatformToPal(pal, *(int*)(info + SoiProtocol), out int palProtocol)) palProtocol = PalPT_UNKNOWN;
            *protocolType = palProtocol;
            *isListening = (*(short*)(info + SoiOptions) & SO_ACCEPTCONN) != 0 ? 1 : 0;
#else
            int value;
            uint length = 4;
            if (getsockopt(fd, SOL_SOCKET, SO_DOMAIN, &value, &length) != 0 || !TryFamilyPlatformToPal((ushort)value, out int pal)) pal = PalAF_UNKNOWN;
            *addressFamily = pal;
            length = 4;
            if (getsockopt(fd, SOL_SOCKET, SO_TYPE, &value, &length) != 0 || !TrySocketTypePlatformToPal(value, out int palType)) palType = PalSock_UNKNOWN;
            *socketType = palType;
            length = 4;
            if (getsockopt(fd, SOL_SOCKET, SO_PROTOCOL, &value, &length) != 0 || !TryProtocolPlatformToPal(pal, value, out int palProtocol)) palProtocol = PalPT_UNKNOWN;
            *protocolType = palProtocol;
            length = 4;
            *isListening = getsockopt(fd, SOL_SOCKET, SO_ACCEPTCONN, &value, &length) == 0 && value != 0 ? 1 : 0;
#endif
            return Error_SUCCESS;
        }

        /// <summary>SystemNative_Accept: accept4(SOCK_CLOEXEC) on Linux; accept + FD_CLOEXEC + blocking mode on macOS (closing the new socket if either fails), as there.</summary>
        [RuntimeExport("SystemNative_Accept", Trigger + "SystemNative_Accept")]
        public static int Accept(nint socket, byte* socketAddress, int* socketAddressLen, nint* acceptedSocket)
        {
            if (socketAddress == null || socketAddressLen == null || acceptedSocket == null || *socketAddressLen < 0) return Error_EFAULT;
            int fd = Fd(socket);
            uint length = (uint)*socketAddressLen;
            int accepted;
#if CSHARPC_TARGET_MACOS
            while ((accepted = accept(fd, socketAddress, &length)) < 0 && Errno == EINTR) { }
            if (accepted != -1 && (fcntl_int(accepted, F_SETFD, FD_CLOEXEC) != 0 || FcntlSetIsNonBlocking(accepted, 0) != 0))
            {
                int saved = Errno;
                close(accepted);
                accepted = -1;
                Marshal.SetLastSystemError(saved);
            }
#else
            while ((accepted = accept4(fd, socketAddress, &length, SOCK_CLOEXEC)) < 0 && Errno == EINTR) { }
#endif
            if (accepted == -1)
            {
                *acceptedSocket = -1;
                return PalErrno();
            }
            // The kernel reports the full address length, which can exceed the buffer it filled (truncated).
            *socketAddressLen = (int)Math.Min(length, (uint)*socketAddressLen);
            *acceptedSocket = accepted;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_Bind", Trigger + "SystemNative_Bind")]
        public static int Bind(nint socket, int protocolType, byte* socketAddress, int socketAddressLen)
        {
            if (socketAddress == null || socketAddressLen < 0) return Error_EFAULT;
            int fd = Fd(socket);
            // As pal_networking.c: a TCP socket always binds with SO_REUSEADDR (TIME_WAIT reuse, Windows' default); ExclusiveAddressUse is SO_REUSEPORT.
            if (protocolType == PalPT_TCP)
            {
                int one = 1;
                setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &one, 4);
            }
            return bind(fd, socketAddress, (uint)socketAddressLen) == 0 ? Error_SUCCESS : PalErrno();
        }

        [RuntimeExport("SystemNative_Connect", Trigger + "SystemNative_Connect")]
        public static int Connect(nint socket, byte* socketAddress, int socketAddressLen)
        {
            if (socketAddress == null || socketAddressLen < 0) return Error_EFAULT;
            int err;
            while ((err = connect(Fd(socket), socketAddress, (uint)socketAddressLen)) < 0 && Errno == EINTR) { }
            return err == 0 ? Error_SUCCESS : PalErrno();
        }

        /// <summary>SystemNative_Connectx: macOS connectx with the first data (TCP Fast Open); Linux enables TCP_FASTOPEN_CONNECT when the socket has TCP_FASTOPEN, then connects (no data sent).</summary>
        [RuntimeExport("SystemNative_Connectx", Trigger + "SystemNative_Connectx")]
        public static int Connectx(nint socket, byte* socketAddress, int socketAddressLen, byte* data, int dataLen, int tfo, int* sent)
        {
            if (socketAddress == null || socketAddressLen < 0 || sent == null) return Error_EFAULT;
            int fd = Fd(socket);
#if CSHARPC_TARGET_MACOS
            const uint CONNECT_DATA_IDEMPOTENT = 0x2;
            if (dataLen < 0) dataLen = 0;
            byte* endpoints = stackalloc byte[40];
            new Span<byte>(endpoints, 40).Clear();
            *(byte**)(endpoints + 24) = socketAddress;
            *(uint*)(endpoints + 32) = (uint)socketAddressLen;
            nint* iov = stackalloc nint[2];
            iov[0] = (nint)data;
            iov[1] = dataLen;
            nuint length = 0;
            int err;
            while ((err = connectx(fd, endpoints, 0, tfo != 0 ? CONNECT_DATA_IDEMPOTENT : 0, dataLen > 0 ? iov : null, dataLen > 0 ? 1u : 0u, &length, null)) < 0
                && Errno == EINTR) { }
            *sent = (int)length;
            return err == 0 ? Error_SUCCESS : PalErrno();
#else
            int enabled = 1;
            uint length = 4;
            if (getsockopt(fd, IPPROTO_TCP, TCP_FASTOPEN, &enabled, &length) == 0 && enabled != 0)
                setsockopt(fd, IPPROTO_TCP, TCP_FASTOPEN_CONNECT, &enabled, length);
            // pal_networking.c leaves *sent untouched here (it assigns the local pointer): the caller sends the data itself.
            return Connect(socket, socketAddress, socketAddressLen);
#endif
        }

        [RuntimeExport("SystemNative_GetPeerName", Trigger + "SystemNative_GetPeerName")]
        public static int GetPeerName(nint socket, byte* socketAddress, int* socketAddressLen)
        {
            if (socketAddress == null || socketAddressLen == null || *socketAddressLen < 0) return Error_EFAULT;
            uint length = (uint)*socketAddressLen;
            if (getpeername(Fd(socket), socketAddress, &length) != 0) return PalErrno();
            *socketAddressLen = (int)length;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_GetSockName", Trigger + "SystemNative_GetSockName")]
        public static int GetSockName(nint socket, byte* socketAddress, int* socketAddressLen)
        {
            if (socketAddress == null || socketAddressLen == null || *socketAddressLen < 0) return Error_EFAULT;
            uint length = (uint)*socketAddressLen;
            if (getsockname(Fd(socket), socketAddress, &length) != 0) return PalErrno();
            *socketAddressLen = (int)length;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_Listen", Trigger + "SystemNative_Listen")]
        public static int Listen(nint socket, int backlog) => listen(Fd(socket), backlog) == 0 ? Error_SUCCESS : PalErrno();

        [RuntimeExport("SystemNative_Shutdown", Trigger + "SystemNative_Shutdown")]
        public static int Shutdown(nint socket, int socketShutdown)
        {
            int how = socketShutdown switch { 0 => SHUT_RD, 1 => SHUT_WR, 2 => SHUT_RDWR, _ => -1 };
            if (how < 0) return Error_EINVAL;
            return shutdown(Fd(socket), how) == 0 ? Error_SUCCESS : PalErrno();
        }

        /// <summary>SystemNative_Disconnect: Linux dissolves the association with connect(AF_UNSPEC), macOS with disconnectx; either falls back to shutdown(SHUT_RDWR).</summary>
        [RuntimeExport("SystemNative_Disconnect", Trigger + "SystemNative_Disconnect")]
        public static int Disconnect(nint socket)
        {
            int fd = Fd(socket);
#if CSHARPC_TARGET_MACOS
            int err = disconnectx(fd, 0, 0);
#else
            byte* address = stackalloc byte[16];
            new Span<byte>(address, 16).Clear();
            WriteFamily(address, AF_UNSPEC);
            int err = connect(fd, address, 16);
#endif
            if (err != 0) err = shutdown(fd, SHUT_RDWR);
            return err == 0 ? Error_SUCCESS : PalErrno();
        }

        [RuntimeExport("SystemNative_GetSocketErrorOption", Trigger + "SystemNative_GetSocketErrorOption")]
        public static int GetSocketErrorOption(nint socket, int* error)
        {
            if (error == null) return Error_EFAULT;
            int socketErrno;
            uint length = 4;
            if (getsockopt(Fd(socket), SOL_SOCKET, SO_ERROR, &socketErrno, &length) != 0) return PalErrno();
            *error = ToPal(socketErrno);
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_GetBytesAvailable", Trigger + "SystemNative_GetBytesAvailable")]
        public static int GetBytesAvailable(nint socket, int* available)
        {
            if (available == null) return Error_EFAULT;
            int result, err;
            while ((err = ioctl_ptr(Fd(socket), FIONREAD, &result)) < 0 && Errno == EINTR) { }
            if (err == -1) { *available = 0; return PalErrno(); }
            *available = result;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_GetAtOutOfBandMark", Trigger + "SystemNative_GetAtOutOfBandMark")]
        public static int GetAtOutOfBandMark(nint socket, int* atMark)
        {
            if (atMark == null) return Error_EFAULT;
            int result, err;
            while ((err = ioctl_ptr(Fd(socket), SIOCATMARK, &result)) < 0 && Errno == EINTR) { }
            if (err == -1) { *atMark = 0; return PalErrno(); }
            *atMark = result;
            return Error_SUCCESS;
        }

        // ---- options ------------------------------------------------------------------------------------------------------------
        private static bool TryGetPlatformSocketOption(int level, int name, out int optLevel, out int optName)
        {
            optName = -1;
            switch (level)
            {
                case PalSOL_SOCKET:
                    optLevel = SOL_SOCKET;
                    optName = name switch
                    {
                        0x0001 => SO_DEBUG, 0x0002 => SO_ACCEPTCONN, 0x0004 => SO_REUSEADDR, 0x0008 => SO_KEEPALIVE, 0x0010 => SO_DONTROUTE,
                        0x0020 => SO_BROADCAST, 0x0080 => SO_LINGER, 0x0100 => SO_OOBINLINE, 0x1001 => SO_SNDBUF, 0x1002 => SO_RCVBUF,
                        0x1003 => SO_SNDLOWAT, 0x1004 => SO_RCVLOWAT, 0x1005 => SO_SNDTIMEO, 0x1006 => SO_RCVTIMEO, 0x1007 => SO_ERROR,
                        0x1008 => SO_TYPE, _ => -1,
                    };
                    break;
                case PalSOL_IP:
                    optLevel = IPPROTO_IP;
                    optName = name switch
                    {
                        1 => IP_OPTIONS, 2 => IP_HDRINCL, 3 => IP_TOS, 4 => IP_TTL, 9 => IP_MULTICAST_IF, 10 => IP_MULTICAST_TTL,
                        11 => IP_MULTICAST_LOOP, 12 => IP_ADD_MEMBERSHIP, 13 => IP_DROP_MEMBERSHIP,
                        14 => IP_MTU_DISCOVER >= 0 ? IP_MTU_DISCOVER : IP_DONTFRAG,
                        15 => IP_ADD_SOURCE_MEMBERSHIP, 16 => IP_DROP_SOURCE_MEMBERSHIP, 17 => IP_BLOCK_SOURCE, 18 => IP_UNBLOCK_SOURCE,
                        19 => IP_PKTINFO, _ => -1,
                    };
                    break;
                case PalSOL_IPV6:
                    optLevel = IPPROTO_IPV6;
                    optName = name switch
                    {
                        21 => IPV6_HOPLIMIT, 27 => IPV6_V6ONLY, 19 => IPV6_RECVPKTINFO, 9 => IPV6_MULTICAST_IF, 11 => IPV6_MULTICAST_LOOP,
                        10 => IPV6_MULTICAST_HOPS, 4 => IPV6_UNICAST_HOPS, _ => -1,
                    };
                    break;
                case PalSOL_TCP:
                    optLevel = IPPROTO_TCP;
                    optName = name switch { 1 => TCP_NODELAY, 16 => TCP_KEEPCNT, 3 => TCP_KEEPIDLE, 17 => TCP_KEEPINTVL, 15 => TCP_FASTOPEN, _ => -1 };
                    break;
                default:
                    // SOL_UDP has no option csharpc (or libSystem.Native) maps.
                    optLevel = 0;
                    return false;
            }
            return optName >= 0;
        }

        [RuntimeExport("SystemNative_GetSockOpt", Trigger + "SystemNative_GetSockOpt")]
        public static int GetSockOpt(nint socket, int socketOptionLevel, int socketOptionName, byte* optionValue, int* optionLen)
        {
            if (optionLen == null || *optionLen < 0) return Error_EFAULT;
            int fd = Fd(socket);
            if (socketOptionLevel == PalSOL_SOCKET && (socketOptionName == PalSO_EXCLUSIVEADDRUSE || socketOptionName == PalSO_REUSEADDR))
            {
                if (*optionLen != 4 || optionValue == null) return Error_EINVAL;
                uint length = 4;
                if (getsockopt(fd, SOL_SOCKET, SO_REUSEPORT, optionValue, &length) != 0) return PalErrno();
                int value = *(int*)optionValue == 0 ? 0 : 1;
                if (socketOptionName == PalSO_EXCLUSIVEADDRUSE) value = value == 0 ? 1 : 0;
                *(int*)optionValue = value;
                return Error_SUCCESS;
            }
#if CSHARPC_TARGET_MACOS
            if (socketOptionLevel == PalSOL_SOCKET && socketOptionName == PalSO_ACCEPTCONN)
            {
                if (*optionLen != 4 || optionValue == null) return Error_EINVAL;
                int family, type, protocol, listening;
                int r = GetSocketType(socket, &family, &type, &protocol, &listening);
                if (r != Error_SUCCESS) return PalErrno();
                *(int*)optionValue = listening;
                return Error_SUCCESS;
            }
#endif
            if (!TryGetPlatformSocketOption(socketOptionLevel, socketOptionName, out int optLevel, out int optName)) return Error_ENOTSUP;
            uint optLen = (uint)*optionLen;
            if (getsockopt(fd, optLevel, optName, optionValue, &optLen) != 0) return PalErrno();
#if !CSHARPC_TARGET_MACOS
            if (socketOptionLevel == PalSOL_IP && socketOptionName == PalSO_IP_DONTFRAGMENT && optionValue != null)
            {
                if (optLen >= 4) *(int*)optionValue = *(int*)optionValue == IP_PMTUDISC_DO ? 1 : 0;
                else if (optLen >= 1) *optionValue = (byte)(*optionValue == IP_PMTUDISC_DO ? 1 : 0);
            }
#endif
            if (socketOptionLevel == PalSOL_SOCKET && socketOptionName == PalSO_TYPE)
            {
                if (optLen != 4 || *optionLen < 4 || !TrySocketTypePlatformToPal(*(int*)optionValue, out int palType)) return Error_ENOTSUP;
                *(int*)optionValue = palType;
                optLen = 4;
            }
            *optionLen = (int)Math.Min(optLen, (uint)*optionLen);
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_GetRawSockOpt", Trigger + "SystemNative_GetRawSockOpt")]
        public static int GetRawSockOpt(nint socket, int socketOptionLevel, int socketOptionName, byte* optionValue, int* optionLen)
        {
            if (optionLen == null || *optionLen < 0) return Error_EFAULT;
            uint optLen = (uint)*optionLen;
            if (getsockopt(Fd(socket), socketOptionLevel, socketOptionName, optionValue, &optLen) != 0) return PalErrno();
            *optionLen = (int)Math.Min(optLen, (uint)*optionLen);
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_SetSockOpt", Trigger + "SystemNative_SetSockOpt")]
        public static int SetSockOpt(nint socket, int socketOptionLevel, int socketOptionName, byte* optionValue, int optionLen)
        {
            if (optionLen < 0 || optionValue == null) return Error_EFAULT;
            int fd = Fd(socket);
            if (socketOptionLevel == PalSOL_SOCKET && (socketOptionName == PalSO_EXCLUSIVEADDRUSE || socketOptionName == PalSO_REUSEADDR))
            {
                if (optionLen != 4) return Error_EINVAL;
                int value = *(int*)optionValue;
                if (socketOptionName == PalSO_EXCLUSIVEADDRUSE)
                {
                    if (value != 0 && value != 1) return Error_EINVAL;
                    value = value == 0 ? 1 : 0;
                }
                int err = setsockopt(fd, SOL_SOCKET, SO_REUSEPORT, &value, (uint)optionLen);
                if (err == 0) err = setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &value, (uint)optionLen);
                return err == 0 ? Error_SUCCESS : PalErrno();
            }
#if !CSHARPC_TARGET_MACOS
            if (socketOptionLevel == PalSOL_IP && socketOptionName == PalSO_IP_DONTFRAGMENT)
            {
                // pal_networking.c translates the caller's buffer in place (DontFragment true/false to IP_PMTUDISC_DO/DONT).
                if (optionLen >= 4) *(int*)optionValue = *(int*)optionValue != 0 ? IP_PMTUDISC_DO : IP_PMTUDISC_DONT;
                else if (optionLen >= 1) *optionValue = (byte)(*optionValue != 0 ? IP_PMTUDISC_DO : IP_PMTUDISC_DONT);
            }
#endif
            if (!TryGetPlatformSocketOption(socketOptionLevel, socketOptionName, out int optLevel, out int optName)) return Error_ENOTSUP;
            return setsockopt(fd, optLevel, optName, optionValue, (uint)optionLen) == 0 ? Error_SUCCESS : PalErrno();
        }

        [RuntimeExport("SystemNative_SetRawSockOpt", Trigger + "SystemNative_SetRawSockOpt")]
        public static int SetRawSockOpt(nint socket, int socketOptionLevel, int socketOptionName, byte* optionValue, int optionLen)
        {
            if (optionLen < 0 || optionValue == null) return Error_EFAULT;
            return setsockopt(Fd(socket), socketOptionLevel, socketOptionName, optionValue, (uint)optionLen) == 0 ? Error_SUCCESS : PalErrno();
        }

        // LingerOption: OnOff, Seconds.
        [RuntimeExport("SystemNative_GetLingerOption", Trigger + "SystemNative_GetLingerOption")]
        public static int GetLingerOption(nint socket, int* option)
        {
            if (option == null) return Error_EFAULT;
            int* opt = stackalloc int[2];
            uint length = 8;
            if (getsockopt(Fd(socket), SOL_SOCKET, LingerOptionName, opt, &length) != 0) return PalErrno();
            option[0] = opt[0];
            option[1] = opt[1];
            return Error_SUCCESS;
        }

        private static int MaxLingerTime()
        {
#if CSHARPC_TARGET_MACOS
            const int _SC_CLK_TCK = 3;
            return (int)(32767 / sysconf(_SC_CLK_TCK));
#else
            return 65535;
#endif
        }

        [RuntimeExport("SystemNative_SetLingerOption", Trigger + "SystemNative_SetLingerOption")]
        public static int SetLingerOption(nint socket, int* option)
        {
            if (option == null) return Error_EFAULT;
            if (option[0] != 0 && (option[1] < 0 || option[1] > MaxLingerTime())) return Error_EINVAL;
            int* opt = stackalloc int[2];
            opt[0] = option[0];
            opt[1] = option[1];
            int err = setsockopt(Fd(socket), SOL_SOCKET, LingerOptionName, opt, 8);
#if CSHARPC_TARGET_MACOS
            // macOS reports EINVAL for a linger set on a socket the peer already reset; .NET ignores it there.
            if (err != 0 && Errno == EINVAL) err = 0;
#endif
            return err == 0 ? Error_SUCCESS : PalErrno();
        }

        private static int SetTimeoutOption(int fd, int milliseconds, int optionName)
        {
            if (milliseconds < 0) return Error_EINVAL;
            long* timeout = stackalloc long[2];
            timeout[0] = milliseconds / 1000;
            timeout[1] = milliseconds % 1000 * 1000;
            return setsockopt(fd, SOL_SOCKET, optionName, timeout, 16) == 0 ? Error_SUCCESS : PalErrno();
        }

        [RuntimeExport("SystemNative_SetReceiveTimeout", Trigger + "SystemNative_SetReceiveTimeout")]
        public static int SetReceiveTimeout(nint socket, int millisecondsTimeout) => SetTimeoutOption(Fd(socket), millisecondsTimeout, SO_RCVTIMEO);

        [RuntimeExport("SystemNative_SetSendTimeout", Trigger + "SystemNative_SetSendTimeout")]
        public static int SetSendTimeout(nint socket, int millisecondsTimeout) => SetTimeoutOption(Fd(socket), millisecondsTimeout, SO_SNDTIMEO);

        private static bool TryMulticastOptionName(int option, bool ipv6, out int name)
        {
            switch (option)
            {
                case 0: name = ipv6 ? IPV6_ADD_MEMBERSHIP : IP_ADD_MEMBERSHIP; return true;
                case 1: name = ipv6 ? IPV6_DROP_MEMBERSHIP : IP_DROP_MEMBERSHIP; return true;
                case 2: name = ipv6 ? IPV6_MULTICAST_IF : IP_MULTICAST_IF; return true;
                default: name = 0; return false;
            }
        }

        // IPv4MulticastOption: MulticastAddress, LocalAddress, InterfaceIndex, Padding (16 bytes).
        [RuntimeExport("SystemNative_GetIPv4MulticastOption", Trigger + "SystemNative_GetIPv4MulticastOption")]
        public static int GetIPv4MulticastOption(nint socket, int multicastOption, uint* option)
        {
            if (option == null) return Error_EFAULT;
            if (!TryMulticastOptionName(multicastOption, false, out int name)) return Error_EINVAL;
            uint* opt = stackalloc uint[3];
            opt[0] = opt[1] = opt[2] = 0;
            uint length = IpMreqSize;
            if (getsockopt(Fd(socket), IPPROTO_IP, name, opt, &length) != 0) return PalErrno();
            option[0] = opt[0];
            option[1] = opt[1];
            option[2] = IpMreqSize == 12 ? opt[2] : 0;
            option[3] = 0;
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_SetIPv4MulticastOption", Trigger + "SystemNative_SetIPv4MulticastOption")]
        public static int SetIPv4MulticastOption(nint socket, int multicastOption, uint* option)
        {
            if (option == null) return Error_EFAULT;
            if (!TryMulticastOptionName(multicastOption, false, out int name)) return Error_EINVAL;
            int fd = Fd(socket);
            if (IP_MULTICAST_IFINDEX >= 0 && name == PalSO_IP_MULTICAST_IF)
            {
                // pal_networking.c compares the platform option name with the PAL constant 9, which is IP_MULTICAST_IF on macOS.
                uint index = option[2];
                return setsockopt(fd, IPPROTO_IP, IP_MULTICAST_IFINDEX, &index, 4) == 0 ? Error_SUCCESS : PalErrno();
            }
            uint* opt = stackalloc uint[3];
            opt[0] = option[0];
            opt[1] = option[1];
            opt[2] = option[2];
            if (IpMreqSize == 8 && option[2] != 0) return Error_ENOPROTOOPT;
            return setsockopt(fd, IPPROTO_IP, name, opt, IpMreqSize) == 0 ? Error_SUCCESS : PalErrno();
        }

        // IPv6MulticastOption: IPAddress (24 bytes), InterfaceIndex, Padding (32 bytes); struct ipv6_mreq is 16 address bytes and an index.
        [RuntimeExport("SystemNative_GetIPv6MulticastOption", Trigger + "SystemNative_GetIPv6MulticastOption")]
        public static int GetIPv6MulticastOption(nint socket, int multicastOption, byte* option)
        {
            if (option == null) return Error_EFAULT;
            if (!TryMulticastOptionName(multicastOption, true, out int name)) return Error_EINVAL;
            byte* opt = stackalloc byte[20];
            new Span<byte>(opt, 20).Clear();
            uint length = 20;
            if (getsockopt(Fd(socket), IPPROTO_IPV6, name, opt, &length) != 0) return PalErrno();
            Buffer.MemoryCopy(opt, option, 16, 16);
            *(int*)(option + 24) = *(int*)(opt + 16);
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_SetIPv6MulticastOption", Trigger + "SystemNative_SetIPv6MulticastOption")]
        public static int SetIPv6MulticastOption(nint socket, int multicastOption, byte* option)
        {
            if (option == null) return Error_EFAULT;
            if (!TryMulticastOptionName(multicastOption, true, out int name)) return Error_EINVAL;
            byte* opt = stackalloc byte[20];
            Buffer.MemoryCopy(option, opt, 16, 16);
            *(int*)(opt + 16) = *(int*)(option + 24);
            return setsockopt(Fd(socket), IPPROTO_IPV6, name, opt, 20) == 0 ? Error_SUCCESS : PalErrno();
        }

        // ---- data ---------------------------------------------------------------------------------------------------------------
        private static bool TryFlagsPalToPlatform(int pal, out int platform)
        {
            int supported = PalMSG_OOB | PalMSG_PEEK | PalMSG_DONTWAIT | PalMSG_DONTROUTE | PalMSG_TRUNC | PalMSG_CTRUNC | (MSG_ERRQUEUE != 0 ? PalMSG_ERRQUEUE : 0);
            platform = 0;
            if ((pal & ~supported) != 0) return false;
            if ((pal & PalMSG_ERRQUEUE) != 0) platform |= MSG_ERRQUEUE;
            if ((pal & PalMSG_OOB) != 0) platform |= MSG_OOB;
            if ((pal & PalMSG_PEEK) != 0) platform |= MSG_PEEK;
            if ((pal & PalMSG_DONTROUTE) != 0) platform |= MSG_DONTROUTE;
            if ((pal & PalMSG_DONTWAIT) != 0) platform |= MSG_DONTWAIT;
            if ((pal & PalMSG_TRUNC) != 0) platform |= MSG_TRUNC;
            if ((pal & PalMSG_CTRUNC) != 0) platform |= MSG_CTRUNC;
            return true;
        }

        private static int FlagsPlatformToPal(int platform) =>
            ((platform & MSG_OOB) != 0 ? PalMSG_OOB : 0) | ((platform & MSG_DONTROUTE) != 0 ? PalMSG_DONTROUTE : 0)
            | ((platform & MSG_TRUNC) != 0 ? PalMSG_TRUNC : 0) | ((platform & MSG_CTRUNC) != 0 ? PalMSG_CTRUNC : 0);

        [RuntimeExport("SystemNative_Receive", Trigger + "SystemNative_Receive")]
        public static int Receive(nint socket, byte* buffer, int bufferLen, int flags, int* received)
        {
            if (buffer == null || bufferLen < 0 || received == null) return Error_EFAULT;
            if (!TryFlagsPalToPlatform(flags, out int socketFlags)) return Error_ENOTSUP;
            nint res;
            while ((res = recv(Fd(socket), buffer, (nuint)bufferLen, socketFlags)) < 0 && Errno == EINTR) { }
            if (res != -1) { *received = (int)res; return Error_SUCCESS; }
            *received = 0;
            return PalErrno();
        }

        [RuntimeExport("SystemNative_Send", Trigger + "SystemNative_Send")]
        public static int Send(nint socket, byte* buffer, int bufferLen, int flags, int* sent)
        {
            if (buffer == null || bufferLen < 0 || sent == null) return Error_EFAULT;
            if (!TryFlagsPalToPlatform(flags, out int socketFlags)) return Error_ENOTSUP;
            nint res;
#if CSHARPC_TARGET_MACOS
            // EPROTOTYPE: macOS reports it for a send racing the peer's close; pal_networking.c retries it a few times.
            int maxProtoRetry = 4;
            while ((res = send(Fd(socket), buffer, (nuint)bufferLen, socketFlags)) < 0 && (Errno == EINTR || Errno == EPROTOTYPE && --maxProtoRetry > 0)) { }
#else
            while ((res = send(Fd(socket), buffer, (nuint)bufferLen, socketFlags)) < 0 && Errno == EINTR) { }
#endif
            if (res != -1) { *sent = (int)res; return Error_SUCCESS; }
            *sent = 0;
            return PalErrno();
        }

        // MessageHeader: SocketAddress, IOVectors, ControlBuffer, SocketAddressLen, IOVectorCount, ControlBufferLen, Flags (40 bytes).
        [StructLayout(LayoutKind.Sequential)]
        internal struct MessageHeader
        {
            public byte* SocketAddress;
            public nint* IOVectors;
            public byte* ControlBuffer;
            public int SocketAddressLen;
            public int IOVectorCount;
            public int ControlBufferLen;
            public int Flags;
        }

        private static bool IsStreamSocket(int fd)
        {
            int type;
            uint length = 4;
            return getsockopt(fd, SOL_SOCKET, SO_TYPE, &type, &length) == 0 && type == SOCK_STREAM;
        }

        /// <summary>ConvertMessageHeaderToMsghdr: the framework's header as a struct msghdr; a stream socket gets at most IOV_MAX vectors.</summary>
        private static void ToMsghdr(byte* header, MessageHeader* message, int fd)
        {
            new Span<byte>(header, MsghdrSize).Clear();
            int iovlen = message->IOVectorCount;
            if (iovlen > IOV_MAX_NET && fd >= 0 && IsStreamSocket(fd)) iovlen = IOV_MAX_NET;
            *(byte**)header = message->SocketAddress;
            *(uint*)(header + 8) = (uint)message->SocketAddressLen;
            *(nint**)(header + 16) = message->IOVectors;
            if (MsghdrSize == 56) *(nuint*)(header + MsgIovLenOffset) = (nuint)iovlen;
            else *(int*)(header + MsgIovLenOffset) = iovlen;
            *(byte**)(header + MsgControlOffset) = message->ControlBuffer;
            if (MsghdrSize == 56) *(nuint*)(header + MsgControlLenOffset) = (uint)message->ControlBufferLen;
            else *(uint*)(header + MsgControlLenOffset) = (uint)message->ControlBufferLen;
        }

        private static uint MsgNameLen(byte* header) => *(uint*)(header + 8);
        private static long MsgControlLen(byte* header) => MsghdrSize == 56 ? (long)*(nuint*)(header + MsgControlLenOffset) : *(uint*)(header + MsgControlLenOffset);

        [RuntimeExport("SystemNative_ReceiveMessage", Trigger + "SystemNative_ReceiveMessage")]
        public static int ReceiveMessage(nint socket, MessageHeader* messageHeader, int flags, long* received)
        {
            if (messageHeader == null || received == null || messageHeader->SocketAddressLen < 0 || messageHeader->ControlBufferLen < 0
                || messageHeader->IOVectorCount < 0)
                return Error_EFAULT;
            int fd = Fd(socket);
            if (!TryFlagsPalToPlatform(flags, out int socketFlags)) return Error_ENOTSUP;
            byte* header = stackalloc byte[MsghdrSize];
            ToMsghdr(header, messageHeader, fd);
            nint res;
            while ((res = recvmsg(fd, header, socketFlags)) < 0 && Errno == EINTR) { }
            messageHeader->SocketAddressLen = (int)Math.Min(MsgNameLen(header), (uint)messageHeader->SocketAddressLen);
            messageHeader->ControlBufferLen = (int)Math.Min(MsgControlLen(header), messageHeader->ControlBufferLen);
            messageHeader->Flags = FlagsPlatformToPal(*(int*)(header + MsgFlagsOffset));
            if (res != -1) { *received = res; return Error_SUCCESS; }
            *received = 0;
            return PalErrno();
        }

        [RuntimeExport("SystemNative_SendMessage", Trigger + "SystemNative_SendMessage")]
        public static int SendMessage(nint socket, MessageHeader* messageHeader, int flags, long* sent)
        {
            if (messageHeader == null || sent == null || messageHeader->SocketAddressLen < 0 || messageHeader->ControlBufferLen < 0
                || messageHeader->IOVectorCount < 0)
                return Error_EFAULT;
            int fd = Fd(socket);
            if (!TryFlagsPalToPlatform(flags, out int socketFlags)) return Error_ENOTSUP;
            byte* header = stackalloc byte[MsghdrSize];
            ToMsghdr(header, messageHeader, fd);
            nint res;
#if CSHARPC_TARGET_MACOS
            int maxProtoRetry = 4;
            while ((res = sendmsg(fd, header, socketFlags)) < 0 && (Errno == EINTR || Errno == EPROTOTYPE && --maxProtoRetry > 0)) { }
#else
            while ((res = sendmsg(fd, header, socketFlags)) < 0 && Errno == EINTR) { }
#endif
            if (res != -1) { *sent = res; return Error_SUCCESS; }
            *sent = 0;
            return PalErrno();
        }

        /// <summary>SystemNative_ReceiveSocketError: the ICMP error queue (Ping); not available on macOS (ENOTSUP), as there.</summary>
        [RuntimeExport("SystemNative_ReceiveSocketError", Trigger + "SystemNative_ReceiveSocketError")]
        public static int ReceiveSocketError(nint socket, MessageHeader* messageHeader)
        {
            if (messageHeader == null) return Error_EFAULT;
#if CSHARPC_TARGET_MACOS
            messageHeader->SocketAddressLen = 0;
            return ToPal(ENOTSUP);
#else
            int fd = Fd(socket);
            // struct sock_extended_err (16 bytes) followed by the offender's address (sockaddr_storage).
            const int ExtendedErrSize = 16, SO_EE_ORIGIN_ICMP = 2;
            byte* control = stackalloc byte[ExtendedErrSize + SockaddrStorageSize + CmsgHeaderSize];
            messageHeader->ControlBufferLen = ExtendedErrSize + SockaddrStorageSize;
            messageHeader->ControlBuffer = control;
            byte* header = stackalloc byte[MsghdrSize];
            ToMsghdr(header, messageHeader, fd);
            byte* icmp = stackalloc byte[8];
            nint* iov = stackalloc nint[2];
            if (*(nuint*)(header + MsgIovLenOffset) == 0 || *(nint**)(header + 16) == null)
            {
                iov[0] = (nint)icmp;
                iov[1] = 8;
                *(nint**)(header + 16) = iov;
                *(nuint*)(header + MsgIovLenOffset) = 1;
            }
            nint res;
            // pal_networking.c passes the PAL values (0x1000 | 0x2000) unconverted; on Linux 0x2000 is MSG_ERRQUEUE, which never blocks,
            // so the platform flags behave the same without handing the kernel an unrelated bit.
            while ((res = recvmsg(fd, header, MSG_DONTWAIT | MSG_ERRQUEUE)) < 0 && Errno == EINTR) { }
            for (byte* cmsg = FirstCmsg(header); cmsg != null; cmsg = NextCmsg(header, cmsg))
            {
                if (*(int*)(cmsg + 8) == IPPROTO_IP && *(int*)(cmsg + 12) == IP_RECVERR)
                {
                    byte* e = cmsg + CmsgHeaderSize;
                    if (e[4] == SO_EE_ORIGIN_ICMP)
                    {
                        int size = (int)(*(long*)cmsg - ExtendedErrSize);
                        int copy = Math.Max(0, Math.Min(Math.Min(size, messageHeader->SocketAddressLen), SockaddrStorageSize));
                        messageHeader->SocketAddressLen = copy;
                        if (copy > 0 && messageHeader->SocketAddress != null)
                            Buffer.MemoryCopy(e + ExtendedErrSize, messageHeader->SocketAddress, copy, copy);
                        return Error_SUCCESS;
                    }
                }
            }
            messageHeader->SocketAddressLen = 0;
            return res != -1 ? Error_SUCCESS : PalErrno();
#endif
        }

        // ---- control messages ---------------------------------------------------------------------------------------------------
        private static long CmsgAlignUp(long length) => (length + CmsgAlign - 1) & ~(long)(CmsgAlign - 1);
        private static int CmsgSpace(int length) => (int)(CmsgAlignUp(CmsgHeaderSize) + CmsgAlignUp(length));
        private static long CmsgLen(byte* cmsg) => CmsgHeaderSize == 16 ? *(long*)cmsg : *(uint*)cmsg;

        private static byte* FirstCmsg(byte* header)
        {
            long controlLength = MsgControlLen(header);
            return controlLength >= CmsgHeaderSize ? *(byte**)(header + MsgControlOffset) : null;
        }

        /// <summary>CMSG_NXTHDR (glibc's __cmsg_nxthdr, Darwin's macro): null when the next header or its data would leave the control buffer.</summary>
        private static byte* NextCmsg(byte* header, byte* cmsg)
        {
            byte* control = *(byte**)(header + MsgControlOffset);
            byte* end = control + MsgControlLen(header);
            long length = CmsgLen(cmsg);
            if (length < CmsgHeaderSize) return null;
            byte* next = cmsg + CmsgAlignUp(length);
            if (next + CmsgHeaderSize > end) return null;
            if (CmsgHeaderSize == 16 && next + CmsgAlignUp(CmsgLen(next)) > end) return null;
            return next;
        }

        [RuntimeExport("SystemNative_GetControlMessageBufferSize", Trigger + "SystemNative_GetControlMessageBufferSize")]
        public static int GetControlMessageBufferSize(int isIPv4, int isIPv6) =>
            (isIPv4 != 0 ? CmsgSpace(12 /* in_pktinfo */) : 0) + (isIPv6 != 0 ? CmsgSpace(20 /* in6_pktinfo */) : 0);

        // IPPacketInformation: IPAddress (24 bytes), InterfaceIndex, Padding.
        [RuntimeExport("SystemNative_TryGetIPPacketInformation", Trigger + "SystemNative_TryGetIPPacketInformation")]
        public static int TryGetIPPacketInformation(MessageHeader* messageHeader, int isIPv4, byte* packetInfo)
        {
            if (messageHeader == null || packetInfo == null) return 0;
            if (messageHeader->ControlBufferLen < 0) return 0;
            byte* header = stackalloc byte[MsghdrSize];
            ToMsghdr(header, messageHeader, -1);
            for (byte* cmsg = FirstCmsg(header); cmsg != null && CmsgLen(cmsg) > 0; cmsg = NextCmsg(header, cmsg))
            {
                int level = *(int*)(cmsg + CmsgHeaderSize - 8), type = *(int*)(cmsg + CmsgHeaderSize - 4);
                byte* data = cmsg + CmsgAlignUp(CmsgHeaderSize);
                if (isIPv4 != 0 && level == IPPROTO_IP && type == IP_PKTINFO)
                {
                    if (CmsgLen(cmsg) < 12) return 0;
                    *(uint*)packetInfo = *(uint*)(data + 8);      // ipi_addr
                    *(int*)(packetInfo + 24) = *(int*)data;      // ipi_ifindex
                    return 1;
                }
                if (isIPv4 == 0 && level == IPPROTO_IPV6 && type == IPV6_PKTINFO)
                {
                    if (CmsgLen(cmsg) < 20) return 0;
                    Buffer.MemoryCopy(data, packetInfo, 16, 16); // ipi6_addr
                    *(uint*)(packetInfo + 16) = 1;               // IsIPv6
                    *(int*)(packetInfo + 24) = *(int*)(data + 16);
                    return 1;
                }
            }
            return 0;
        }

        [RuntimeExport("SystemNative_PlatformSupportsDualModeIPv4PacketInfo", Trigger + "SystemNative_PlatformSupportsDualModeIPv4PacketInfo")]
        public static int PlatformSupportsDualModeIPv4PacketInfo() => DualModeIPv4PacketInfo;

        // ---- files to sockets -----------------------------------------------------------------------------------------------------
        [RuntimeExport("SystemNative_SendFile", Trigger + "SystemNative_SendFile")]
        public static int SendFile(nint outFd, nint inFd, long offset, long count, long* sent)
        {
            if (sent == null) return Error_EFAULT;
#if CSHARPC_TARGET_MACOS
            *sent = 0;
            while (true)
            {
                long length = count;
                int res = sendfile_bsd(Fd(inFd), Fd(outFd), offset, &length, null, 0);
                if (res != -1) { *sent += length; return Error_SUCCESS; }
                int error = Errno;
                if (error == EAGAIN || error == EINTR)
                {
                    *sent += length;
                    offset += length;
                    count -= length;
                    if (count == 0) return Error_SUCCESS;
                    if (error == EINTR) continue;
                }
                return ToPal(error);
            }
#else
            long position = offset;
            nint res;
            while ((res = sendfile(Fd(outFd), Fd(inFd), &position, (nuint)count)) < 0 && Errno == EINTR) { }
            if (res != -1) { *sent = res; return Error_SUCCESS; }
            *sent = 0;
            return PalErrno();
#endif
        }

        // ---- poll and select ------------------------------------------------------------------------------------------------------
        // PollEvent: FileDescriptor, short Events, short TriggeredEvents (8 bytes); struct pollfd has the same layout.
        private static short PollPalToPlatform(int pal) => (short)(((pal & PAL_POLLIN) != 0 ? POLLIN : 0) | ((pal & PAL_POLLPRI) != 0 ? POLLPRI : 0)
            | ((pal & PAL_POLLOUT) != 0 ? POLLOUT : 0) | ((pal & PAL_POLLERR) != 0 ? POLLERR : 0) | ((pal & PAL_POLLHUP) != 0 ? POLLHUP : 0)
            | ((pal & PAL_POLLNVAL) != 0 ? POLLNVAL : 0));

        private static short PollPlatformToPal(int platform) => (short)(((platform & POLLIN) != 0 ? PAL_POLLIN : 0) | ((platform & POLLPRI) != 0 ? PAL_POLLPRI : 0)
            | ((platform & POLLOUT) != 0 ? PAL_POLLOUT : 0) | ((platform & POLLERR) != 0 ? PAL_POLLERR : 0) | ((platform & POLLHUP) != 0 ? PAL_POLLHUP : 0)
            | ((platform & POLLNVAL) != 0 ? PAL_POLLNVAL : 0));

        private const short POLLPRI = 0x2, POLLNVAL = 0x20;

        /// <summary>SystemNative_Poll (pal_io_common.h's Common_Poll): the events converted into a pollfd array of exactly eventCount entries, poll retried on EINTR.</summary>
        [RuntimeExport("SystemNative_Poll", Trigger + "SystemNative_Poll")]
        public static int Poll(byte* pollEvents, uint eventCount, int milliseconds, uint* triggered)
        {
            if (pollEvents == null || triggered == null) return Error_EFAULT;
            if (milliseconds < -1) return Error_EINVAL;
            if (eventCount > int.MaxValue / 8) return Error_ENOMEM;
            byte* stackBuffer = stackalloc byte[256 * 8];
            byte* fds = eventCount <= 256 ? stackBuffer : (byte*)calloc(eventCount, 8);
            if (fds == null) return Error_ENOMEM;
            for (uint i = 0; i < eventCount; i++)
            {
                *(int*)(fds + i * 8) = *(int*)(pollEvents + i * 8);
                *(short*)(fds + i * 8 + 4) = PollPalToPlatform(*(short*)(pollEvents + i * 8 + 4));
                *(short*)(fds + i * 8 + 6) = 0;
            }
            int rv;
            while ((rv = poll(fds, eventCount, milliseconds)) < 0 && Errno == EINTR) { }
            if (rv < 0)
            {
                int error = Errno;
                if (eventCount > 256) free(fds);
                *triggered = 0;
                return ToPal(error);
            }
            for (uint i = 0; i < eventCount; i++) *(short*)(pollEvents + i * 8 + 6) = PollPlatformToPal(*(short*)(fds + i * 8 + 6));
            *triggered = (uint)rv;
            if (eventCount > 256) free(fds);
            return Error_SUCCESS;
        }

        /// <summary>SystemNative_Select: ENOTSUP on Linux, as there (the framework uses Poll); macOS selects over fd_sets sized by maxFd (_DARWIN_UNLIMITED_SELECT).</summary>
        [RuntimeExport("SystemNative_Select", Trigger + "SystemNative_Select")]
        public static int Select(int* readFds, int readFdsCount, int* writeFds, int writeFdsCount, int* errorFds, int errorFdsCount, int microseconds, int maxFd, int* triggered)
        {
#if CSHARPC_TARGET_MACOS
            if (triggered == null || maxFd < 0 || maxFd > int.MaxValue - 1 || readFdsCount < 0 || writeFdsCount < 0 || errorFdsCount < 0) return Error_EINVAL;
            // 32-bit words, at least FD_SETSIZE (1024) bits, as the system's fd_set.
            nuint words = (nuint)Math.Max(1024, maxFd + 1 + 31) / 32;
            uint* readSet = readFdsCount == 0 ? null : (uint*)calloc(words, 4);
            uint* writeSet = writeFdsCount == 0 ? null : (uint*)calloc(words, 4);
            uint* errorSet = errorFdsCount == 0 ? null : (uint*)calloc(words, 4);
            if (readFdsCount != 0 && readSet == null || writeFdsCount != 0 && writeSet == null || errorFdsCount != 0 && errorSet == null)
            {
                free(readSet); free(writeSet); free(errorSet);
                return Error_ENOMEM;
            }
            bool Set(uint* set, int fd)
            {
                if ((uint)fd > (uint)maxFd) return false;
                set[fd / 32] |= 1u << (fd % 32);
                return true;
            }
            bool ok = true;
            for (int i = 0; i < readFdsCount; i++) ok &= Set(readSet, readFds[i]);
            for (int i = 0; i < writeFdsCount; i++) ok &= Set(writeSet, writeFds[i]);
            for (int i = 0; i < errorFdsCount; i++) ok &= Set(errorSet, errorFds[i]);
            if (!ok) { free(readSet); free(writeSet); free(errorSet); return Error_EINVAL; }
            long* timeout = stackalloc long[2];
            timeout[0] = microseconds / 1000000;
            timeout[1] = microseconds % 1000000;
            *triggered = select(maxFd + 1, readSet, writeSet, errorSet, microseconds < 0 ? null : timeout);
            int result = Error_SUCCESS;
            if (*triggered < 0) result = PalErrno();
            else
            {
                for (int i = 0; i < readFdsCount; i++) readFds[i] = (int)(readSet[readFds[i] / 32] >> (readFds[i] % 32)) & 1;
                for (int i = 0; i < writeFdsCount; i++) writeFds[i] = (int)(writeSet[writeFds[i] / 32] >> (writeFds[i] % 32)) & 1;
                for (int i = 0; i < errorFdsCount; i++) errorFds[i] = (int)(errorSet[errorFds[i] / 32] >> (errorFds[i] % 32)) & 1;
            }
            free(readSet); free(writeSet); free(errorSet);
            return result;
#else
            return ToPal(ENOTSUP);
#endif
        }

        // ---- descriptor flags (pal_io.c) --------------------------------------------------------------------------------------------
        [RuntimeExport("SystemNative_FcntlGetFD", Trigger + "SystemNative_FcntlGetFD")]
        public static int FcntlGetFD(nint fd) => fcntl_int(Fd(fd), F_GETFD, 0);

        [RuntimeExport("SystemNative_FcntlSetIsNonBlocking", Trigger + "SystemNative_FcntlSetIsNonBlocking")]
        public static int FcntlSetIsNonBlocking(nint fd, int isNonBlocking)
        {
            int descriptor = Fd(fd);
            int flags = fcntl_int(descriptor, F_GETFL, 0);
            if (flags == -1) return -1;
            flags = isNonBlocking == 0 ? flags & ~O_NONBLOCK : flags | O_NONBLOCK;
            return fcntl_int(descriptor, F_SETFL, flags);
        }

        // ---- the socket event port: epoll on Linux, kqueue on macOS ------------------------------------------------------------------
        [RuntimeExport("SystemNative_CreateSocketEventPort", Trigger + "SystemNative_CreateSocketEventPort")]
        public static int CreateSocketEventPort(nint* port)
        {
            if (port == null) return Error_EFAULT;
#if CSHARPC_TARGET_MACOS
            int fd = kqueue();
            if (fd != -1) fcntl_int(fd, F_SETFD, FD_CLOEXEC); // kqueues are not inherited by fork; the flag is defensive
#else
            int fd = epoll_create1(EPOLL_CLOEXEC);
#endif
            *port = fd;
            return fd == -1 ? PalErrno() : Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_CloseSocketEventPort", Trigger + "SystemNative_CloseSocketEventPort")]
        public static int CloseSocketEventPort(nint port)
        {
            int err = close(Fd(port));
            return err == 0 || err < 0 && Errno == EINTR ? Error_SUCCESS : PalErrno();
        }

        [RuntimeExport("SystemNative_CreateSocketEventBuffer", Trigger + "SystemNative_CreateSocketEventBuffer")]
        public static int CreateSocketEventBuffer(int count, void** buffer)
        {
            if (buffer == null || count < 0) return Error_EFAULT;
            if ((long)count * SocketEventElementSize > int.MaxValue) return Error_ENOMEM;
            *buffer = malloc((nuint)((long)count * SocketEventElementSize));
            return *buffer == null ? Error_ENOMEM : Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_FreeSocketEventBuffer", Trigger + "SystemNative_FreeSocketEventBuffer")]
        public static int FreeSocketEventBuffer(void* buffer)
        {
            free(buffer);
            return Error_SUCCESS;
        }

        [RuntimeExport("SystemNative_TryChangeSocketEventRegistration", Trigger + "SystemNative_TryChangeSocketEventRegistration")]
        public static int TryChangeSocketEventRegistration(nint port, nint socket, int currentEvents, int newEvents, nuint data)
        {
            const int Supported = SA_READ | SA_WRITE | SA_READCLOSE | SA_CLOSE | SA_ERROR;
            if ((currentEvents & ~Supported) != 0 || (newEvents & ~Supported) != 0) return Error_EINVAL;
            if (currentEvents == newEvents) return Error_SUCCESS;
            int portFd = Fd(port), fd = Fd(socket);
#if CSHARPC_TARGET_MACOS
            int changes = currentEvents ^ newEvents;
            byte* events = stackalloc byte[2 * KeventSize];
            new Span<byte>(events, 2 * KeventSize).Clear();
            int n = 0;
            void SetEvent(byte* e, short filter, ushort flags)
            {
                *(nuint*)e = (nuint)fd;
                *(short*)(e + 8) = filter;
                *(ushort*)(e + 10) = flags;
                *(nuint*)(e + 24) = data;
            }
            if ((changes & SA_READ) != 0) SetEvent(events + KeventSize * n++, EVFILT_READ, (newEvents & SA_READ) == 0 ? EV_DELETE : (ushort)(EV_ADD | EV_CLEAR));
            if ((changes & SA_WRITE) != 0) SetEvent(events + KeventSize * n++, EVFILT_WRITE, (newEvents & SA_WRITE) == 0 ? EV_DELETE : (ushort)(EV_ADD | EV_CLEAR));
            int err;
            while ((err = kevent(portFd, events, n, null, 0, null)) < 0 && Errno == EINTR) { }
            return err == 0 ? Error_SUCCESS : PalErrno();
#else
            int op = currentEvents == 0 ? EPOLL_CTL_ADD : newEvents == 0 ? EPOLL_CTL_DEL : EPOLL_CTL_MOD;
            byte* evt = stackalloc byte[16];
            new Span<byte>(evt, 16).Clear();
            uint epollEvents = ((newEvents & SA_READ) != 0 ? EPOLLIN : 0) | ((newEvents & SA_WRITE) != 0 ? EPOLLOUT : 0)
                | ((newEvents & SA_READCLOSE) != 0 ? EPOLLRDHUP : 0) | ((newEvents & SA_CLOSE) != 0 ? EPOLLHUP : 0)
                | ((newEvents & SA_ERROR) != 0 ? EPOLLERR : 0);
            *(uint*)evt = epollEvents | EPOLLET;
            *(nuint*)(evt + EpollDataOffset) = data;
            return epoll_ctl(portFd, op, fd, evt) == 0 ? Error_SUCCESS : PalErrno();
#endif
        }

        /// <summary>
        /// SystemNative_WaitForSocketEvents: one blocking epoll_wait/kevent for at most *count events into the buffer CreateSocketEventBuffer
        /// sized (count elements of the larger of the two layouts), converted in place to SocketEvent (pointer-size data, events, padding).
        /// </summary>
        [RuntimeExport("SystemNative_WaitForSocketEvents", Trigger + "SystemNative_WaitForSocketEvents")]
        public static int WaitForSocketEvents(nint port, byte* buffer, int* count)
        {
            if (buffer == null || count == null || *count < 0) return Error_EFAULT;
            int fd = Fd(port);
            int numEvents;
#if CSHARPC_TARGET_MACOS
            while ((numEvents = kevent(fd, null, 0, buffer, *count, null)) < 0 && Errno == EINTR) { }
            if (numEvents == -1) { *count = -1; return PalErrno(); }
            for (int i = 0; i < numEvents; i++)
            {
                byte* e = buffer + i * KeventSize;
                short filter = *(short*)(e + 8);
                ushort flags = *(ushort*)(e + 10);
                nuint data = *(nuint*)(e + 24);
                int events = filter == EVFILT_READ ? SA_READ | ((flags & EV_EOF) != 0 ? SA_READCLOSE : 0)
                    : filter == EVFILT_WRITE ? SA_WRITE | ((flags & EV_EOF) != 0 ? SA_READ : 0) : 0;
                if (events != 0 && (flags & EV_ERROR) != 0) events |= SA_ERROR;
                // SocketEvent (16 bytes) fits in the first half of the same 32-byte kevent slot.
                *(nuint*)(buffer + i * 16) = data;
                *(int*)(buffer + i * 16 + 8) = events;
                *(int*)(buffer + i * 16 + 12) = 0;
            }
#else
            while ((numEvents = epoll_wait(fd, buffer, *count, -1)) < 0 && Errno == EINTR) { }
            if (numEvents == -1) { *count = 0; return PalErrno(); }
            // epoll_event can be smaller than SocketEvent (12 bytes on x86-64): convert from the last one backwards so no event is
            // overwritten before it is read.
            for (int i = numEvents - 1; i >= 0; i--)
            {
                byte* e = buffer + i * EpollEventSize;
                uint events = *(uint*)e;
                nuint data = *(nuint*)(e + EpollDataOffset);
                if ((events & EPOLLHUP) != 0) events = (events & ~EPOLLHUP) | EPOLLIN | EPOLLOUT;
                int socketEvents = ((events & EPOLLIN) != 0 ? SA_READ : 0) | ((events & EPOLLOUT) != 0 ? SA_WRITE : 0)
                    | ((events & EPOLLRDHUP) != 0 ? SA_READCLOSE : 0) | ((events & EPOLLHUP) != 0 ? SA_CLOSE : 0) | ((events & EPOLLERR) != 0 ? SA_ERROR : 0);
                *(nuint*)(buffer + i * 16) = data;
                *(int*)(buffer + i * 16 + 8) = socketEvents;
                *(int*)(buffer + i * 16 + 12) = 0;
            }
#endif
            *count = numEvents;
            return Error_SUCCESS;
        }
#endif
    }
}
#endif
