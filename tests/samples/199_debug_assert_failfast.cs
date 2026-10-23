// A failed Debug.Assert in a Debug build (the default configuration) prints "Process terminated.
// Assertion failed." with its messages to stderr and fails fast: SIGABRT, exit status 134.
using System.Diagnostics;

Debug.Assert(true, "fine");
Console.WriteLine("before");
Console.Out.Flush();
Debug.Assert(1 + 1 == 3, "math is broken", "detail here");
Console.WriteLine("after");
