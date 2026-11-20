# System.Net.Sockets, System.Net.NameResolution, System.Net.NetworkInformation

A library served from the framework's IL: its row of the per-library table of [ADR 0023](../adr/0023-framework-il-first.md)
(columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| ROW |

## The socket engine below the framework's IL (security review)

On Linux and macOS, `Socket`, `NetworkStream`, `TcpClient`, `TcpListener`, `UdpClient`, `SocketAsyncEventArgs`, `SafeSocketHandle`,
`UnixDomainSocketEndPoint`, `Dns`, `IPHostEntry` and `SocketException` are the framework's IL, and so is the framework's own asynchronous
engine (`SocketAsyncEngine`, `SocketAsyncContext`: one event thread, completions on the thread pool, as in .NET). On Linux
`NetworkInterface`, `NetworkChange` and the rest of System.Net.NetworkInformation are too. Their `Interop.Sys` P/Invokes into
`libSystem.Native` bind to csharpc's C# contract, `corelib/Net/SystemNative.Net.cs` and (Linux) `corelib/Net/SystemNative.NetworkInformation.cs`
(option (b) of [21-ownership.md](../21-ownership.md): csharpc's own libc layer, not the pack's shim; the same mechanism as the file system's
`corelib/IO/SystemNative.cs`). That contract is csharpc's socket engine now: epoll on Linux, kqueue on macOS, behind the PAL's
`CreateSocketEventPort`/`TryChangeSocketEventRegistration`/`WaitForSocketEvents`. It is `pal_networking.c`, `pal_networking_common.h`,
`pal_io_common.h`, `pal_interfaceaddresses.c`, `pal_networkchange.c`, `pal_maphardwaretype.c` and `pal_tcpstate.c` (dotnet/runtime
release/11.0), function by function. A framework assembly is compiled from IL only when the contract exports every libSystem.Native entry
point it imports (`FrameworkIl.ServedByNativeContract`): a new one in a later .NET is an explicit "can't be compiled from IL" diagnostic,
never a silent fallback. What was checked:

- **Descriptors are close-on-exec.** `socket` gets `SOCK_CLOEXEC` (Linux) or `FD_CLOEXEC` right after (macOS, as there); `accept` is
  `accept4(SOCK_CLOEXEC)` on Linux, and on macOS `accept` followed by `FD_CLOEXEC` and blocking mode, closing the new descriptor if either
  fails, as there; the epoll instance is `epoll_create1(EPOLL_CLOEXEC)`. Stronger than `pal_*.c` and not observable: the interface list's
  helper datagram socket and the netlink change listener get `SOCK_CLOEXEC` too (upstream creates them without), and the kqueue gets
  `FD_CLOEXEC`.
- **Bounds on every native write.** Socket-address accessors check the length the caller passed against the field they touch (family,
  port, IPv4 and IPv6 address, scope) and return `EFAULT` otherwise, as there; `accept`, `getsockname`, `getpeername` and the option getters
  pass the caller's length to the kernel and report at most that much back (the kernel reports the untruncated length; the contract clamps
  it, where `pal_networking.c` only asserts); `ReceiveMessage`/`SendMessage` reject negative lengths and counts and give a stream socket at
  most `IOV_MAX` vectors; `ReceiveSocketError` copies the offender's address bounded by both the caller's length and `sockaddr_storage`
  (upstream bounds it by the caller's only); the control-message walk is glibc's/Darwin's `CMSG_NXTHDR`, which stops before a header or its
  data would leave the buffer, and `TryGetIPPacketInformation` checks each message's length before reading `in_pktinfo`/`in6_pktinfo`;
  `WaitForSocketEvents` writes at most `*count` events into the buffer `CreateSocketEventBuffer` sized for `count` of the larger of the
  kernel's and the PAL's event (converting backwards on x86-64, where `epoll_event` is the smaller); `Poll` fills a `pollfd` array of exactly
  `eventCount`; the host entry is sized by a counting pass and the copy pass writes at most that many addresses (upstream decrements the count
  instead); `GetNetworkInterfaces` allocates one slot per `getifaddrs` entry plus one per address, as there, copies interface names up to
  their NUL within the 16-byte field (upstream copies 16 bytes whatever the name's length), reads netmasks from a copy (upstream's
  `mask2prefix` shifts them in place) and treats a missing netmask as a full prefix (upstream dereferences it); `ReadEvents` reads at most
  4 KiB and walks netlink messages within the length `recvmsg` returned, checking each message is long enough for the field it reads.
- **EINTR** is retried exactly where `pal_networking.c` retries it: `accept`, `connect`, `connectx`, `recv`, `send`, `recvmsg`,
  `sendmsg`, `sendfile`, `poll`, `epoll_wait`, `kevent`, the `FIONREAD`/`SIOCATMARK` ioctls; not around `bind`, `listen`, `shutdown`,
  `getsockopt`, `setsockopt` or `close` (an interrupted `close` of the event port is success, as there). macOS also retries `send`/`sendmsg`
  a few times on `EPROTOTYPE`, as there.
- **No new attack surface.** Names reach `getaddrinfo`/`getnameinfo` as the framework's NUL-terminated UTF-8; nothing parses untrusted
  text in the contract (the framework's IL parses endpoints, URIs and HTTP); the only callbacks are the framework's own
  `UnmanagedCallersOnly` functions; SIGPIPE stays ignored by the runtime (`csrt.c`), as .NET does, so a write to a reset connection is
  `EPIPE`, not a signal. The Windows corelib keeps its Winsock shadows (below), so nothing changes there.

What `libSystem.Native` does that the contract does differently, by design: the clamps and NUL-bounded copies above; `ReceiveSocketError`
hands `recvmsg` the platform's `MSG_DONTWAIT | MSG_ERRQUEUE` where `pal_networking.c` passes the PAL values unconverted (0x3000; on Linux
the error queue never blocks either way); `GetSocketType` on macOS reads `proc_pidfdinfo` into a 1 KiB buffer and requires only the
fields it reads, instead of comparing with the full `socket_fdinfo` size.

### Windows

The Windows build of System.Net.Sockets is overlapped I/O over an I/O completion port (`ThreadPoolBoundHandle`, `NativeOverlapped`),
which csharpc's thread pool does not implement yet: Windows keeps the corelib's Winsock shadows (`Net/Sockets.cs`, `SocketEngine.cs`,
`NetNative.cs`, `SocketAsyncEventArgs.cs`, `SocketAddressPal.cs`, `UnixDomainSocketEndPoint.cs`, `NetworkInterfaces.cs`, `NetworkChange.cs`,
now compiled into the Windows corelib only; `FrameworkIl` ignores a shadow file whose whole content is for other targets).
