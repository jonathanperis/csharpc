// The SDK defines target-framework preprocessor symbols (NET, NET11_0, NET10_0_OR_GREATER,
// NETCOREAPP, ...) in a build target, after evaluation; csharpc doesn't define them, so #if
// selects a different branch: silently wrong output. Affects .csproj input as well (csharpc reads
// DefineConstants from evaluation).
// Found in dotnet/docs docs/csharp/fundamentals/program-structure/snippets/preprocessor-directives.
using System;

#if NET10_0_OR_GREATER
Console.WriteLine("NET10_0_OR_GREATER");
#else
Console.WriteLine("not NET10_0_OR_GREATER");
#endif
#if NET
Console.WriteLine("NET");
#endif
#if NETCOREAPP
Console.WriteLine("NETCOREAPP");
#endif
#if NET11_0
Console.WriteLine("NET11_0");
#endif
#if RELEASE
Console.WriteLine("RELEASE");
#endif
