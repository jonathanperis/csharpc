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

    /// <summary>Compiles every runtime/*.c once per (sources, target, cc) and caches the objects.</summary>
    public string[] RuntimeObjects(string triple)
    {
        if (!Directory.Exists(RuntimeDir)) throw new FileNotFoundException($"csharpc runtime not found at '{RuntimeDir}'");
        var files = Directory.GetFiles(RuntimeDir).Where(f => f.EndsWith(".c") || f.EndsWith(".h")).Order().ToArray();
        var hashInput = string.Concat(files.Select(File.ReadAllText)) + triple + options.CC;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16].ToLowerInvariant();
        var cacheDir = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
            "csharpc");
        Directory.CreateDirectory(cacheDir);

        List<string> objects = [];
        foreach (var source in files.Where(f => f.EndsWith(".c")))
        {
            var obj = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(source)}-{hash}.o");
            objects.Add(obj);
            if (File.Exists(obj)) continue;
            var tmp = obj + "." + Environment.ProcessId + ".tmp";
            Run(options.CC, ["-c", "-O2", "-std=c11", "-fPIC", "-funwind-tables", $"--target={triple}", source, "-o", tmp]);
            File.Move(tmp, obj, overwrite: true);
        }
        return [.. objects];
    }

    public void Link(string triple, string programObject, string output)
    {
        Run(options.CC, [$"--target={triple}", programObject, .. RuntimeObjects(triple), "-lm", "-o", output]);
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
