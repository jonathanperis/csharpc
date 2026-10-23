// csharpc's default configuration is Debug, like `dotnet run` and `dotnet build`: DEBUG and TRACE are
// defined and RELEASE is not, at every optimization level (-O picks the optimizer, -c the symbols).
#if DEBUG
Console.WriteLine("DEBUG");
#endif
#if RELEASE
Console.WriteLine("RELEASE");
#endif
#if TRACE
Console.WriteLine("TRACE");
#endif
#if DEBUG && !RELEASE
Console.WriteLine("Debug configuration");
#endif
