// Environment.FailFast: what was written to stdout stays, no finally block or exception filter runs, and the
// process ends by SIGABRT (exit status 134), as .NET's does; the message goes to stderr.
using System;

Console.WriteLine("before");
try
{
    Environment.FailFast("csharpc fail-fast test");
}
finally
{
    Console.WriteLine("finally must not run");
}
Console.WriteLine("after must not run");
