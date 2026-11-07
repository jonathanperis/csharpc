// Library members the Roslyn-from-IL survey listed as missing (M6, #169), each checked against .NET:
// Debugger.Break without a debugger, BinaryWriter.Write(char[]), AppDomain.AssemblyResolve/Load and ResolveEventArgs,
// Assembly.GlobalAssemblyCache, the RuntimeReflectionExtensions lookups, Delegate.CreateDelegate over a MethodInfo,
// File.GetAttributes/SetAttributes and FileInfo.IsReadOnly, SynchronizationContext.Wait, and the COM interop structs.
#pragma warning disable SYSLIB0005, CS0618, IL2026, IL2075, CS0169, CS0414
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;

static void Show(string label, Func<object> f)
{
    try { Console.WriteLine(label + ": " + f()); }
    catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name); }
}

Debugger.Break();
Console.WriteLine("Debugger.IsAttached: " + Debugger.IsAttached);

// BinaryWriter.Write(char[]): the characters are encoded without a length prefix.
var ms = new MemoryStream();
using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
{
    w.Write(new[] { 'a', 'b', 'é' });
    w.Write(new[] { 'x', 'y', 'z', 'w' }, 1, 2);
    w.Write(new byte[] { 1, 2, 3 }.AsSpan(1));
    w.Write("ab".AsSpan());
    Show("Write(null chars)", () => { w.Write((char[])null); return "no"; });
}
Console.WriteLine(BitConverter.ToString(ms.ToArray()));

// AppDomain.AssemblyResolve is never raised for a program with everything compiled in; Load finds what Assembly.Load does.
ResolveEventHandler handler = (s, e) => { Console.WriteLine("resolve " + e.Name); return null; };
AppDomain.CurrentDomain.AssemblyResolve += handler;
AppDomain.CurrentDomain.AssemblyResolve -= handler;
var resolveArgs = new ResolveEventArgs("Some.Assembly, Version=1.0.0.0", typeof(object).Assembly);
Console.WriteLine(resolveArgs.Name + " / " + (resolveArgs.RequestingAssembly == typeof(object).Assembly) + " / " + new ResolveEventArgs("Other").RequestingAssembly);
Console.WriteLine(AppDomain.CurrentDomain.Load(typeof(Sample).Assembly.GetName()) == typeof(Sample).Assembly);
Show("Assembly.GlobalAssemblyCache", () => typeof(object).Assembly.GlobalAssemblyCache);
Show("GetAssemblyName(missing)", () => AssemblyName.GetAssemblyName("/no/such/dir/x.dll"));
Show("GetAssemblyName(null)", () => AssemblyName.GetAssemblyName(null));

// RuntimeReflectionExtensions: Type's lookups with every binding flag but DeclaredOnly.
var type = typeof(Sample);
Console.WriteLine(string.Join(",", typeof(Sample).GetRuntimeMethods().Where(m => m.DeclaringType == type).Select(m => m.Name).Order()));
Console.WriteLine(typeof(Sample).GetRuntimeMethods().Count() == typeof(Sample).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Length);
Console.WriteLine(string.Join(",", typeof(Sample).GetRuntimeFields().Where(f => f.DeclaringType == type).Select(f => f.Name).Order()));
Console.WriteLine(string.Join(",", typeof(Sample).GetRuntimeProperties().Select(p => p.Name).Order()));
Console.WriteLine(string.Join(",", typeof(Sample).GetRuntimeEvents().Select(p => p.Name).Order()));
Console.WriteLine(typeof(Sample).GetRuntimeMethod("Twice", new[] { typeof(int) })?.Name + " " + typeof(Sample).GetRuntimeMethod("Hidden", Type.EmptyTypes));
Console.WriteLine(typeof(Sample).GetRuntimeProperty("Value")?.Name + " " + typeof(Sample).GetRuntimeField("_secret") + " " + typeof(Sample).GetRuntimeEvent("Changed")?.Name);
Show("GetRuntimeMethods(null)", () => ((Type)null).GetRuntimeMethods());

// Delegate.CreateDelegate over a MethodInfo.
var twice = typeof(Sample).GetMethod("Twice");
var inst = typeof(Sample).GetMethod("Inst");
Console.WriteLine(((Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), null, twice))(21));
Console.WriteLine(((Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), twice))(4));
Console.WriteLine(((Func<int>)Delegate.CreateDelegate(typeof(Func<int>), new Sample(), inst))());
Show("bad signature", () => Delegate.CreateDelegate(typeof(Func<string>), new Sample(), inst));
Show("bad signature, no throw", () => (object)Delegate.CreateDelegate(typeof(Func<string>), new Sample(), inst, false) ?? "null");
Show("null type", () => Delegate.CreateDelegate(null, new Sample(), inst));
Show("null method", () => Delegate.CreateDelegate(typeof(Func<int>), new Sample(), (MethodInfo)null));
Show("not a delegate", () => Delegate.CreateDelegate(typeof(string), new Sample(), inst));

// File attributes: ReadOnly is the write permission, and a missing file is FileNotFoundException.
string dir = Path.Combine(Path.GetTempPath(), "csharpc-attrs-" + Environment.ProcessId);
Directory.CreateDirectory(dir);
try
{
    string path = Path.Combine(dir, "a.txt");
    File.WriteAllText(path, "x");
    File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("644", 8));
    Console.WriteLine(File.GetAttributes(path) + " " + new FileInfo(path).IsReadOnly);
    File.SetAttributes(path, FileAttributes.ReadOnly);
    Console.WriteLine(File.GetAttributes(path) + " " + new FileInfo(path).IsReadOnly + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    var info = new FileInfo(path);
    info.IsReadOnly = false;
    Console.WriteLine(info.Attributes + " " + info.IsReadOnly + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    info.Attributes = FileAttributes.ReadOnly | FileAttributes.Archive;
    Console.WriteLine(info.Attributes + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    File.SetAttributes(path, FileAttributes.Normal);
    Console.WriteLine(File.GetAttributes(path) + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    // Without owner read permission nobody is granted write permission.
    File.SetUnixFileMode(path, UnixFileMode.GroupRead);
    File.SetAttributes(path, FileAttributes.Archive);
    Console.WriteLine(Convert.ToString((int)File.GetUnixFileMode(path), 8));
    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.WriteLine(File.GetAttributes(dir));
    File.SetAttributes(dir, FileAttributes.Directory);
    Console.WriteLine(Convert.ToString((int)File.GetUnixFileMode(dir) & 0x80, 8));
    Show("SetAttributes(missing)", () => { File.SetAttributes(Path.Combine(dir, "none"), FileAttributes.ReadOnly); return "no"; });
    Show("GetAttributes(missing)", () => File.GetAttributes(Path.Combine(dir, "none")));
    Show("GetAttributes(missing dir)", () => File.GetAttributes(Path.Combine(dir, "none", "x")));
    Show("SetAttributes(invalid)", () => { File.SetAttributes(path, (FileAttributes)0x40000000); return "no"; });
    Show("FileInfo.Attributes(missing)", () => (int)new FileInfo(Path.Combine(dir, "none")).Attributes);
    Show("FileInfo.IsReadOnly(missing)", () => new FileInfo(Path.Combine(dir, "none")).IsReadOnly);
    Show("FileInfo.IsReadOnly = (missing)", () => { new FileInfo(Path.Combine(dir, "none")).IsReadOnly = true; return "no"; });
}
finally { Directory.Delete(dir, true); }

// SynchronizationContext.Wait is virtual and checks its argument.
var context = new WaitingContext();
Show("Wait(null)", () => context.Wait(null, false, 0));
Console.WriteLine(context.Wait(new IntPtr[0], false, 5));

// The COM interop structs: plain sequential data.
var time = new FILETIME { dwLowDateTime = 5, dwHighDateTime = 7 };
var stat = new STATSTG { pwcsName = "stream", type = 2, cbSize = 1L << 33, mtime = time, grfMode = 0x12, clsid = Guid.Empty, reserved = 9 };
Console.WriteLine(stat.pwcsName + " " + stat.type + " " + stat.cbSize + " " + stat.mtime.dwLowDateTime + "," + stat.mtime.dwHighDateTime + " " + stat.ctime.dwLowDateTime + " " + stat.grfMode + " " + stat.reserved);

class WaitingContext : SynchronizationContext
{
    public WaitingContext() { SetWaitNotificationRequired(); }
    public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
    {
        if (waitHandles == null) return base.Wait(waitHandles, waitAll, millisecondsTimeout);
        return 258 + waitHandles.Length;
    }
}

class Sample
{
    private int _secret;
    public int Value { get; set; }
    public event EventHandler Changed;
    public static int Twice(int x) => 2 * x;
    public int Inst() => 7;
    private void Hidden() { }
}
