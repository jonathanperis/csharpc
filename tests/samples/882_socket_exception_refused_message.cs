// A SocketException made from a SocketError carries the OS's error number and text for it: "Connection
// refused" for ConnectionRefused (on WebAssembly csharpc took Linux's errno 111, which isn't WASI's ECONNREFUSED,
// and printed another text). These two texts are the same in glibc, musl/wasi-libc and macOS.
using System;
using System.Net.Sockets;

foreach (var value in new[] { SocketError.ConnectionRefused, SocketError.ConnectionReset })
{
    var e = new SocketException((int)value);
    Console.WriteLine($"{e.SocketErrorCode}: '{e.Message}' native set {e.NativeErrorCode != 0} {e.ErrorCode == e.NativeErrorCode}");
}
try { throw new SocketException((int)SocketError.ConnectionRefused); }
catch (SocketException e) { Console.WriteLine(e.ToString().Split(Environment.NewLine)[0].Replace(e.NativeErrorCode.ToString(), "N")); }
