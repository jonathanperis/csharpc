// Runtime-facing members ASP.NET Core's hosting and Microsoft.Extensions.DependencyInjection read at
// start-up: the app's base directory and executable path, the dynamic-code feature switches (false
// for a file-based app, which publishes with NativeAOT), stack probing, the debugger flag and
// Timeout.InfiniteTimeSpan; plus StringBuilder's range replacements.
using System.Runtime.CompilerServices;
using System.Text;

string? exe = Environment.ProcessPath;
string baseDirectory = AppContext.BaseDirectory;
Console.WriteLine(exe is null || baseDirectory == Path.GetDirectoryName(exe) + Path.DirectorySeparatorChar);
Console.WriteLine(baseDirectory.Length == 0 || Path.EndsInDirectorySeparator(baseDirectory));
Console.WriteLine(exe is null || (Path.IsPathFullyQualified(exe) && File.Exists(exe)));
Console.WriteLine(ReferenceEquals(AppContext.BaseDirectory, AppContext.BaseDirectory));
Console.WriteLine($"{RuntimeFeature.IsDynamicCodeSupported} {RuntimeFeature.IsDynamicCodeCompiled}");
Console.WriteLine(System.Diagnostics.Debugger.IsAttached);

Console.WriteLine(RuntimeHelpers.TryEnsureSufficientExecutionStack());
RuntimeHelpers.EnsureSufficientExecutionStack();
Console.WriteLine(Depth(0));

Console.WriteLine($"{Timeout.InfiniteTimeSpan} {Timeout.InfiniteTimeSpan.TotalMilliseconds} {Timeout.InfiniteTimeSpan == TimeSpan.FromMilliseconds(Timeout.Infinite)}");

var sb = new StringBuilder("a-b-c-d");
sb.Replace('-', '+', 2, 3);
Console.WriteLine(sb);
sb.Replace("+", "::", 0, 5).Replace("a", "A", 0, 1);
Console.WriteLine(sb);
sb.Replace("::", "", 3, 3);
Console.WriteLine(sb);
try { sb.Replace('x', 'y', 4, 99); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { sb.Replace("x", "y", -1, 0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { sb.Replace("", "y", 0, 0); } catch (ArgumentException e) { Console.WriteLine(e.Message); }

// Recursion that probes the stack the way DependencyInjection's call-site resolver does: it stops when the
// remaining stack gets short instead of overflowing.
static int Depth(int depth) => depth < 100_000 && RuntimeHelpers.TryEnsureSufficientExecutionStack() ? Depth(depth + 1) : (depth > 1000 ? 1000 : depth);
