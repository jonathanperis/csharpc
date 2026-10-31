// Process.Start on Linux and macOS (csharpc's driver starts clang and dotnet this way): ArgumentList, the
// working directory and environment, redirected standard output and error, exit codes (128 + the signal
// for a killed child), a program that doesn't exist, and output larger than a pipe's buffer. It runs sh, so
// it is a Unix program (tests/platform-skips.txt).
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = "/" };
psi.ArgumentList.Add("-c");
psi.ArgumentList.Add("pwd; echo out $CSHARPC_TEST_VALUE; echo err 1>&2; exit 3");
psi.Environment["CSHARPC_TEST_VALUE"] = "value";
using (var process = Process.Start(psi)!)
{
    var stderr = process.StandardError.ReadToEndAsync();
    var stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    Console.WriteLine($"out: [{stdout.Replace('\n', '|')}] err: [{stderr.Result.Replace('\n', '|')}] exit {process.ExitCode} exited {process.HasExited}");
}

var large = new ProcessStartInfo("sh") { RedirectStandardOutput = true, RedirectStandardError = true };
large.ArgumentList.Add("-c");
large.ArgumentList.Add("i=0; while [ $i -lt 20000 ]; do echo line$i; i=$((i+1)); done; echo done 1>&2");
using (var process = Process.Start(large)!)
{
    var stderr = process.StandardError.ReadToEndAsync();
    var lines = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    process.WaitForExit();
    Console.WriteLine($"large: {lines.Length} last {lines.Last()} err {stderr.Result.Trim()}");
}

var killed = new ProcessStartInfo("sh");
killed.ArgumentList.Add("-c");
killed.ArgumentList.Add("kill -9 $$");
using (var process = Process.Start(killed)!)
{
    process.WaitForExit();
    Console.WriteLine($"signal: {process.ExitCode}");
}

try { Process.Start(new ProcessStartInfo("csharpc-no-such-program-731")); }
catch (Win32Exception e) { Console.WriteLine($"missing: {e.NativeErrorCode} {e.Message.StartsWith("An error occurred trying to start process 'csharpc-no-such-program-731' with working directory", StringComparison.Ordinal)}"); }

try { _ = new Process().ExitCode; }
catch (InvalidOperationException e) { Console.WriteLine($"not started: {e.Message}"); }
