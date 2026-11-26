// Looking up processes (#102): GetProcessById, GetProcesses, ProcessName, Modules and MainModule, for this process and for a child.
// The program's own name depends on how it was launched, so it is compared rather than printed.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using var current = Process.GetCurrentProcess();
using var byId = Process.GetProcessById(current.Id);
var exeName = Path.GetFileName(Environment.ProcessPath!);
Console.WriteLine($"same id: {byId.Id == current.Id}, not the same object: {!ReferenceEquals(byId, current)}");
Console.WriteLine($"name is the executable's: {current.ProcessName == Path.GetFileNameWithoutExtension(exeName)}, by id too: {byId.ProcessName == current.ProcessName}");
Console.WriteLine($"has not exited: {!byId.HasExited}");

var main = current.MainModule!;
Console.WriteLine($"main module is the executable: {main.FileName == Environment.ProcessPath}, {main.ModuleName == exeName}");
Console.WriteLine($"main module first: {ReferenceEquals(current.Modules[0], main)}, count > 0: {current.Modules.Count > 0}");
Console.WriteLine($"module text: {main.ToString() == $"System.Diagnostics.ProcessModule ({exeName})"}");
Console.WriteLine($"modules enumerate: {current.Modules.Cast<ProcessModule>().Count() == current.Modules.Count}");

var all = Process.GetProcesses();
Console.WriteLine($"GetProcesses has this process: {all.Any(p => p.Id == current.Id)}, more than one: {all.Length > 1}");
foreach (var p in all) p.Dispose();

// /bin/sleep: by name, PATH may find another sleep (macOS CI puts GNU coreutils' gsleep first), whose own name the lookup reports.
var start = new ProcessStartInfo("/bin/sleep") { ArgumentList = { "30" } };
using (var child = Process.Start(start)!)
{
    using var found = Process.GetProcessById(child.Id);
    Console.WriteLine($"child: {child.ProcessName}, by id: {found.ProcessName}");
    child.Kill();
    child.WaitForExit();
    Console.WriteLine($"child exited: {child.HasExited}");
    try { Console.WriteLine(child.ProcessName); }
    catch (InvalidOperationException e) { Console.WriteLine("child name after exit: " + e.Message.Replace(child.Id.ToString(), "<pid>")); }
}

try { Process.GetProcessById(int.MaxValue); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }

// kill(0, 0) succeeds (it checks this program's process group), so .NET's lookup of id 0 succeeds too.
using (var group = Process.GetProcessById(0)) Console.WriteLine($"id 0 looked up: {group.Id}");

try { Console.WriteLine(new Process().ProcessName); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
