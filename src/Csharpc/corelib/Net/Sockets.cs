// System.Net.Sockets: Socket, NetworkStream, TcpListener, TcpClient, UdpClient, Dns, and the
// ExternalException -> Win32Exception -> SocketException chain (error codes need fields, which the
// runtime's fixed exception layout doesn't have). Blocking calls; the asynchronous operations
// (SocketAsyncEventArgs, ValueTask Receive/Send/AcceptAsync) run on the socket engine (SocketEngine.cs:
// epoll on Linux). Connecting asynchronously is still a blocking connect on the thread pool.
#nullable disable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.ExternalException")]
    public class ExternalExceptionImpl : SystemException
    {
        internal const int E_FAIL = unchecked((int)0x80004005);
        // The runtime's exception layout has no HResult slot, so the error code lives here.
        internal int _hresult = E_FAIL;
        public ExternalExceptionImpl() : base("External component has thrown an exception.") { }
        public ExternalExceptionImpl(string message) : base(message) { }
        public ExternalExceptionImpl(string message, Exception inner) : base(message, inner) { }
        public ExternalExceptionImpl(string message, int errorCode) : base(message) { _hresult = errorCode; }
        public virtual int ErrorCode => _hresult;
    }

    [Shadows("System.ComponentModel.Win32Exception")]
    public class Win32ExceptionImpl : System.Runtime.InteropServices.ExternalException
    {
        private const int E_FAIL = unchecked((int)0x80004005);
        public int NativeErrorCode { get; }
        private int HResultValue => ((ExternalExceptionImpl)(object)this)._hresult;

        public Win32ExceptionImpl() : this(System.Runtime.InteropServices.Marshal.GetLastPInvokeError()) { }
        public Win32ExceptionImpl(int error) : this(error, System.Runtime.InteropServices.Marshal.GetPInvokeErrorMessage(error)) { }
        public Win32ExceptionImpl(int error, string message) : base(message) { NativeErrorCode = error; }
        public Win32ExceptionImpl(string message) : this(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), message) { }
        public Win32ExceptionImpl(string message, Exception innerException) : base(message, innerException)
        {
            NativeErrorCode = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        }

        /// <summary>.NET's Win32Exception.ToString: "Type (native): message", with the HResult when it isn't E_FAIL.</summary>
        public override string ToString()
        {
            if (NativeErrorCode == 0 || NativeErrorCode == HResultValue) return base.ToString();
            string native = NativeErrorCode < 0 ? "0x" + NativeErrorCode.ToString("X8") : NativeErrorCode.ToString();
            var s = new System.Text.StringBuilder(GetType().ToString());
            if (HResultValue == E_FAIL) s.Append(" (").Append(native).Append(')');
            else s.Append(" (").Append(HResultValue.ToString("X8")).Append(", ").Append(native).Append(')');
            string message = Message;
            if (!string.IsNullOrEmpty(message)) s.Append(": ").Append(message);
            if (InnerException != null) s.Append(" ---> ").Append(InnerException.ToString());
            return s.ToString();
        }
    }

    [Shadows("System.Net.Sockets.SocketException")]
    public class SocketExceptionImpl : System.ComponentModel.Win32Exception
    {
        private readonly SocketError _errorCode;

        public SocketExceptionImpl() : this((int)SocketError.SocketError) { }

        /// <summary>new SocketException(int SocketError value): the native error and message are the OS's for it.</summary>
        public SocketExceptionImpl(int errorCode) : this((SocketError)errorCode, NativeFor((SocketError)errorCode), (EndPoint)null) { }

        public SocketExceptionImpl(int errorCode, string message)
            : base(NativeFor((SocketError)errorCode), message) { _errorCode = (SocketError)errorCode; }

        internal SocketExceptionImpl(SocketError errorCode, int nativeError, EndPoint endPoint)
            : base(nativeError, MessageFor(errorCode, nativeError, endPoint)) { _errorCode = errorCode; }

        internal SocketExceptionImpl(SocketError errorCode, int nativeError, string message)
            : base(nativeError, message) { _errorCode = errorCode; }

        public SocketError SocketErrorCode => _errorCode;
        public override int ErrorCode => NativeErrorCode;

        private static int NativeFor(SocketError error)
        {
#if CSHARPC_TARGET_WINDOWS
            return (int)error;
#else
            for (int i = 1; i < 200; i++) if (NetNative.ToSocketError(i) == error) return i;
            return (int)error;
#endif
        }

        private static string MessageFor(SocketError error, int nativeError, EndPoint endPoint)
        {
            string message = nativeError == (int)error && error != SocketError.Success
#if CSHARPC_TARGET_WINDOWS
                ? NetNative.Message(nativeError)
#else
                ? "Unknown socket error"
#endif
                : NetNative.Message(nativeError);
            return endPoint == null ? message : message + " " + endPoint.ToString();
        }
    }

    [Shadows("System.Net.Sockets.Socket")]
    public sealed unsafe class SocketImpl : IDisposable
    {
        private long _handle;
        private bool _disposed, _connected, _bound, _listening, _dualMode;
        private EndPoint _remote;
        private int _receiveTimeout, _sendTimeout;
        private bool _noDelay;
        // Asynchronous operations (SocketEngine.cs): the socket's queues, created by the first one. A socket
        // the engine made non-blocking (or accepted non-blocking) waits with poll() in synchronous calls.
        private SocketAsyncContext _async;
        private bool _nonBlocking;
        // new Socket(SafeSocketHandle(fd, ownsHandle: false)) (an inherited listen socket): Dispose leaves the fd open.
        private bool _ownsHandle = true;

        internal long RawHandle => _handle;

        public AddressFamily AddressFamily { get; }
        public SocketType SocketType { get; }
        public ProtocolType ProtocolType { get; }

        /// <summary>new Socket(SocketType, ProtocolType): an IPv6 socket in dual mode, like .NET.</summary>
        public SocketImpl(SocketType socketType, ProtocolType protocolType)
            : this(AddressFamily.InterNetworkV6, socketType, protocolType)
        {
            DualMode = true;
        }

        public SocketImpl(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType)
        {
            if (addressFamily != AddressFamily.InterNetwork && addressFamily != AddressFamily.InterNetworkV6)
                throw new NotSupportedException("csharpc: sockets support only the InterNetwork and InterNetworkV6 address families.");
            int type = socketType == SocketType.Stream ? NetNative.SOCK_STREAM
                : socketType == SocketType.Dgram ? NetNative.SOCK_DGRAM
                : throw new NotSupportedException("csharpc: only stream and datagram sockets are supported.");
            int protocol = protocolType == ProtocolType.Tcp ? NetNative.IPPROTO_TCP
                : protocolType == ProtocolType.Udp ? NetNative.IPPROTO_UDP
                : protocolType == ProtocolType.Unspecified ? 0
                : throw new NotSupportedException("csharpc: only TCP and UDP sockets are supported.");
            _handle = NetNative.Socket(addressFamily == AddressFamily.InterNetworkV6 ? NetNative.AF_INET6 : NetNative.AF_INET, type, protocol);
            AddressFamily = addressFamily;
            SocketType = socketType;
            ProtocolType = protocolType;
        }

        /// <summary>A socket over an existing descriptor (systemd socket activation, Kestrel's FileHandleEndPoint).</summary>
        public SocketImpl(SafeSocketHandle handle)
        {
            ArgumentNullException.ThrowIfNull(handle);
            var safe = (SafeSocketHandleImpl)(object)handle;
            if (safe.IsInvalid) throw new ArgumentException("The handle is invalid.", "handle");
#if CSHARPC_TARGET_LINUX
            long fd = safe.DangerousGetHandle();
            int domain = NetNative.GetInt(fd, NetNative.SOL_SOCKET, 39 /* SO_DOMAIN */);
            int type = NetNative.GetInt(fd, NetNative.SOL_SOCKET, 3 /* SO_TYPE */);
            int protocol = NetNative.GetInt(fd, NetNative.SOL_SOCKET, 38 /* SO_PROTOCOL */);
            if (domain != NetNative.AF_INET && domain != NetNative.AF_INET6)
                throw new NotSupportedException("csharpc: sockets support only the InterNetwork and InterNetworkV6 address families.");
            _handle = fd;
            _ownsHandle = safe.OwnsHandle;
            AddressFamily = domain == NetNative.AF_INET6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            SocketType = type == NetNative.SOCK_DGRAM ? SocketType.Dgram : SocketType.Stream;
            ProtocolType = protocol == NetNative.IPPROTO_UDP ? ProtocolType.Udp : ProtocolType.Tcp;
            _bound = true;
            _listening = NetNative.GetInt(fd, NetNative.SOL_SOCKET, 30 /* SO_ACCEPTCONN */) != 0;
            if (AddressFamily == AddressFamily.InterNetworkV6) _dualMode = NetNative.GetInt(fd, NetNative.IPPROTO_IPV6, NetNative.IPV6_V6ONLY) == 0;
#else
            throw new NotSupportedException("A socket over an existing handle is not supported by csharpc yet on this platform.");
#endif
        }

        private SocketImpl(long handle, AddressFamily family, SocketType type, ProtocolType protocol, bool dualMode)
        {
            _handle = handle;
            AddressFamily = family;
            SocketType = type;
            ProtocolType = protocol;
            _dualMode = dualMode;
            _connected = true;
        }

        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(typeof(Socket).FullName);
        }

        private SocketAsyncContext AsyncContext()
        {
            EnsureOpen();
            SocketAsyncContext context = Volatile.Read(ref _async);
            if (context != null) return context;
            lock (this)
            {
                if (_async == null)
                {
                    context = new SocketAsyncContext(this);
                    SocketEngine.Register(context);
                    _nonBlocking = !SocketEngine.Blocking;
                    Volatile.Write(ref _async, context);
                }
                return _async;
            }
        }

        /// <summary>A connection accepted from this listener (non-blocking where the engine accepts it so).</summary>
        internal SocketImpl CreateAccepted(long client) =>
            new SocketImpl(client, AddressFamily, SocketType, ProtocolType, _dualMode) { _nonBlocking = !SocketEngine.Blocking };

        /// <summary>Synchronous calls on a non-blocking socket wait for readiness (the timeout, or none).</summary>
        private void WaitReady(bool write, int timeout)
        {
#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
            if (!NetNative.Wait(_handle, write, timeout <= 0 ? -1 : timeout))
                throw (SocketException)(object)new SocketExceptionImpl(SocketError.TimedOut, NetNative.EAGAIN, (EndPoint)null);
#endif
        }

        public static bool OSSupportsIPv4 => !OperatingSystem.IsWasi();
        public static bool OSSupportsIPv6 => !OperatingSystem.IsWasi();
        public static bool OSSupportsUnixDomainSockets => false;

        // ---- SocketAsyncEventArgs operations: true when pending (Completed is raised later) ----------
        private SocketAsyncEventArgsImpl Begin(SocketAsyncEventArgs e, SocketAsyncOperation operation)
        {
            if (e == null) throw new ArgumentNullException("e");
            EnsureOpen();
            var args = (SocketAsyncEventArgsImpl)(object)e;
            args.StartOperation(this, operation);
            return args;
        }

        private static bool Run(SocketAsyncContext context, SocketOperation op)
        {
            if (context.Start(op)) return true;
            op.Complete(true);
            return false;
        }

        public bool ReceiveAsync(SocketAsyncEventArgs e)
        {
            var args = Begin(e, SocketAsyncOperation.Receive);
            // One operation object per SocketAsyncEventArgs, reused: it has one operation in flight at a time.
            var op = args.ReceiveOperation ??= new EventArgsOperations.Receive { Args = args };
            op.Reset();
            op.Buffer = args.OperationBuffer;
            op.BufferList = args.BufferList;
            op.Flags = args.SocketFlags;
            return Run(StartContext(args), op);
        }

        public bool SendAsync(SocketAsyncEventArgs e)
        {
            var args = Begin(e, SocketAsyncOperation.Send);
            if (args.SocketFlags != SocketFlags.None) { args.FinishSynchronously(SocketError.Success, 0); throw new NotSupportedException("csharpc: socket flags are not supported yet."); }
            var op = args.SendOperation ??= new EventArgsOperations.Send { Args = args };
            op.Reset();
            op.Buffer = args.OperationBuffer;
            op.BufferList = args.BufferList;
            return Run(StartContext(args), op);
        }

        public bool AcceptAsync(SocketAsyncEventArgs e)
        {
            var args = Begin(e, SocketAsyncOperation.Accept);
            if (!_listening) { args.FinishSynchronously(SocketError.Success, 0); throw new InvalidOperationException("You must call the Listen method before performing this operation."); }
            if (args.AcceptSocket != null)
            {
                args.FinishSynchronously(SocketError.Success, 0);
                throw new NotSupportedException("Accepting into an existing socket (SocketAsyncEventArgs.AcceptSocket) is not supported by csharpc yet.");
            }
            var accept = args.AcceptOperation ??= new EventArgsOperations.Accept { Args = args };
            accept.Reset();
            return Run(StartContext(args), accept);
        }

        private SocketAsyncContext StartContext(SocketAsyncEventArgsImpl args)
        {
            try { return AsyncContext(); }
            catch { args.FinishSynchronously(SocketError.Success, 0); throw; }
        }

        // ---- Task-based operations ----------------------------------------------------------------
        private T StartTask<T>(T op, CancellationToken cancellationToken) where T : SocketOperation
        {
            SocketAsyncContext context = AsyncContext();
            if (!context.Start(op)) { op.Complete(true); return op; }
            if (cancellationToken.CanBeCanceled)
            {
                op.Token = cancellationToken;
                op.Registration = cancellationToken.Register(() =>
                {
                    if (!context.Remove(op)) return;
                    op.Canceled = true;
                    op.Complete(false);
                });
            }
            return op;
        }

        public ValueTask<Socket> AcceptAsync(CancellationToken cancellationToken) => AcceptAsync((Socket)null, cancellationToken);

        public ValueTask<Socket> AcceptAsync(Socket acceptSocket, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return new ValueTask<Socket>(Task.FromCanceled<Socket>(cancellationToken));
            EnsureOpen();
            if (!_listening) throw new InvalidOperationException("You must call the Listen method before performing this operation.");
            if (acceptSocket != null) throw new NotSupportedException("Accepting into an existing socket is not supported by csharpc yet.");
            return new ValueTask<Socket>(StartTask(new TaskOperations.Accept(), cancellationToken).Source.Task);
        }

        public Task<Socket> AcceptAsync() => AcceptAsync(CancellationToken.None).AsTask();

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            return new ValueTask<int>(StartTask(new TaskOperations.Receive { Buffer = buffer, Flags = socketFlags }, cancellationToken).Source.Task);
        }

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            SendAsync(buffer, SocketFlags.None, cancellationToken);

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            if (socketFlags != SocketFlags.None) throw new NotSupportedException("csharpc: socket flags are not supported yet.");
            return new ValueTask<int>(StartTask(new TaskOperations.Send { Buffer = buffer }, cancellationToken).Source.Task);
        }

        public Task<int> ReceiveAsync(ArraySegment<byte> buffer, SocketFlags socketFlags) =>
            ReceiveAsync((Memory<byte>)buffer, socketFlags, CancellationToken.None).AsTask();

        public Task<int> SendAsync(ArraySegment<byte> buffer, SocketFlags socketFlags) =>
            SendAsync((ReadOnlyMemory<byte>)buffer, socketFlags, CancellationToken.None).AsTask();

        public bool DualMode
        {
            get
            {
                if (AddressFamily != AddressFamily.InterNetworkV6) throw new NotSupportedException("This protocol version is not supported.");
                return _dualMode;
            }
            set
            {
                if (AddressFamily != AddressFamily.InterNetworkV6) throw new NotSupportedException("This protocol version is not supported.");
                EnsureOpen();
                NetNative.SetInt(_handle, NetNative.IPPROTO_IPV6, NetNative.IPV6_V6ONLY, value ? 0 : 1);
                _dualMode = value;
            }
        }

        public bool Connected => _connected;
        public bool IsBound => _bound;

        public bool NoDelay
        {
            get => _noDelay;
            set { EnsureOpen(); NetNative.SetInt(_handle, NetNative.IPPROTO_TCP, NetNative.TCP_NODELAY, value ? 1 : 0); _noDelay = value; }
        }

        public int ReceiveTimeout
        {
            get => _receiveTimeout;
            set
            {
                if (value < -1) throw new ArgumentOutOfRangeException("value");
                EnsureOpen();
                NetNative.SetTimeout(_handle, NetNative.SO_RCVTIMEO, value == -1 ? 0 : value);
                _receiveTimeout = value == -1 ? 0 : value;
            }
        }

        public int SendTimeout
        {
            get => _sendTimeout;
            set
            {
                if (value < -1) throw new ArgumentOutOfRangeException("value");
                EnsureOpen();
                NetNative.SetTimeout(_handle, NetNative.SO_SNDTIMEO, value == -1 ? 0 : value);
                _sendTimeout = value == -1 ? 0 : value;
            }
        }

        public EndPoint LocalEndPoint
        {
            get
            {
                EnsureOpen();
                return _bound || _connected ? NetNative.LocalEndPoint(_handle) : null;
            }
        }

        public EndPoint RemoteEndPoint
        {
            get
            {
                EnsureOpen();
                if (!_connected) return null;
                return _remote ?? (_remote = NetNative.RemoteEndPoint(_handle));
            }
        }

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue) =>
            SetSocketOption(optionLevel, optionName, optionValue ? 1 : 0);

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue)
        {
            EnsureOpen();
            if (optionLevel == SocketOptionLevel.Socket && optionName == SocketOptionName.ReuseAddress)
                NetNative.SetInt(_handle, NetNative.SOL_SOCKET, NetNative.SO_REUSEADDR, optionValue);
            else if (optionLevel == SocketOptionLevel.Tcp && optionName == SocketOptionName.NoDelay)
                NoDelay = optionValue != 0;
            else if (optionLevel == SocketOptionLevel.Socket && optionName == SocketOptionName.ReceiveTimeout)
                ReceiveTimeout = optionValue;
            else if (optionLevel == SocketOptionLevel.Socket && optionName == SocketOptionName.SendTimeout)
                SendTimeout = optionValue;
            else if (optionLevel == SocketOptionLevel.IPv6 && optionName == SocketOptionName.IPv6Only)
                DualMode = optionValue == 0;
            else
                throw new NotSupportedException("csharpc: socket option " + optionLevel.ToString() + "." + optionName.ToString() + " is not supported yet.");
        }

        /// <summary>Dual-mode sockets reach IPv4 endpoints through IPv4-mapped IPv6 addresses.</summary>
        private EndPoint Adapt(EndPoint endPoint)
        {
            if (endPoint == null) throw new ArgumentNullException("remoteEP");
            var ip = endPoint as IPEndPoint;
            if (ip != null && AddressFamily == AddressFamily.InterNetworkV6 && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                if (!_dualMode) throw new SocketException((int)SocketError.AddressFamilyNotSupported);
                return new IPEndPoint(ip.Address.MapToIPv6(), ip.Port);
            }
            if (ip != null && AddressFamily == AddressFamily.InterNetwork && ip.AddressFamily == AddressFamily.InterNetworkV6)
                throw new SocketException((int)SocketError.AddressFamilyNotSupported);
            return endPoint;
        }

        public void Bind(EndPoint localEP)
        {
            EnsureOpen();
            if (localEP == null) throw new ArgumentNullException("localEP");
            NetNative.Bind(_handle, Adapt(localEP));
            _bound = true;
        }

        public void Listen() => Listen(int.MaxValue);

        public void Listen(int backlog)
        {
            EnsureOpen();
            NetNative.Listen(_handle, backlog > 4096 ? 4096 : backlog);
            _listening = true;
        }

        public Socket Accept()
        {
            EnsureOpen();
            if (!_listening) throw new InvalidOperationException("You must call the Start() method before calling this method.");
            if (_nonBlocking)
            {
                while (true)
                {
                    long ready = NetNative.TryAccept(_handle, out int error);
                    if (ready != NetNative.InvalidSocket) return (Socket)(object)CreateAccepted(ready);
                    if (!NetNative.IsWouldBlock(error)) throw NetNative.Error(error);
                    WaitReady(false, 0);
                }
            }
            long client = NetNative.Accept(_handle);
            return (Socket)(object)new SocketImpl(client, AddressFamily, SocketType, ProtocolType, _dualMode);
        }

        public void Connect(EndPoint remoteEP)
        {
            EnsureOpen();
            if (_connected) throw new SocketException((int)SocketError.IsConnected);
            var target = Adapt(remoteEP);
            NetNative.Connect(_handle, target);
            _connected = true;
            _bound = true;
        }

        public void Connect(IPAddress address, int port) => Connect(new IPEndPoint(address, port));

        public void Connect(string host, int port)
        {
            if (host == null) throw new ArgumentNullException("host");
            IPAddress literal;
            if (IPAddress.TryParse(host, out literal)) { Connect(literal, port); return; }
            Connect(Dns.GetHostAddresses(host), port);
        }

        public void Connect(IPAddress[] addresses, int port)
        {
            if (addresses == null) throw new ArgumentNullException("addresses");
            SocketException last = null;
            foreach (var address in addresses)
            {
                if (address.AddressFamily != AddressFamily && !(AddressFamily == AddressFamily.InterNetworkV6 && _dualMode)) continue;
                try { Connect(new IPEndPoint(address, port)); return; }
                catch (SocketException e) { last = e; }
            }
            throw last ?? new SocketException((int)SocketError.AddressFamilyNotSupported);
        }

        public Task ConnectAsync(EndPoint remoteEP) => Task.Run(() => Connect(remoteEP));
        public ValueTask ConnectAsync(EndPoint remoteEP, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? new ValueTask(Task.FromCanceled(cancellationToken)) : new ValueTask(Task.Run(() => Connect(remoteEP)));
        public Task ConnectAsync(IPAddress address, int port) => Task.Run(() => Connect(address, port));
        public Task ConnectAsync(string host, int port) => Task.Run(() => Connect(host, port));

        public int Send(byte[] buffer) => Send(buffer, 0, buffer?.Length ?? 0, SocketFlags.None);
        public int Send(byte[] buffer, SocketFlags socketFlags) => Send(buffer, 0, buffer?.Length ?? 0, socketFlags);

        public int Send(byte[] buffer, int offset, int size, SocketFlags socketFlags)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if ((uint)offset > (uint)buffer.Length) throw new ArgumentOutOfRangeException("offset");
            if ((uint)size > (uint)(buffer.Length - offset)) throw new ArgumentOutOfRangeException("size");
            return Send(new ReadOnlySpan<byte>(buffer, offset, size), socketFlags);
        }

        public int Send(ReadOnlySpan<byte> buffer) => Send(buffer, SocketFlags.None);

        /// <summary>Blocking stream sockets send everything, as .NET's Socket.Send does.</summary>
        public int Send(ReadOnlySpan<byte> buffer, SocketFlags socketFlags)
        {
            EnsureOpen();
            if (socketFlags != SocketFlags.None) throw new NotSupportedException("csharpc: socket flags are not supported yet.");
            fixed (byte* p = buffer)
            {
                int sent = 0;
                while (sent < buffer.Length)
                {
                    if (_nonBlocking)
                    {
                        int m = NetNative.TrySend(_handle, p + sent, buffer.Length - sent, out int error);
                        if (m < 0)
                        {
                            if (!NetNative.IsWouldBlock(error)) throw NetNative.Error(error);
                            WaitReady(true, _sendTimeout);
                            continue;
                        }
                        sent += m;
                        if (SocketType != SocketType.Stream) break;
                        continue;
                    }
                    int n = NetNative.Send(_handle, p + sent, buffer.Length - sent, _sendTimeout > 0);
                    if (n == 0) break;
                    sent += n;
                    if (SocketType != SocketType.Stream) break;
                }
                return sent;
            }
        }

        public int Receive(byte[] buffer) => Receive(buffer, 0, buffer?.Length ?? 0, SocketFlags.None);
        public int Receive(byte[] buffer, SocketFlags socketFlags) => Receive(buffer, 0, buffer?.Length ?? 0, socketFlags);

        public int Receive(byte[] buffer, int offset, int size, SocketFlags socketFlags)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if ((uint)offset > (uint)buffer.Length) throw new ArgumentOutOfRangeException("offset");
            if ((uint)size > (uint)(buffer.Length - offset)) throw new ArgumentOutOfRangeException("size");
            return Receive(new Span<byte>(buffer, offset, size), socketFlags);
        }

        public int Receive(Span<byte> buffer) => Receive(buffer, SocketFlags.None);

        public int Receive(Span<byte> buffer, SocketFlags socketFlags)
        {
            EnsureOpen();
            if (socketFlags != SocketFlags.None) throw new NotSupportedException("csharpc: socket flags are not supported yet.");
            fixed (byte* p = buffer)
            {
                if (!_nonBlocking) return NetNative.Receive(_handle, p, buffer.Length, _receiveTimeout > 0);
                while (true)
                {
                    int n = NetNative.TryReceive(_handle, p, buffer.Length, 0, out int error);
                    if (n >= 0) return n;
                    if (!NetNative.IsWouldBlock(error)) throw NetNative.Error(error);
                    WaitReady(false, _receiveTimeout);
                }
            }
        }

        public int SendTo(byte[] buffer, EndPoint remoteEP) => SendTo(buffer, 0, buffer?.Length ?? 0, SocketFlags.None, remoteEP);

        public int SendTo(byte[] buffer, int offset, int size, SocketFlags socketFlags, EndPoint remoteEP)
        {
            EnsureOpen();
            if (buffer == null) throw new ArgumentNullException("buffer");
            fixed (byte* p = buffer) return NetNative.SendTo(_handle, p + offset, size, Adapt(remoteEP));
        }

        public int ReceiveFrom(byte[] buffer, ref EndPoint remoteEP) => ReceiveFrom(buffer, 0, buffer?.Length ?? 0, SocketFlags.None, ref remoteEP);

        public int ReceiveFrom(byte[] buffer, int offset, int size, SocketFlags socketFlags, ref EndPoint remoteEP)
        {
            EnsureOpen();
            if (buffer == null) throw new ArgumentNullException("buffer");
            EndPoint from;
            int n;
            fixed (byte* p = buffer) n = NetNative.ReceiveFrom(_handle, p + offset, size, out from);
            remoteEP = from;
            _bound = true;
            return n;
        }

        public void Shutdown(SocketShutdown how)
        {
            EnsureOpen();
            NetNative.Shutdown(_handle, how == SocketShutdown.Receive ? NetNative.SHUT_RD : how == SocketShutdown.Send ? NetNative.SHUT_WR : NetNative.SHUT_RDWR);
        }

        public void Close() => Dispose();

        /// <summary>Close(0) resets the connection (SO_LINGER with a zero timeout), as .NET does.</summary>
        public void Close(int timeout)
        {
            if (timeout < -1) throw new ArgumentOutOfRangeException("timeout");
            if (!_disposed && timeout == 0)
            {
                try { NetNative.SetLinger(_handle, true, 0); }
                catch (SocketException) { }
            }
            Dispose();
        }

        private int _disposeStarted;

        public void Dispose()
        {
            // Kestrel disposes a connection's socket from its send and receive loops at once: closing the
            // descriptor twice could close another socket that reused the number.
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
            _disposed = true;
            _connected = false;
            // Pending operations end with OperationAborted before the descriptor goes away.
            Volatile.Read(ref _async)?.Close();
            if (_ownsHandle) NetNative.Close(_handle);
            _handle = NetNative.InvalidSocket;
        }
    }

    [Shadows("System.Net.Sockets.SafeSocketHandle")]
    public sealed class SafeSocketHandleImpl : IDisposable
    {
        private readonly nint _handle;
        private bool _closed;
        internal bool OwnsHandle { get; }

        public SafeSocketHandleImpl() { _handle = -1; OwnsHandle = true; }

        public SafeSocketHandleImpl(nint preexistingHandle, bool ownsHandle)
        {
            _handle = preexistingHandle;
            OwnsHandle = ownsHandle;
        }

        public bool IsInvalid => _handle == -1;
        public bool IsClosed => _closed;
        public nint DangerousGetHandle() => _handle;

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            if (OwnsHandle && !IsInvalid) NetNative.Close(_handle);
        }
    }

    [Shadows("System.Net.Sockets.NetworkStream")]
    public class NetworkStreamImpl : Stream
    {
        private readonly Socket _socket;
        private readonly bool _ownsSocket;
        private bool _disposed;

        public NetworkStreamImpl(Socket socket) : this(socket, false) { }

        public NetworkStreamImpl(Socket socket, bool ownsSocket)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (!socket.Connected) throw new IOException("The operation is not allowed on non-connected sockets.");
            _socket = socket;
            _ownsSocket = ownsSocket;
        }

        public Socket Socket => _socket;
        public override bool CanRead => !_disposed;
        public override bool CanWrite => !_disposed;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException("This stream does not support seek operations.");
        public override long Position
        {
            get => throw new NotSupportedException("This stream does not support seek operations.");
            set => throw new NotSupportedException("This stream does not support seek operations.");
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("This stream does not support seek operations.");
        public override void SetLength(long value) => throw new NotSupportedException("This stream does not support seek operations.");
        public override void Flush() { }

        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(typeof(NetworkStream).FullName);
        }

        /// <summary>NetworkStream wraps socket errors in IOException, as .NET does.</summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureOpen();
            try { return _socket.Receive(buffer, offset, count, SocketFlags.None); }
            catch (SocketException e)
            {
                throw new IOException("Unable to read data from the transport connection: " + e.Message + ".", e);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureOpen();
            try { _socket.Send(buffer, offset, count, SocketFlags.None); }
            catch (SocketException e)
            {
                throw new IOException("Unable to write data to the transport connection: " + e.Message + ".", e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing && _ownsSocket) _socket.Dispose();
        }
    }

    [Shadows("System.Net.Sockets.TcpListener")]
    public class TcpListenerImpl
    {
        private readonly IPEndPoint _endPoint;
        private Socket _server;
        private bool _active;

        public TcpListenerImpl(IPAddress localaddr, int port) : this(new IPEndPoint(localaddr ?? throw new ArgumentNullException("localaddr"), port)) { }

        public TcpListenerImpl(IPEndPoint localEP)
        {
            if (localEP == null) throw new ArgumentNullException("localEP");
            _endPoint = localEP;
            _server = new Socket(localEP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        public Socket Server => _server;
        public EndPoint LocalEndpoint => _active ? _server.LocalEndPoint : _endPoint;

        public void Start() => Start(int.MaxValue);

        public void Start(int backlog)
        {
            if (_active) return;
            if (_server == null) _server = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _server.Bind(_endPoint);
            _server.Listen(backlog);
            _active = true;
        }

        public void Stop()
        {
            _server?.Dispose();
            _server = null;
            _active = false;
        }

        public Socket AcceptSocket()
        {
            if (!_active) throw new InvalidOperationException("Not listening. You must call the Start() method before calling this method.");
            return _server.Accept();
        }

        public TcpClient AcceptTcpClient() => (TcpClient)(object)new TcpClientImpl(AcceptSocket());
        public Task<Socket> AcceptSocketAsync() => Task.Run(() => AcceptSocket());
        public Task<TcpClient> AcceptTcpClientAsync() => Task.Run(() => AcceptTcpClient());

        public void Dispose() => Stop();
    }

    [Shadows("System.Net.Sockets.TcpClient")]
    public class TcpClientImpl : IDisposable
    {
        private Socket _client;
        private NetworkStream _stream;
        private bool _disposed;

        /// <summary>new TcpClient(): a dual-mode IPv6 socket, as .NET (IPv4 peers appear as ::ffff:a.b.c.d).</summary>
        public TcpClientImpl() { _client = new Socket(SocketType.Stream, ProtocolType.Tcp); }

        public TcpClientImpl(AddressFamily family)
        {
            _client = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
        }

        public TcpClientImpl(string hostname, int port)
        {
            if (hostname == null) throw new ArgumentNullException("hostname");
            Connect(hostname, port);
        }

        internal TcpClientImpl(Socket acceptedSocket) { _client = acceptedSocket; }

        public Socket Client { get => _client; set => _client = value; }
        public bool Connected => _client != null && _client.Connected;

        public bool NoDelay { get => _client.NoDelay; set => _client.NoDelay = value; }
        public int ReceiveTimeout { get => _client.ReceiveTimeout; set => _client.ReceiveTimeout = value; }
        public int SendTimeout { get => _client.SendTimeout; set => _client.SendTimeout = value; }

        public void Connect(string hostname, int port)
        {
            if (hostname == null) throw new ArgumentNullException("hostname");
            if (_disposed) throw new ObjectDisposedException(typeof(TcpClient).FullName);
            // As .NET: try each resolved address with a socket of its family.
            SocketException last = null;
            IPAddress literal;
            IPAddress[] addresses = IPAddress.TryParse(hostname, out literal) ? new[] { literal } : Dns.GetHostAddresses(hostname);
            foreach (var address in addresses)
            {
                var socket = _client != null && !_client.Connected && Accepts(_client, address.AddressFamily)
                    ? _client : new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Connect(new IPEndPoint(address, port));
                    _client = socket;
                    return;
                }
                catch (SocketException e)
                {
                    last = e;
                    if (socket != _client) socket.Dispose();
                }
            }
            throw last ?? new SocketException((int)SocketError.HostNotFound);
        }

        public void Connect(IPAddress address, int port) => Connect(new IPEndPoint(address, port));

        public void Connect(IPEndPoint remoteEP)
        {
            if (_disposed) throw new ObjectDisposedException(typeof(TcpClient).FullName);
            if (_client == null || !Accepts(_client, remoteEP.AddressFamily))
                _client = new Socket(remoteEP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _client.Connect(remoteEP);
        }

        /// <summary>A dual-mode IPv6 socket also reaches IPv4 peers (through IPv4-mapped addresses).</summary>
        private static bool Accepts(Socket socket, AddressFamily family) =>
            socket.AddressFamily == family || (socket.AddressFamily == AddressFamily.InterNetworkV6 && socket.DualMode);

        public Task ConnectAsync(string host, int port) => Task.Run(() => Connect(host, port));
        public Task ConnectAsync(IPAddress address, int port) => Task.Run(() => Connect(address, port));

        public NetworkStream GetStream()
        {
            if (_disposed) throw new ObjectDisposedException(typeof(TcpClient).FullName);
            if (!Connected) throw new InvalidOperationException("The operation is not allowed on non-connected sockets.");
            return _stream ?? (_stream = new NetworkStream(_client, true));
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_stream != null) _stream.Dispose();
            else _client?.Dispose();
        }
    }

    [Shadows("System.Net.Sockets.UdpClient")]
    public class UdpClientImpl : IDisposable
    {
        private Socket _client;
        private bool _connected;

        public UdpClientImpl() : this(AddressFamily.InterNetwork) { }
        public UdpClientImpl(AddressFamily family) { _client = new Socket(family, SocketType.Dgram, ProtocolType.Udp); }
        public UdpClientImpl(int port) : this(port, AddressFamily.InterNetwork) { }

        public UdpClientImpl(int port, AddressFamily family) : this(family)
        {
            _client.Bind(new IPEndPoint(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, port));
        }

        public UdpClientImpl(IPEndPoint localEP)
        {
            if (localEP == null) throw new ArgumentNullException("localEP");
            _client = new Socket(localEP.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _client.Bind(localEP);
        }

        public Socket Client { get => _client; set => _client = value; }

        public void Connect(string hostname, int port)
        {
            IPAddress literal;
            var address = IPAddress.TryParse(hostname, out literal) ? literal : Dns.GetHostAddresses(hostname)[0];
            Connect(new IPEndPoint(address, port));
        }

        public void Connect(IPAddress addr, int port) => Connect(new IPEndPoint(addr, port));
        public void Connect(IPEndPoint endPoint) { _client.Connect(endPoint); _connected = true; }

        public int Send(byte[] dgram, int bytes) => _connected
            ? _client.Send(dgram, 0, bytes, SocketFlags.None)
            : throw new InvalidOperationException("The operation is not allowed on non-connected sockets.");

        public int Send(byte[] dgram, int bytes, IPEndPoint endPoint)
        {
            if (_connected) throw new InvalidOperationException("Cannot send packets to an arbitrary host while connected.");
            return _client.SendTo(dgram, 0, bytes, SocketFlags.None, endPoint);
        }

        public int Send(byte[] dgram, int bytes, string hostname, int port)
        {
            IPAddress literal;
            var address = IPAddress.TryParse(hostname, out literal) ? literal : Dns.GetHostAddresses(hostname)[0];
            return Send(dgram, bytes, new IPEndPoint(address, port));
        }

        public byte[] Receive(ref IPEndPoint remoteEP)
        {
            byte[] buffer = new byte[65536];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int n = _client.ReceiveFrom(buffer, ref from);
            remoteEP = (IPEndPoint)from;
            byte[] result = new byte[n];
            Array.Copy(buffer, result, n);
            return result;
        }

        public void Close() => Dispose();
        public void Dispose() => _client?.Dispose();
    }

    [Shadows("System.Net.Dns")]
    public static class DnsImpl
    {
        public static IPAddress[] GetHostAddresses(string hostNameOrAddress)
        {
            if (hostNameOrAddress == null) throw new ArgumentNullException("hostNameOrAddress");
            IPAddress literal;
            if (IPAddress.TryParse(hostNameOrAddress, out literal)) return new[] { literal };
            return NetNative.Resolve(hostNameOrAddress);
        }

        public static Task<IPAddress[]> GetHostAddressesAsync(string hostNameOrAddress) => Task.Run(() => GetHostAddresses(hostNameOrAddress));

        public static string GetHostName() => NetNative.HostName();
    }
}
