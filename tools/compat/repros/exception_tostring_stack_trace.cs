// Exception.ToString() (and string interpolation of an exception) includes the stack trace in .NET
// ("   at Program.<Main>$(String[]) in /path/file.cs:line N" lines); csharpc printed only
// "Type: Message". Fixed: docs/adr/0012-stack-traces.md.
// Found in dotnet/docs docs/standard/exceptions/snippets/how-to-use-the-try-catch-block-to-catch-exceptions.
using System;

try
{
    throw new InvalidOperationException("boom");
}
catch (Exception e)
{
    Console.WriteLine(e.ToString().Split('\n').Length > 1 ? "has stack trace" : "no stack trace");
}
