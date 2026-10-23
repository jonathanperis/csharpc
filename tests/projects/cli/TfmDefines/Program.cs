#if MY_FEATURE
System.Console.WriteLine("MY_FEATURE");
#endif
#if RELEASE
System.Console.WriteLine("RELEASE");
#endif
#if DEBUG
System.Console.WriteLine("DEBUG");
#endif
#if TRACE
System.Console.WriteLine("TRACE");
#endif
#if NET && NET11_0 && NETCOREAPP
System.Console.WriteLine("NET NET11_0 NETCOREAPP");
#endif
#if NET10_0_OR_GREATER && NET11_0_OR_GREATER && NETCOREAPP3_1_OR_GREATER
System.Console.WriteLine("OR_GREATER");
#endif
#if NET12_0_OR_GREATER || NET10_0
System.Console.WriteLine("wrong");
#endif
