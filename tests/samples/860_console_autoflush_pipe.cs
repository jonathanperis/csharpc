// Console.Out writes through on every call, as .NET's autoflushing console writer does: a parent reads the
// child's first line while the child is still waiting on its input (a block-buffered stdout would hold it
// back until exit, and a killed process would lose it). Then the parent closes the pipe and the child keeps
// writing: .NET ignores SIGPIPE and the console drops writes to a closed pipe (EPIPE), so the child goes on
// to its exit code instead of being killed by the signal. It starts itself, so it is a Unix program
// (tests/platform-skips.txt).
using System;
using System.Diagnostics;
using System.IO;

if (args.Length > 0 && args[0] == "child")
{
    Console.Write("ready");
    Console.Out.Write(':');
    Console.WriteLine(1);
    if (Console.ReadLine() != null) return 1;
    for (int i = 0; i < 100000; i++) Console.WriteLine($"line {i}");
    Console.Out.WriteLine("after the pipe closed");
    Console.Error.WriteLine("child still running");
    return 3;
}

var self = Environment.ProcessPath!;
var psi = new ProcessStartInfo(self) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
// Under `dotnet app.dll` the process is the host: run the same assembly again.
if (Path.GetFileNameWithoutExtension(self) == "dotnet") psi.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
psi.ArgumentList.Add("child");
using var child = Process.Start(psi)!;
var stderr = child.StandardError.ReadToEndAsync();
Console.WriteLine($"first line while the child waits: {child.StandardOutput.ReadLine()}");
child.StandardOutput.Close();
child.StandardInput.Close();
child.WaitForExit();
Console.WriteLine($"child exit code {child.ExitCode}, stderr: {stderr.Result.Trim()}");
return 0;
