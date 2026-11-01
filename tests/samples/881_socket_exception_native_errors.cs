// new SocketException(SocketError) takes the OS's error number for it as .NET's SocketErrorPal does, which is
// not the inverse of errno -> SocketError: IOPending is EINPROGRESS, OperationAborted ECANCELED,
// TooManyOpenSockets ENFILE, ProtocolNotSupported EPROTONOSUPPORT (not EPROTO), TryAgain EAGAIN, NoData
// ENODATA; HostNotFound and SocketError have .NET's own negative numbers and texts; the rest keep their value
// and strerror's "Unknown error N". The numbers and texts are the OS's (macOS and Windows record their own).
using System;
using System.Net.Sockets;

foreach (SocketError value in Enum.GetValues<SocketError>())
{
    var e = new SocketException((int)value);
    Console.WriteLine($"{value} {(int)value}: {e.SocketErrorCode} {e.NativeErrorCode} {e.ErrorCode} '{e.Message}'");
}
var withMessage = new SocketException((int)SocketError.TimedOut, "custom");
Console.WriteLine($"{withMessage.SocketErrorCode} {withMessage.NativeErrorCode} '{withMessage.Message}'");
