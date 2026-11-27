// Sockets over Winsock (ws2_32): the Windows corelib's socket layer (#441).
// Windows only: on Linux and macOS these types are the framework's IL over the libSystem.Native socket contract
// (corelib/Net/SystemNative.Net.cs, docs/il-first/System.Net.Sockets.md); Windows keeps them until the IOCP engine exists.
#if CSHARPC_TARGET_WINDOWS
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
        public const int AF_INET6 = 23;
        public const int SOL_SOCKET = 0xFFFF, SO_REUSEADDR = 4, SO_RCVTIMEO = 0x1006, SO_SNDTIMEO = 0x1005, SO_ERROR = 0x1007;
        public const int IPV6_V6ONLY = 27;
        public const int SendFlags = 0;
        public const int SO_KEEPALIVE = 8, SO_SNDBUF = 0x1001, SO_RCVBUF = 0x1002, TCP_KEEPIDLE = 3, TCP_KEEPINTVL = 17, TCP_KEEPCNT = 16;

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

        // ---- non-blocking operations (corelib/Net/SocketEngine.cs): the result, or -1 and the error ----
        public const int MSG_PEEK = 2;
        public const int EAGAIN = 10035, EINPROGRESS = 10036;
        public const int SO_LINGER = 0x80;

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
                long client = accept(H(s), null, null);
                if (client != InvalidSocket) { error = 0; return client; }
                error = LastError();
                if (!IsInterrupted(error)) return InvalidSocket;
            }
        }

        // Winsock: Socket.Blocking through ioctlsocket(FIONBIO), and readiness through select(), as .NET's
        // Windows sockets do (Socket.Select and Poll are Winsock's select). WSAPoll isn't used: it doesn't
        // report a connect that failed (the exceptfds of select do).
        [DllImport("ws2_32")] private static extern int ioctlsocket(nint s, int command, uint* argument);
        [DllImport("ws2_32")] private static extern int select(int ignored, nint* readfds, nint* writefds, nint* exceptfds, int* timeout);

        // The poll(2) event bits csharpc's callers use (its own values here): readable, priority/error, writable.
        public const int POLLIN = 1, POLLPRI = 2, POLLOUT = 4, POLLERR = 8, POLLHUP = 16;

        /// <summary>Socket.Blocking: FIONBIO cleared (true) or set (false).</summary>
        public static void SetBlockingMode(long s, bool blocking)
        {
            uint mode = blocking ? 0u : 1u;
            if (ioctlsocket(H(s), unchecked((int)0x8004667E) /* FIONBIO */, &mode) != 0) throw Error(LastError());
        }

        /// <summary>Waits until the socket is readable (or writable, or its connect failed); false when the timeout (ms, -1: none) expires.</summary>
        public static bool Wait(long s, bool write, int timeout)
        {
            long handle = s;
            int events = write ? POLLOUT | POLLERR : POLLIN, revents;
            Poll(&handle, &events, &revents, 1, timeout);
            return revents != 0;
        }

        /// <summary>
        /// poll() over select(): POLLIN|POLLHUP asks for readfds (readable, a connection to accept, or closed),
        /// POLLOUT for writefds (writable, a connect done), POLLPRI|POLLERR for exceptfds (out-of-band data, a
        /// connect that failed). The fd_sets are Winsock's { count; SOCKET[count] } (no FD_SETSIZE limit).
        /// </summary>
        public static void Poll(long* handles, int* events, int* revents, int count, int timeout)
        {
            nint* read = stackalloc nint[count + 1], write = stackalloc nint[count + 1], except = stackalloc nint[count + 1];
            read[0] = write[0] = except[0] = 0;
            for (int i = 0; i < count; i++)
            {
                if ((events[i] & (POLLIN | POLLHUP)) != 0) read[1 + read[0]++] = H(handles[i]);
                if ((events[i] & POLLOUT) != 0) write[1 + write[0]++] = H(handles[i]);
                if ((events[i] & (POLLPRI | POLLERR)) != 0) except[1 + except[0]++] = H(handles[i]);
            }
            int* timeval = stackalloc int[2]; // struct timeval { long tv_sec; long tv_usec; }: 32-bit longs on Windows
            timeval[0] = timeout / 1000;
            timeval[1] = timeout % 1000 * 1000;
            int n = select(0, read[0] == 0 ? null : read, write[0] == 0 ? null : write, except[0] == 0 ? null : except, timeout < 0 ? null : timeval);
            if (n < 0) throw Error(LastError());
            for (int i = 0; i < count; i++)
            {
                nint h = H(handles[i]);
                int ready = 0;
                if (Contains(read, h)) ready |= POLLIN;
                if (Contains(write, h)) ready |= POLLOUT;
                if (Contains(except, h)) ready |= POLLPRI | POLLERR;
                revents[i] = ready & (events[i] | POLLERR);
            }
        }

        // select() leaves only the ready sockets in each set.
        private static bool Contains(nint* set, nint handle)
        {
            for (nint i = 0; i < set[0]; i++) if (set[1 + i] == handle) return true;
            return false;
        }

        /// <summary>connect() without waiting for it: 0, or the error (EINPROGRESS: under way on a non-blocking socket).</summary>
        public static int TryConnect(long s, EndPoint endPoint)
        {
            byte* address = stackalloc byte[SockaddrMax];
            int length = Encode(endPoint, address);
            while (connect(H(s), address, length) != 0)
            {
                int error = LastError();
                if (!IsInterrupted(error)) return error;
            }
            return 0;
        }

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
            return s;
        }

        public static void Close(long s) { if (s != InvalidSocket) closesocket(H(s)); }

        private static nint H(long s) => (nint)s;

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

        private static int Count(int count) => count;

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
            SetInt(s, SOL_SOCKET, name, milliseconds);
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
                byte* addr = (byte*)*(nint*)(ai + 32);
                if (addr == null) continue;
                var endPoint = Decode(addr) as IPEndPoint;
                if (endPoint != null && !Contains(list, endPoint.Address)) list.Add(endPoint.Address);
            }
            freeaddrinfo(result);
            return list.ToArray();
        }

        private static string GaiMessage(int error) => WindowsMarshal.ErrorMessage(error);

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

        private static void SetFamily(byte* address, int family, int length) => *(ushort*)address = (ushort)family;
        private static int GetFamily(byte* address) => *(ushort*)address;

        // ---- errors ---------------------------------------------------------------------------
        public static SocketException Error(int nativeError, EndPoint endPoint = null)
        {
            return (SocketException)(object)new SocketExceptionImpl(ToSocketError(nativeError), nativeError, endPoint);
        }

        private const int TimedOutNative = 10060;

        // Winsock error numbers are SocketError's values.
        public static SocketError ToSocketError(int error) => (SocketError)error;
        private static bool IsInterrupted(int error) => false;
        public static string Message(int nativeError) => WindowsMarshal.ErrorMessage(nativeError);

    }
}
#endif
