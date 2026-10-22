// Compiler crash (SIGSEGV, no diagnostic): a lambda inside a member of a C# 14 extension block.
// Happens even when the member is never called. .NET: 1
// Found in dotnet/docs docs/csharp/linq/snippets/HowToExtend (ExtensionMembers.cs).
using System;
using System.Collections.Generic;
using System.Linq;

Console.WriteLine(new[] { 3.0, 1.0, 2.0 }.Smallest());

static class E
{
    extension(IEnumerable<double> source)
    {
        public double Smallest() => source.OrderBy(n => n).First();
    }
}
