using System.Runtime.InteropServices;

namespace Csharpc.Driver;

/// <summary>
/// The NuGet / .NET runtime identifier (RID) of csharpc's target, which selects a package's runtimes/&lt;rid&gt;/lib and
/// runtimes/&lt;rid&gt;/native assets exactly as `dotnet build -r &lt;rid&gt;` / `PublishAot` do.
/// </summary>
public static class RuntimeIdentifiers
{
    /// <summary>The RID of a target triple, or null for a target no .NET runtime identifier names (WebAssembly).</summary>
    public static string? FromTriple(string triple)
    {
        var arm = triple.StartsWith("aarch64", StringComparison.Ordinal) || triple.StartsWith("arm64", StringComparison.Ordinal);
        var x64 = triple.StartsWith("x86_64", StringComparison.Ordinal);
        if (!arm && !x64) return null;
        var arch = arm ? "arm64" : "x64";
        if (triple.Contains("linux", StringComparison.Ordinal)) return (triple.Contains("musl", StringComparison.Ordinal) ? "linux-musl-" : "linux-") + arch;
        if (triple.Contains("apple", StringComparison.Ordinal) || triple.Contains("darwin", StringComparison.Ordinal) || triple.Contains("macos", StringComparison.Ordinal)) return "osx-" + arch;
        if (triple.Contains("windows", StringComparison.Ordinal) || triple.Contains("mingw", StringComparison.Ordinal)) return "win-" + arch;
        return null;
    }

    /// <summary>The RID of the machine csharpc runs on (a target when none is given).</summary>
    public static string Host =>
        (OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsMacOS() ? "osx-" : "linux-")
        + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    /// <summary>
    /// The RIDs whose assets a target can use, best first: its own, then the compatible ones of the runtime graph
    /// (Microsoft.NETCore.Platforms): `linux-x64` falls back to `linux`, `unix-x64`, `unix` and `any`.
    /// </summary>
    public static IReadOnlyList<string> Fallbacks(string rid)
    {
        var dash = rid.LastIndexOf('-');
        var os = dash < 0 ? rid : rid[..dash];
        var arch = dash < 0 ? "" : rid[(dash + 1)..];
        List<string> chain = [rid];
        // Distro and version qualifiers (ubuntu.24.04-x64, win10-x64) fall back to their family first.
        if (os.StartsWith("linux-musl", StringComparison.Ordinal)) chain.Add("linux-musl");
        if (os is "linux" or "linux-musl" || os.StartsWith("linux-", StringComparison.Ordinal)) { if (!chain.Contains("linux")) chain.Add("linux"); if (arch.Length > 0) chain.Add("unix-" + arch); chain.Add("unix"); }
        else if (os == "osx" || os.StartsWith("osx.", StringComparison.Ordinal)) { chain.Add("osx"); if (arch.Length > 0) chain.Add("unix-" + arch); chain.Add("unix"); }
        else if (os == "win" || os.StartsWith("win", StringComparison.Ordinal)) { if (!chain.Contains("win")) chain.Add("win"); }
        chain.Add("any");
        return [.. chain.Distinct()];
    }
}
