using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Csharpc.Driver;

/// <summary>
/// Linking. Like rustc and gollvm, csharpc delegates the final link to the system C toolchain
/// (clang as a linker driver), which knows the platform's crt objects, libc and linker flags.
/// </summary>
public sealed class Toolchain(CompilerOptions options)
{
    private static string RuntimeDir => Path.Combine(AppContext.BaseDirectory, "runtime");

    /// <summary>Compiles csrt.c once per (source, target, cc) and caches the object file.</summary>
    public string RuntimeObject(string triple)
    {
        var source = Path.Combine(RuntimeDir, "csrt.c");
        if (!File.Exists(source)) throw new FileNotFoundException($"csharpc runtime source not found at '{source}'");

        var hashInput = File.ReadAllText(source) + File.ReadAllText(Path.Combine(RuntimeDir, "csrt.h")) + triple + options.CC;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16].ToLowerInvariant();
        var cacheDir = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
            "csharpc");
        Directory.CreateDirectory(cacheDir);
        var obj = Path.Combine(cacheDir, $"csrt-{hash}.o");
        if (File.Exists(obj)) return obj;

        var tmp = obj + "." + Environment.ProcessId + ".tmp";
        Run(options.CC, ["-c", "-O2", "-std=c11", "-fPIC", $"--target={triple}", source, "-o", tmp]);
        File.Move(tmp, obj, overwrite: true);
        return obj;
    }

    public void Link(string triple, string programObject, string output)
    {
        Run(options.CC, [$"--target={triple}", programObject, RuntimeObject(triple), "-lm", "-o", output]);
    }

    private void Run(string tool, string[] args)
    {
        if (options.Verbose) Console.Error.WriteLine($"  $ {tool} {string.Join(' ', args)}");
        var psi = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process process;
        try { process = Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException($"could not run '{tool}' ({e.Message}); install clang or pass --cc", e);
        }
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{tool}' failed with exit code {process.ExitCode}:\n{stdout}{stderr.Result}");
    }
}
