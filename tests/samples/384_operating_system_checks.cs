// OperatingSystem's platform checks answer for the OS the program runs on (constants of the target in
// csharpc). The checks are combined so the printed line is the same on every OS.
bool[] checks = { OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), OperatingSystem.IsWasi() };
Console.WriteLine(checks.Count(c => c) == 1);
Console.WriteLine(OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsFreeBSD());
Console.WriteLine(OperatingSystem.IsOSPlatform("windows") == OperatingSystem.IsWindows());
Console.WriteLine(OperatingSystem.IsOSPlatform("LINUX") == OperatingSystem.IsLinux());
Console.WriteLine(OperatingSystem.IsOSPlatform("osx") == OperatingSystem.IsMacOS() && OperatingSystem.IsOSPlatform("MacOS") == OperatingSystem.IsMacOS());
Console.WriteLine(OperatingSystem.IsOSPlatform("Plan9"));
try { OperatingSystem.IsOSPlatform(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
