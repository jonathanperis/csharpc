// ExternalException.ToString (and so DbException's, NpgsqlException's) shows the HResult after the type
// name and appends the inner exception without .NET's "--- End of inner exception stack trace ---" line;
// Win32Exception.ToString (SocketException's) keeps the stack trace.
using System;
using System.ComponentModel;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;

static string Shape(Exception e) =>
    string.Join(" | ", e.ToString().Split(Environment.NewLine).Select(l => l.StartsWith("   at ") ? "at" : l).Distinct());

Console.WriteLine(new ExternalException("boom").ToString());
var external = new ExternalException("with code", 0x1234);
Console.WriteLine($"{external} | {external.ErrorCode} {external.HResult:X8}");
Console.WriteLine($"{new ExternalException().HResult:X8} {new ExternalException("m", new InvalidOperationException("inner")).HResult:X8}");
Console.WriteLine(new ExternalException("outer", new InvalidOperationException("inner")).ToString());
Console.WriteLine(new AppDbException("db failed", new TimeoutException("timed out")).ToString());
Console.WriteLine(new AppDbException("db code", 42).ToString());

try { Thrower.Db(); }
catch (DbException e) { Console.WriteLine(Shape(e)); }

try { Thrower.Win32(); }
catch (Win32Exception e) { Console.WriteLine(Shape(e)); }

try { Thrower.Socket(); }
// The native error and its message are the OS's (111 "Connection refused" on Linux, 10061 on Windows).
catch (SocketException e) { Console.WriteLine($"{Shape(e).Replace(e.Message, "MESSAGE").Replace($"({e.NativeErrorCode})", "(NATIVE)")} | {e.HResult:X8} {e.ErrorCode == e.NativeErrorCode}"); }

Console.WriteLine(new Win32Exception(2, "native two").ToString());
Console.WriteLine(new Win32Exception(0, "zero").ToString());

class AppDbException : DbException
{
    public AppDbException(string message, Exception inner) : base(message, inner) { }
    public AppDbException(string message, int code) : base(message, code) { }
}

static class Thrower
{
    public static void Db()
    {
        try { throw new InvalidOperationException("inner thrown"); }
        catch (Exception e) { throw new AppDbException("wrapped", e); }
    }

    public static void Win32() => throw new Win32Exception(13, "denied");
    public static void Socket() => throw new SocketException((int)SocketError.ConnectionRefused);
}
