using System.Diagnostics;

namespace Csharpc.Frontend;

/// <summary>
/// .NET's native shims (libSystem.IO.Compression.Native, ...) as NativeAOT links them: static archives from
/// the NativeAOT runtime pack (Microsoft.NETCore.App.Runtime.NativeAOT.&lt;rid&gt;, the version of the shared
/// framework csharpc binds against). Framework assemblies that P/Invoke into a shim can then be compiled
/// from IL (FrameworkIl) and their calls bind directly to the shim, which carries the same native code
/// (zlib-ng, brotli, zstd) as a NativeAOT executable: compressed bytes are .NET's. The pack is taken from
/// the NuGet packages folder, or downloaded into it once with `dotnet restore` (a PackageDownload, which
/// honours the user's NuGet configuration). On Windows the archives are the pack's MSVC-built .lib files,
/// which lld links into the MinGW executable next to Toolchain's msvc_compat.c (the few vcruntime symbols
/// they reference); the shim's IL module name has no "lib" prefix there.
/// </summary>
public static class NativeAotPack
{
    /// <summary>A shim the IL calls (DllImport module name) and the archives that implement it, in link order.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Shims = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["libSystem.IO.Compression.Native"] = ["libSystem.IO.Compression.Native.a", "libbrotlienc.a", "libbrotlidec.a", "libbrotlicommon.a", "libz.a", "libzstd.a"],
        // Windows: the same code, named without the "lib" prefix in the framework's IL; zlib-ng, brotli and zstd as
        // separate MSVC libraries (System.IO.Compression.Native.Aot.lib holds only the entry points).
        ["System.IO.Compression.Native"] = ["System.IO.Compression.Native.Aot.lib", "brotlienc.lib", "brotlidec.lib", "brotlicommon.lib", "zlibstatic.lib", "zstd_static.lib"],
        // POSIX file metadata and the like (System.Formats.Tar, ZipFile's permissions), on NativeAOT's minipal.
        ["libSystem.Native"] = ["libSystem.Native.a", "libaotminipal.a"],
        // macOS native TLS (Frontend/NativeTls.cs, docs/adr/0022-tls.md): Security.framework / Network.framework / CryptoKit behind
        // the AppleCryptoNative_* entry points, and GSS.framework behind NetSecurityNative_*. Only in native TLS mode (see MayCall).
        ["libSystem.Security.Cryptography.Native.Apple"] = ["libSystem.Security.Cryptography.Native.Apple.a"],
        ["libSystem.Net.Security.Native"] = ["libSystem.Net.Security.Native.a"],
    };

    /// <summary>The shims only the opt-in native TLS mode binds: on Linux the same module names belong to the OpenSSL-era IL, which csharpc shadows.</summary>
    private static readonly string[] s_nativeTlsShims = ["libSystem.Security.Cryptography.Native.Apple", "libSystem.Net.Security.Native"];

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
        Shims.ContainsKey(shim) && (!s_nativeTlsShims.Contains(shim, StringComparer.Ordinal) || NativeTls.On)
        && (!s_shimCallers.TryGetValue(shim, out var callers) || callers.Contains(assembly, StringComparer.Ordinal));

    /// <summary>Whether the pack's native directory has every archive of <paramref name="shim"/>.</summary>
    public static bool HasShim(string nativeDirectory, string shim) =>
        Shims.TryGetValue(shim, out var archives) && archives.All(a => File.Exists(Path.Combine(nativeDirectory, a)));

    private static readonly Lock s_lock = new();
    private static readonly Dictionary<string, string?> s_directories = new(StringComparer.Ordinal);

    /// <summary>The runtime identifier NativeAOT publishes for on this target, or null where csharpc can't link its archives (Windows on Arm; WebAssembly).</summary>
    public static string? Rid(string triple) => triple switch
    {
        _ when triple.Contains("-apple-", StringComparison.Ordinal) => triple.StartsWith("arm64", StringComparison.Ordinal) || triple.StartsWith("aarch64", StringComparison.Ordinal) ? "osx-arm64" : null,
        _ when triple.Contains("windows", StringComparison.Ordinal) => triple.StartsWith("x86_64", StringComparison.Ordinal) ? "win-x64" : null,
        _ when triple.Contains("wasm", StringComparison.Ordinal) || triple.Contains("musl", StringComparison.Ordinal) => null,
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
                if (known is null) reason = RestoreFailure(rid, version);
                return known;
            }
            // A restore can fail for reasons that pass (a network hiccup, NuGet's own locking while a compile running in
            // parallel restores the same pack): retry, and look in the packages folder again each time, because that other
            // compile may have finished it.
            var dir = Find(rid, version);
            for (var attempt = 0; dir is null && attempt < 3; attempt++)
            {
                if (attempt > 0) System.Threading.Thread.Sleep(2000 * attempt);
                Restore(rid, version);
                dir = Find(rid, version);
            }
            s_directories[key] = dir;
            if (dir is null) reason = RestoreFailure(rid, version);
            return dir;
        }
    }

    /// <summary>`dotnet restore`'s last error output, so a diagnostic says why the pack is missing.</summary>
    private static string s_lastRestoreError = "";

    private static string RestoreFailure(string rid, string version) =>
        $"the NativeAOT runtime pack {PackageId(rid)} {version} could not be restored" + (s_lastRestoreError.Length > 0 ? $" ({s_lastRestoreError})" : "");

    private static string PackageId(string rid) => "Microsoft.NETCore.App.Runtime.NativeAOT." + rid;

    private static string PackagesFolder =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } explicitFolder ? explicitFolder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    private static string? Find(string rid, string version)
    {
        var package = Path.Combine(PackagesFolder, PackageId(rid).ToLowerInvariant(), version.ToLowerInvariant());
        var dir = Path.Combine(package, "runtimes", rid, "native");
        // NuGet writes .nupkg.metadata as the last file of an extraction: without it another compile running in parallel (the
        // differential shards run 3 samples at once) may still be extracting, and the archives would be judged missing.
        return Directory.Exists(dir) && File.Exists(Path.Combine(package, ".nupkg.metadata")) ? dir : null;
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
            // A compile run under a memory cap (tools/il-survey sets DOTNET_GCHeapHardLimit and an address-space limit for the
            // compile) passes both to this child, whose GC then fails to reserve 1.5 times the limit ("Reserving 15 GiB ... failed")
            // and the pack, so System.IO.Compression, is missing. A pack download needs a fraction of that.
            if (psi.Environment.ContainsKey("DOTNET_GCHeapHardLimit")) psi.Environment["DOTNET_GCHeapHardLimit"] = "0x80000000";
            foreach (var k in new[] { "MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly" }) psi.Environment.Remove(k);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var error = stderr.Result.Trim();
            try { Directory.Delete(cache, recursive: true); } catch (IOException) { }
            if (process.ExitCode != 0)
                s_lastRestoreError = $"dotnet restore exited with {process.ExitCode}" + (error.Length > 0 ? ": " + error.Split('\n')[0].Trim() : "");
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            s_lastRestoreError = e.Message;
            return false;
        }
    }
}
