// The SDK's target-framework preprocessor symbols for net11.0 (added by a build target, not by
// project evaluation) must be defined, or #if silently picks another branch.
using System;

#if NET
Console.WriteLine("NET");
#endif
#if NET11_0
Console.WriteLine("NET11_0");
#endif
#if NET10_0
Console.WriteLine("NET10_0");
#endif
#if NETCOREAPP
Console.WriteLine("NETCOREAPP");
#endif
#if NET5_0_OR_GREATER && NET8_0_OR_GREATER && NET10_0_OR_GREATER && NET11_0_OR_GREATER
Console.WriteLine("NET5..11_OR_GREATER");
#endif
#if NET12_0_OR_GREATER
Console.WriteLine("NET12_0_OR_GREATER");
#endif
#if NETCOREAPP1_0_OR_GREATER && NETCOREAPP2_1_OR_GREATER && NETCOREAPP3_1_OR_GREATER
Console.WriteLine("NETCOREAPPx_OR_GREATER");
#endif
#if NETSTANDARD || NETFRAMEWORK || NET48
Console.WriteLine("netstandard/netframework");
#endif
#if TRACE
Console.WriteLine("TRACE");
#endif
