using System;
using System.IO;

Environment.SetEnvironmentVariable("CSHARPC_SAMPLE_VAR", "hello world");
Console.WriteLine(Environment.GetEnvironmentVariable("CSHARPC_SAMPLE_VAR"));
Environment.SetEnvironmentVariable("CSHARPC_SAMPLE_VAR", null);
Console.WriteLine(Environment.GetEnvironmentVariable("CSHARPC_SAMPLE_VAR") == null);
Console.WriteLine(Environment.GetEnvironmentVariable("PATH") != null);
Console.WriteLine(Environment.ProcessId > 0);
Console.WriteLine(Environment.MachineName.Length > 0);
Console.WriteLine(Environment.Is64BitProcess);
string cwd = Environment.CurrentDirectory;
Console.WriteLine(cwd == Directory.GetCurrentDirectory());
Directory.SetCurrentDirectory("/");
Console.WriteLine(Environment.CurrentDirectory);
Console.WriteLine(Path.GetFullPath("tmp/../usr"));
try { Directory.SetCurrentDirectory("/no/such/dir"); } catch (DirectoryNotFoundException e) { Console.WriteLine(e.Message); }
Environment.CurrentDirectory = cwd;
Console.WriteLine(Environment.CurrentDirectory == cwd);
Console.Out.WriteLine("exiting with 3");
Environment.ExitCode = 3;
