// Sockets over the OS's BSD socket API: libc on Linux and macOS, Winsock (ws2_32) on Windows.
// Constants, struct sockaddr layouts and error numbers are per OS; the compiler parses the corelib
// with CSHARPC_TARGET_LINUX/MACOS/WINDOWS from the target triple, never the host's. WASI (wasm32) has no
// sockets in preview 1: the operations throw PlatformNotSupportedException there.
#nullable disable
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class NetNative
    {
        public const int SOCK_STREAM = 1, SOCK_DGRAM = 2;
        public const int IPPROTO_TCP = 6, IPPROTO_UDP = 17, IPPROTO_IPV6 = 41, TCP_NODELAY = 1;
        public const int SHUT_RD = 0, SHUT_WR = 1, SHUT_RDWR = 2;
        public const int AF_INET = 2;
#if CSHARPC_TARGET_WINDOWS
        public const int AF_INET6 = 23;
        public const int SOL_SOCKET = 0xFFFF, SO_REUSEADDR = 4, SO_RCVTIMEO = 0x1006, SO_SNDTIMEO = 0x1005, SO_ERROR = 0x1007;
        public const int IPV6_V6ONLY = 27;
        public const int SendFlags = 0;
#elif CSHARPC_TARGET_MACOS
        public const int AF_INET6 = 30;
        public const int SOL_SOCKET = 0xFFFF, SO_REUSEADDR = 4, SO_RCVTIMEO = 0x1006, SO_SNDTIMEO = 0x1005, SO_ERROR = 0x1007;
        public const int SO_NOSIGPIPE = 0x1022;
        public const int IPV6_V6ONLY = 27;
        public const int SendFlags = 0;  // SIGPIPE is suppressed per socket (SO_NOSIGPIPE)
#else
        public const int AF_INET6 = 10;
        public const int SOL_SOCKET = 1, SO_REUSEADDR = 2, SO_RCVTIMEO = 20, SO_SNDTIMEO = 21, SO_ERROR = 4;
        public const int IPV6_V6ONLY = 26;
        public const int SendFlags = 0x4000;  // MSG_NOSIGNAL: a peer that went away is an error, not SIGPIPE
#endif

#if CSHARPC_TARGET_WINDOWS
        public const long InvalidSocket = -1;  // INVALID_SOCKET (~0) as a signed handle
        [DllImport("ws2_32")] private static extern int WSAStartup(ushort version, byte* data);
        [DllImport("ws2_32")] public static extern int WSAGetLastError();
        [DllImport("ws2_32")] private static extern nint socket(int family, int type, int protocol);
        [DllImport("ws2_32")] private static extern int closesocket(nint s);
        [DllImport("ws2_32")] private static extern int bind(nint s, byte* address, int length);
        [DllImport("ws2_32")] private static extern int listen(nint s, int backlog);
        [DllImport("ws2_32")] private static extern nint accept(nint s, byte* address, int* length);
        [DllImport("ws2_32")] private static extern int connect(nint s, byte* address, int length);
        [DllImport("ws2_32")] private static extern int send(nint s, byte* buffer, int length, int flags);
        [DllImport("ws2_32")] private static extern int recv(nint s, byte* buffer, int length, int flags);
        [DllImport("ws2_32")] private static extern int sendto(nint s, byte* buffer, int length, int flags, byte* address, int addressLength);
        [DllImport("ws2_32")] private static extern int recvfrom(nint s, byte* buffer, int length, int flags, byte* address, int* addressLength);
        [DllImport("ws2_32")] private static extern int shutdown(nint s, int how);
        [DllImport("ws2_32")] private static extern int setsockopt(nint s, int level, int name, byte* value, int length);
        [DllImport("ws2_32")] private static extern int getsockopt(nint s, int level, int name, byte* value, int* length);
        [DllImport("ws2_32")] private static extern int getsockname(nint s, byte* address, int* length);
        [DllImport("ws2_32")] private static extern int getpeername(nint s, byte* address, int* length);
        [DllImport("ws2_32")] private static extern int getaddrinfo(byte* node, byte* service, byte* hints, byte** result);
        [DllImport("ws2_32")] private static extern void freeaddrinfo(byte* info);
        [DllImport("ws2_32")] private static extern int gethostname(byte* name, int length);

        private static bool s_started;
        private static readonly object s_startLock = new object();
        private static void Startup()
        {
            if (s_started) return;
            lock (s_startLock)
            {
                if (s_started) return;
                byte* data = stackalloc byte[512];  // WSADATA (408 bytes on x64)
                WSAStartup(0x0202, data);
                s_started = true;
            }
        }
        private static int LastError() => WSAGetLastError();
#elif CSHARPC_TARGET_WASI
        // WASI preview 1 can't create, bind, connect or resolve sockets: every socket operation throws
        // PlatformNotSupportedException, as unsupported platform features do in .NET.
        public const long InvalidSocket = -1;
        private static Exception NotSupported() => new PlatformNotSupportedException();
        private static int socket(int family, int type, int protocol) => throw NotSupported();
        private static int closesocket(int s) => throw NotSupported();
        private static int bind(int s, byte* address, int length) => throw NotSupported();
        private static int listen(int s, int backlog) => throw NotSupported();
        private static int accept(int s, byte* address, int* length) => throw NotSupported();
        private static int connect(int s, byte* address, int length) => throw NotSupported();
        private static nint send(int s, byte* buffer, nuint length, int flags) => throw NotSupported();
        private static nint recv(int s, byte* buffer, nuint length, int flags) => throw NotSupported();
        private static nint sendto(int s, byte* buffer, nuint length, int flags, byte* address, int addressLength) => throw NotSupported();
        private static nint recvfrom(int s, byte* buffer, nuint length, int flags, byte* address, int* addressLength) => throw NotSupported();
        private static int shutdown(int s, int how) => throw NotSupported();
        private static int setsockopt(int s, int level, int name, byte* value, int length) => throw NotSupported();
        private static int getsockopt(int s, int level, int name, byte* value, int* length) => throw NotSupported();
        private static int getsockname(int s, byte* address, int* length) => throw NotSupported();
        private static int getpeername(int s, byte* address, int* length) => throw NotSupported();
        private static int getaddrinfo(byte* node, byte* service, byte* hints, byte** result) => throw NotSupported();
        private static void freeaddrinfo(byte* info) => throw NotSupported();
        private static int gethostname(byte* name, nuint length) => throw NotSupported();
        private static void Startup() => throw NotSupported();
        private static int LastError() => 0;
#else
        public const long InvalidSocket = -1;
        [DllImport("libc", SetLastError = true)] private static extern int socket(int family, int type, int protocol);
        [DllImport("libc", SetLastError = true, EntryPoint = "close")] private static extern int closesocket(int s);
        [DllImport("libc", SetLastError = true)] private static extern int bind(int s, byte* address, int length);
        [DllImport("libc", SetLastError = true)] private static extern int listen(int s, int backlog);
        [DllImport("libc", SetLastError = true)] private static extern int accept(int s, byte* address, int* length);
        [DllImport("libc", SetLastError = true)] private static extern int connect(int s, byte* address, int length);
        [DllImport("libc", SetLastError = true)] private static extern nint send(int s, byte* buffer, nuint length, int flags);
        [DllImport("libc", SetLastError = true)] private static extern nint recv(int s, byte* buffer, nuint length, int flags);
        [DllImport("libc", SetLastError = true)] private static extern nint sendto(int s, byte* buffer, nuint length, int flags, byte* address, int addressLength);
        [DllImport("libc", SetLastError = true)] private static extern nint recvfrom(int s, byte* buffer, nuint length, int flags, byte* address, int* addressLength);
        [DllImport("libc", SetLastError = true)] private static extern int shutdown(int s, int how);
        [DllImport("libc", SetLastError = true)] private static extern int setsockopt(int s, int level, int name, byte* value, int length);
        [DllImport("libc", SetLastError = true)] private static extern int getsockopt(int s, int level, int name, byte* value, int* length);
        [DllImport("libc", SetLastError = true)] private static extern int getsockname(int s, byte* address, int* length);
        [DllImport("libc", SetLastError = true)] private static extern int getpeername(int s, byte* address, int* length);
        [DllImport("libc")] private static extern int getaddrinfo(byte* node, byte* service, byte* hints, byte** result);
        [DllImport("libc")] private static extern void freeaddrinfo(byte* info);
        [DllImport("libc")] private static extern int gethostname(byte* name, nuint length);
        [DllImport("libc", SetLastError = true)] private static extern int fcntl(int fd, int command, int argument);
        [DllImport("libc", SetLastError = true)] private static extern int poll(long* fds, nuint count, int timeout);
#if CSHARPC_TARGET_LINUX
        [DllImport("libc", SetLastError = true)] private static extern int accept4(int s, byte* address, int* length, int flags);
#endif
        private static void Startup() { }
        private static int LastError() => Marshal.GetLastPInvokeError();
#endif

        // ---- non-blocking operations (corelib/Net/SocketEngine.cs): the result, or -1 and the error ----
        public const int MSG_PEEK = 2;
#if CSHARPC_TARGET_MACOS
        public const int EAGAIN = 35, EINPROGRESS = 36;
        public const int SO_LINGER = 0x1080; // SO_LINGER_SEC: the timeout in seconds, as .NET sets it
#elif CSHARPC_TARGET_WINDOWS
        public const int EAGAIN = 10035, EINPROGRESS = 10036;
        public const int SO_LINGER = 0x80;
#else
        public const int EAGAIN = 11, EINPROGRESS = 115;
        public const int SO_LINGER = 13;
#endif

        public static bool IsWouldBlock(int error) => error == EAGAIN;

        public static int TryReceive(long s, byte* buffer, int count, int flags, out int error)
        {
            while (true)
            {
                long n = recv(H(s), buffer, Count(count), flags);
                if (n >= 0) { error = 0; return (int)n; }
                error = LastError();
                if (!IsInterrupted(error)) return -1;
            }
        }

        public static int TrySend(long s, byte* buffer, int count, out int error)
        {
            while (true)
            {
                long n = send(H(s), buffer, Count(count), SendFlags);
                if (n >= 0) { error = 0; return (int)n; }
                error = LastError();
                if (!IsInterrupted(error)) return -1;
            }
        }

        /// <summary>accept() of a ready connection; the new socket is non-blocking where the engine is epoll.</summary>
        public static long TryAccept(long s, out int error)
        {
            while (true)
            {
#if CSHARPC_TARGET_LINUX
                long client = accept4(H(s), null, null, 0x800 /* SOCK_NONBLOCK */ | 0x80000 /* SOCK_CLOEXEC */);
#else
                long client = accept(H(s), null, null);
#endif
                if (client != InvalidSocket) { error = 0; return client; }
                error = LastError();
                if (!IsInterrupted(error)) return InvalidSocket;
            }
        }

#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
        public static void SetNonBlocking(long s)
        {
#if CSHARPC_TARGET_MACOS
            const int O_NONBLOCK = 0x4;
#else
            const int O_NONBLOCK = 0x800;
#endif
            int flags = fcntl(H(s), 3 /* F_GETFL */, 0);
            if (flags < 0 || fcntl(H(s), 4 /* F_SETFL */, flags | O_NONBLOCK) < 0) throw Error(LastError());
        }

        /// <summary>Waits until the socket is readable (or writable); false when the timeout (ms, -1: none) expires.</summary>
        public static bool Wait(long s, bool write, int timeout)
        {
            // struct pollfd { int fd; short events; short revents; }: POLLIN 1, POLLOUT 4.
            long pollfd = (uint)H(s) | ((long)(write ? 4 : 1) << 32);
            while (true)
            {
                int n = poll(&pollfd, 1, timeout);
                if (n > 0) return true;
                if (n == 0) return false;
                int error = LastError();
                if (!IsInterrupted(error)) throw Error(error);
            }
        }
#endif

        /// <summary>SO_LINGER: Close(0) resets the connection instead of a graceful FIN.</summary>
        public static void SetLinger(long s, bool enabled, int seconds)
        {
            int* linger = stackalloc int[2];
            linger[0] = enabled ? 1 : 0;
            linger[1] = seconds;
            if (setsockopt(H(s), SOL_SOCKET, SO_LINGER, (byte*)linger, 8) != 0) throw Error(LastError());
        }

        // ---- socket calls: return -1 (or a negative count) and leave the error in LastError -------
        public static long Socket(int family, int type, int protocol)
        {
            Startup();
            long s = socket(family, type, protocol);
            if (s == InvalidSocket) throw Error(LastError());
#if CSHARPC_TARGET_MACOS
            int one = 1;
            setsockopt((int)s, SOL_SOCKET, SO_NOSIGPIPE, (byte*)&one, 4);
#endif
            return s;
        }

        public static void Close(long s) { if (s != InvalidSocket) closesocket(H(s)); }

#if CSHARPC_TARGET_WINDOWS
        private static nint H(long s) => (nint)s;
#else
        private static int H(long s) => (int)s;
#endif

        public static void Bind(long s, EndPoint endPoint)
        {
            byte* address = stackalloc byte[SockaddrMax];
            int length = Encode(endPoint, address);
            if (bind(H(s), address, length) != 0) throw Error(LastError());
        }

        public static void Listen(long s, int backlog) { if (listen(H(s), backlog) != 0) throw Error(LastError()); }

        public static long Accept(long s)
        {
            while (true)
            {
                long client = accept(H(s), null, null);
                if (client != InvalidSocket) return client;
                int error = LastError();
                if (!IsInterrupted(error)) throw Error(error);
            }
        }

        /// <summary>connect(); .NET appends the endpoint to the message ("Connection refused 127.0.0.1:1").</summary>
        public static void Connect(long s, EndPoint endPoint)
        {
            byte* address = stackalloc byte[SockaddrMax];
            int length = Encode(endPoint, address);
            while (connect(H(s), address, length) != 0)
            {
                int error = LastError();
                if (!IsInterrupted(error)) throw Error(error, endPoint);
            }
        }

        /// <summary>send(); <paramref name="timed"/>: SO_SNDTIMEO is set, so EAGAIN means the timeout expired
        /// (.NET reports TimedOut).</summary>
        public static int Send(long s, byte* buffer, int count, bool timed)
        {
            while (true)
            {
                long n = send(H(s), buffer, Count(count), SendFlags);
                if (n >= 0) return (int)n;
                int error = LastError();
                if (!IsInterrupted(error)) throw TimeoutOr(error, timed);
            }
        }

        public static int Receive(long s, byte* buffer, int count, bool timed)
        {
            while (true)
            {
                long n = recv(H(s), buffer, Count(count), 0);
                if (n >= 0) return (int)n;
                int error = LastError();
                if (!IsInterrupted(error)) throw TimeoutOr(error, timed);
            }
        }

        private static SocketException TimeoutOr(int error, bool timed) =>
            timed && ToSocketError(error) == SocketError.WouldBlock
                ? (SocketException)(object)new SocketExceptionImpl(SocketError.TimedOut, TimedOutNative, (EndPoint)null)
                : Error(error);

        public static int SendTo(long s, byte* buffer, int count, EndPoint endPoint)
        {
            byte* address = stackalloc byte[SockaddrMax];
            int length = Encode(endPoint, address);
            long n = sendto(H(s), buffer, Count(count), SendFlags, address, length);
            if (n < 0) throw Error(LastError());
            return (int)n;
        }

        public static int ReceiveFrom(long s, byte* buffer, int count, out EndPoint from)
        {
            byte* address = stackalloc byte[SockaddrMax];
            int length = SockaddrMax;
            long n = recvfrom(H(s), buffer, Count(count), 0, address, &length);
            if (n < 0) throw Error(LastError());
            from = Decode(address);
            return (int)n;
        }

#if CSHARPC_TARGET_WINDOWS
        private static int Count(int count) => count;
#else
        private static nuint Count(int count) => (nuint)count;
#endif

        public static void Shutdown(long s, int how) { if (shutdown(H(s), how) != 0) throw Error(LastError()); }

        public static void SetInt(long s, int level, int name, int value)
        {
            if (setsockopt(H(s), level, name, (byte*)&value, 4) != 0) throw Error(LastError());
        }

        public static int GetInt(long s, int level, int name)
        {
            int value = 0, length = 4;
            if (getsockopt(H(s), level, name, (byte*)&value, &length) != 0) throw Error(LastError());
            return value;
        }

        /// <summary>SO_RCVTIMEO/SO_SNDTIMEO: milliseconds (DWORD) on Windows, struct timeval elsewhere.</summary>
        public static void SetTimeout(long s, int name, int milliseconds)
        {
#if CSHARPC_TARGET_WINDOWS
            SetInt(s, SOL_SOCKET, name, milliseconds);
#else
            long* tv = stackalloc long[2];
            tv[0] = milliseconds / 1000;
            tv[1] = (milliseconds % 1000) * 1000;
            if (setsockopt(H(s), SOL_SOCKET, name, (byte*)tv, 16) != 0) throw Error(LastError());
#endif
        }

        public static EndPoint LocalEndPoint(long s)
        {
            byte* address = stackalloc byte[SockaddrMax];
            int length = SockaddrMax;
            if (getsockname(H(s), address, &length) != 0) throw Error(LastError());
            return Decode(address);
        }

        public static EndPoint RemoteEndPoint(long s)
        {
            byte* address = stackalloc byte[SockaddrMax];
            int length = SockaddrMax;
            if (getpeername(H(s), address, &length) != 0) throw Error(LastError());
            return Decode(address);
        }

        public static string HostName()
        {
            byte* name = stackalloc byte[256];
            gethostname(name, 256);
            return Utf8.FromNative(name);
        }

        /// <summary>getaddrinfo for Dns: addresses in the resolver's order, IPv6 and IPv4.</summary>
        public static IPAddress[] Resolve(string host)
        {
            Startup();
            byte* node = Utf8.ToNative(host);
            byte* result = null;
            // hints: ai_socktype = SOCK_STREAM so each address is listed once (not per socket type).
            byte* hints = stackalloc byte[64];
            for (int i = 0; i < 64; i++) hints[i] = 0;
            *(int*)(hints + 8) = SOCK_STREAM;
            int error = getaddrinfo(node, null, hints, &result);
            Libc.free(node);
            if (error != 0)
                throw (SocketException)(object)new SocketExceptionImpl(SocketError.HostNotFound, error, GaiMessage(error));
            var list = new System.Collections.Generic.List<IPAddress>();
            // struct addrinfo: ai_family @4, ai_addr @24 (Linux) / @32 (macOS, Windows: ai_canonname comes first), ai_next @40.
            for (byte* ai = result; ai != null; ai = (byte*)*(nint*)(ai + 40))
            {
#if CSHARPC_TARGET_LINUX
                byte* addr = (byte*)*(nint*)(ai + 24);
#else
                byte* addr = (byte*)*(nint*)(ai + 32);
#endif
                if (addr == null) continue;
                var endPoint = Decode(addr) as IPEndPoint;
                if (endPoint != null && !Contains(list, endPoint.Address)) list.Add(endPoint.Address);
            }
            freeaddrinfo(result);
            return list.ToArray();
        }

#if CSHARPC_TARGET_WINDOWS
        private static string GaiMessage(int error) => WindowsMarshal.ErrorMessage(error);
#else
#if CSHARPC_TARGET_WASI
        private static string GaiMessage(int error) => throw NotSupported();
#else
        [DllImport("libc")] private static extern byte* gai_strerror(int error);
        private static string GaiMessage(int error) => Utf8.FromNative(gai_strerror(error));
#endif
#endif

        private static bool Contains(System.Collections.Generic.List<IPAddress> list, IPAddress address)
        {
            foreach (var a in list) if (a.Equals(address)) return true;
            return false;
        }

        // ---- struct sockaddr -------------------------------------------------------------------
        public const int SockaddrMax = 128;

        /// <summary>sockaddr_in (16 bytes) / sockaddr_in6 (28 bytes); macOS has a length byte before the family.</summary>
        public static int Encode(EndPoint endPoint, byte* address)
        {
            var ip = endPoint as IPEndPoint;
            if (ip == null) throw new NotSupportedException("csharpc: only IP endpoints are supported for sockets.");
            for (int i = 0; i < SockaddrMax; i++) address[i] = 0;
            bool v6 = ip.AddressFamily == AddressFamily.InterNetworkV6;
            int length = v6 ? 28 : 16;
            SetFamily(address, v6 ? AF_INET6 : AF_INET, length);
            address[2] = (byte)(ip.Port >> 8);
            address[3] = (byte)ip.Port;
            byte[] bytes = ip.Address.GetAddressBytes();
            if (v6)
            {
                for (int i = 0; i < 16; i++) address[8 + i] = bytes[i];
                *(uint*)(address + 24) = (uint)ip.Address.ScopeId;
            }
            else
                for (int i = 0; i < 4; i++) address[4 + i] = bytes[i];
            return length;
        }

        public static EndPoint Decode(byte* address)
        {
            int family = GetFamily(address);
            int port = (address[2] << 8) | address[3];
            if (family == AF_INET6)
            {
                byte[] bytes = new byte[16];
                for (int i = 0; i < 16; i++) bytes[i] = address[8 + i];
                return new IPEndPoint(new IPAddress(bytes, *(uint*)(address + 24)), port);
            }
            byte[] v4 = new byte[4];
            for (int i = 0; i < 4; i++) v4[i] = address[4 + i];
            return new IPEndPoint(new IPAddress(v4), port);
        }

#if CSHARPC_TARGET_MACOS
        private static void SetFamily(byte* address, int family, int length) { address[0] = (byte)length; address[1] = (byte)family; }
        private static int GetFamily(byte* address) => address[1];
#else
        private static void SetFamily(byte* address, int family, int length) => *(ushort*)address = (ushort)family;
        private static int GetFamily(byte* address) => *(ushort*)address;
#endif

        // ---- errors ---------------------------------------------------------------------------
        public static SocketException Error(int nativeError, EndPoint endPoint = null)
        {
            return (SocketException)(object)new SocketExceptionImpl(ToSocketError(nativeError), nativeError, endPoint);
        }

#if CSHARPC_TARGET_WINDOWS
        private const int TimedOutNative = 10060;
#elif CSHARPC_TARGET_MACOS
        private const int TimedOutNative = 60;
#else
        private const int TimedOutNative = 110;
#endif

#if CSHARPC_TARGET_WINDOWS
        // Winsock error numbers are SocketError's values.
        public static SocketError ToSocketError(int error) => (SocketError)error;
        private static bool IsInterrupted(int error) => false;
        public static string Message(int nativeError) => WindowsMarshal.ErrorMessage(nativeError);
#else
        private static bool IsInterrupted(int error) => error == 4;  // EINTR
        public static string Message(int nativeError) => Utf8.FromNative(Posix.strerror(nativeError));

        /// <summary>errno -> SocketError, as .NET's SocketErrorPal on Unix.</summary>
        public static SocketError ToSocketError(int error)
        {
            switch (error)
            {
                case 0: return SocketError.Success;
                case 4: return SocketError.Interrupted;
                case 9: return SocketError.OperationAborted;   // EBADF: the socket was closed
                case 13: return SocketError.AccessDenied;
                case 14: return SocketError.Fault;
                case 22: return SocketError.InvalidArgument;
                case 24: return SocketError.TooManyOpenSockets;
                case 32: return SocketError.Shutdown;          // EPIPE
#if CSHARPC_TARGET_MACOS
                case 35: return SocketError.WouldBlock;
                case 36: return SocketError.InProgress;
                case 37: return SocketError.AlreadyInProgress;
                case 38: return SocketError.NotSocket;
                case 39: return SocketError.DestinationAddressRequired;
                case 40: return SocketError.MessageSize;
                case 41: return SocketError.ProtocolType;
                case 42: return SocketError.ProtocolOption;
                case 43: return SocketError.ProtocolNotSupported;
                case 44: return SocketError.SocketNotSupported;
                case 45: return SocketError.OperationNotSupported;
                case 46: return SocketError.ProtocolFamilyNotSupported;
                case 47: return SocketError.AddressFamilyNotSupported;
                case 48: return SocketError.AddressAlreadyInUse;
                case 49: return SocketError.AddressNotAvailable;
                case 50: return SocketError.NetworkDown;
                case 51: return SocketError.NetworkUnreachable;
                case 52: return SocketError.NetworkReset;
                case 53: return SocketError.ConnectionAborted;
                case 54: return SocketError.ConnectionReset;
                case 55: return SocketError.NoBufferSpaceAvailable;
                case 56: return SocketError.IsConnected;
                case 57: return SocketError.NotConnected;
                case 58: return SocketError.Disconnecting;
                case 60: return SocketError.TimedOut;
                case 61: return SocketError.ConnectionRefused;
                case 64: return SocketError.HostDown;
                case 65: return SocketError.HostUnreachable;
#else
                case 11: return SocketError.WouldBlock;
                case 71: return SocketError.ProtocolNotSupported;
                case 88: return SocketError.NotSocket;
                case 89: return SocketError.DestinationAddressRequired;
                case 90: return SocketError.MessageSize;
                case 91: return SocketError.ProtocolType;
                case 92: return SocketError.ProtocolOption;
                case 93: return SocketError.ProtocolNotSupported;
                case 94: return SocketError.SocketNotSupported;
                case 95: return SocketError.OperationNotSupported;
                case 96: return SocketError.ProtocolFamilyNotSupported;
                case 97: return SocketError.AddressFamilyNotSupported;
                case 98: return SocketError.AddressAlreadyInUse;
                case 99: return SocketError.AddressNotAvailable;
                case 100: return SocketError.NetworkDown;
                case 101: return SocketError.NetworkUnreachable;
                case 102: return SocketError.NetworkReset;
                case 103: return SocketError.ConnectionAborted;
                case 104: return SocketError.ConnectionReset;
                case 105: return SocketError.NoBufferSpaceAvailable;
                case 106: return SocketError.IsConnected;
                case 107: return SocketError.NotConnected;
                case 108: return SocketError.Disconnecting;
                case 110: return SocketError.TimedOut;
                case 111: return SocketError.ConnectionRefused;
                case 112: return SocketError.HostDown;
                case 113: return SocketError.HostUnreachable;
                case 114: return SocketError.AlreadyInProgress;
                case 115: return SocketError.InProgress;
#endif
                default: return SocketError.SocketError;
            }
        }
#endif
    }
}
