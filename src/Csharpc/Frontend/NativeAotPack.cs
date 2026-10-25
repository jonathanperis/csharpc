using System.Diagnostics;

namespace Csharpc.Frontend;

/// <summary>
/// .NET's native shims (libSystem.IO.Compression.Native, ...) as NativeAOT links them: static archives from
/// the NativeAOT runtime pack (Microsoft.NETCore.App.Runtime.NativeAOT.&lt;rid&gt;, the version of the shared
/// framework csharpc binds against). Framework assemblies that P/Invoke into a shim can then be compiled
/// from IL (FrameworkIl) and their calls bind directly to the shim, which carries the same native code
/// (zlib-ng, brotli, zstd) as a NativeAOT executable: compressed bytes are .NET's. The pack is taken from
/// the NuGet packages folder, or downloaded into it once with `dotnet restore` (a PackageDownload, which
/// honours the user's NuGet configuration).
/// </summary>
public static class NativeAotPack
{
    /// <summary>A shim the IL calls (DllImport module name) and the archives that implement it, in link order.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Shims = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["libSystem.IO.Compression.Native"] = ["libSystem.IO.Compression.Native.a", "libbrotlienc.a", "libbrotlidec.a", "libbrotlicommon.a", "libz.a", "libzstd.a"],
        // POSIX file metadata and the like (System.Formats.Tar, ZipFile's permissions), on NativeAOT's minipal.
        ["libSystem.Native"] = ["libSystem.Native.a", "libaotminipal.a"],
    };

    /// <summary>
    /// Assemblies allowed to bind a shim, where not every caller is known to work: libSystem.Native also
    /// carries .NET's signal handling and child-process machinery (System.Diagnostics.Process, pipes, the
    /// file system watcher), which would compete with csharpc's runtime — only file-metadata users for now.
    /// </summary>
    private static readonly Dictionary<string, string[]> s_shimCallers = new(StringComparer.Ordinal)
    {
        ["libSystem.Native"] = ["System.Formats.Tar", "System.IO.Compression.ZipFile"],
    };

    /// <summary>Whether <paramref name="assembly"/> may be compiled from IL with its calls into <paramref name="shim"/> linked.</summary>
    public static bool MayCall(string assembly, string shim) =>
        Shims.ContainsKey(shim) && (!s_shimCallers.TryGetValue(shim, out var callers) || callers.Contains(assembly, StringComparer.Ordinal));

    /// <summary>Whether the pack's native directory has every archive of <paramref name="shim"/>.</summary>
    public static bool HasShim(string nativeDirectory, string shim) =>
        Shims.TryGetValue(shim, out var archives) && archives.All(a => File.Exists(Path.Combine(nativeDirectory, a)));

    private static readonly Lock s_lock = new();
    private static readonly Dictionary<string, string?> s_directories = new(StringComparer.Ordinal);

    /// <summary>The runtime identifier NativeAOT publishes for on this target, or null where csharpc can't link its archives (Windows: MSVC libraries; WebAssembly).</summary>
    public static string? Rid(string triple) => triple switch
    {
        _ when triple.Contains("-apple-", StringComparison.Ordinal) => triple.StartsWith("arm64", StringComparison.Ordinal) || triple.StartsWith("aarch64", StringComparison.Ordinal) ? "osx-arm64" : null,
        _ when triple.Contains("windows", StringComparison.Ordinal) || triple.Contains("wasm", StringComparison.Ordinal) || triple.Contains("musl", StringComparison.Ordinal) => null,
        _ when triple.Contains("-linux", StringComparison.Ordinal) => triple.StartsWith("x86_64", StringComparison.Ordinal) ? "linux-x64"
            : triple.StartsWith("aarch64", StringComparison.Ordinal) || triple.StartsWith("arm64", StringComparison.Ordinal) ? "linux-arm64" : null,
        _ => null,
    };

    /// <summary>
    /// The pack's native directory (runtimes/&lt;rid&gt;/native) for <paramref name="rid"/> and the shared
    /// framework's version, restoring the pack if needed; null (with the reason) when it can't be had.
    /// </summary>
    public static string? NativeDirectory(string rid, out string? reason)
    {
        reason = null;
        if (SharedFramework.ProductVersion is not { } version)
        {
            reason = "the .NET 11 shared framework was not found";
            return null;
        }
        var key = rid + "|" + version;
        lock (s_lock)
        {
            if (s_directories.TryGetValue(key, out var known))
            {
                if (known is null) reason = $"the NativeAOT runtime pack {PackageId(rid)} {version} could not be restored";
                return known;
            }
            var dir = Find(rid, version) ?? (Restore(rid, version) ? Find(rid, version) : null);
            s_directories[key] = dir;
            if (dir is null) reason = $"the NativeAOT runtime pack {PackageId(rid)} {version} could not be restored";
            return dir;
        }
    }

    private static string PackageId(string rid) => "Microsoft.NETCore.App.Runtime.NativeAOT." + rid;

    private static string PackagesFolder =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } explicitFolder ? explicitFolder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    private static string? Find(string rid, string version)
    {
        var dir = Path.Combine(PackagesFolder, PackageId(rid).ToLowerInvariant(), version.ToLowerInvariant(), "runtimes", rid, "native");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>`dotnet restore` of a project that only downloads the pack (into the NuGet packages folder).</summary>
    private static bool Restore(string rid, string version)
    {
        try
        {
            // One directory per process: compiles running in parallel may restore at the same time (NuGet
            // itself serializes the extraction into the packages folder).
            var cache = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "csharpc", "nativeaot-pack",
                rid + "-" + version + "-" + Environment.ProcessId);
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "restore.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageDownload Include="{PackageId(rid)}" Version="[{version}]" />
                  </ItemGroup>
                </Project>
                """);
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet";
            var psi = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cache };
            foreach (var a in new[] { "restore", "restore.csproj", "-nologo", "-v", "quiet" }) psi.ArgumentList.Add(a);
            foreach (var k in new[] { "MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly" }) psi.Environment.Remove(k);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = stderr.Result;
            try { Directory.Delete(cache, recursive: true); } catch (IOException) { }
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
