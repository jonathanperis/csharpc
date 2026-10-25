// Environment.Version, RuntimeInformation and the SDK-generated assembly attributes of the program
// (docs-core-tools-global-tools-how-to-create-dotnet-env). Machine-specific values are only checked
// for consistency, so the recording holds on every OS and architecture.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

Version v = Environment.Version;
Console.WriteLine($"Version: major {v.Major}, minor {v.Minor}, build {v.Build}, revision {v.Revision}");
Console.WriteLine($"FrameworkDescription starts with '.NET {v}': {RuntimeInformation.FrameworkDescription.StartsWith(".NET " + v, StringComparison.Ordinal)}");

string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsWasi() ? "wasi" : "linux";
string rid = os == "wasi" ? "wasi-wasm" : os + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
Console.WriteLine($"RuntimeIdentifier matches the OS and architecture: {RuntimeInformation.RuntimeIdentifier == rid}");
Console.WriteLine($"OSArchitecture is the process architecture: {RuntimeInformation.OSArchitecture == RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"OSDescription is set: {RuntimeInformation.OSDescription.Length > 0}");
Console.WriteLine($"ProcessorCount is positive: {Environment.ProcessorCount > 0}");

Console.WriteLine(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) == OperatingSystem.IsLinux());
Console.WriteLine(RuntimeInformation.IsOSPlatform(OSPlatform.Create("linux")) == OperatingSystem.IsLinux());
Console.WriteLine(OSPlatform.Create("Windows") == OSPlatform.Windows);
Console.WriteLine(OSPlatform.Create("FreeBSD").Equals(OSPlatform.Linux));
Console.WriteLine(OSPlatform.OSX);
try { OSPlatform.Create(""); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }

Assembly entry = Assembly.GetEntryAssembly()!;
Console.WriteLine(entry.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
Console.WriteLine(entry.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
Console.WriteLine(entry.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
Console.WriteLine(entry.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration);
Console.WriteLine(entry.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
// "1.0.0", plus "+<commit>" inside a git repository.
string? informational = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
Console.WriteLine(informational == "1.0.0" || informational?.StartsWith("1.0.0+", StringComparison.Ordinal) == true);
Console.WriteLine(entry.GetCustomAttribute<AssemblyCopyrightAttribute>() is null);
var framework = entry.GetCustomAttribute<TargetFrameworkAttribute>();
Console.WriteLine($"{framework?.FrameworkName} ({framework?.FrameworkDisplayName})");
Console.WriteLine(entry.GetName().Version);
