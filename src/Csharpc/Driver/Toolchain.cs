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
    /// <summary>-fexceptions: C# exceptions unwind through runtime frames (and, with runtime LTO,
    /// keeps clang from marking csrt functions nounwind, which would delete landing pads).</summary>
    private static readonly string[] RuntimeCFlags = ["-O2", "-std=c11", "-fPIC", "-funwind-tables", "-fexceptions"];

    private static string RuntimeDir => Path.Combine(AppContext.BaseDirectory, "runtime");

    /// <summary>Compiles every src/runtime/*.c once per (sources, target, cc) and caches the objects.</summary>
    public string[] RuntimeObjects(string triple) => CompileRuntime(triple, bitcode: false);

    /// <summary>
    /// The runtime as LLVM bitcode, for whole-program LTO: linked into the program module before
    /// optimization so allocation and other hot helpers inline into user code (see RuntimeLto).
    /// </summary>
    public string[] RuntimeBitcode(string triple) => CompileRuntime(triple, bitcode: true);

    private string[] CompileRuntime(string triple, bool bitcode)
    {
        if (!Directory.Exists(RuntimeDir)) throw new FileNotFoundException($"csharpc runtime not found at '{RuntimeDir}'");
        var files = Directory.GetFiles(RuntimeDir).Where(f => f.EndsWith(".c") || f.EndsWith(".h")).Order().ToArray();
        // Key the cache on everything that affects the output, including the flags below.
        var hashInput = string.Concat(files.Select(File.ReadAllText)) + triple + options.CC + string.Join(' ', RuntimeCFlags) + (bitcode ? "bc" : "o");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16].ToLowerInvariant();
        var cacheDir = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
            "csharpc");
        Directory.CreateDirectory(cacheDir);

        List<string> outputs = [];
        foreach (var source in files.Where(f => f.EndsWith(".c")))
        {
            var output = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(source)}-{hash}{(bitcode ? ".bc" : ".o")}");
            outputs.Add(output);
            if (File.Exists(output)) continue;
            var tmp = output + "." + Environment.ProcessId + ".tmp";
            string[] mode = bitcode ? ["-emit-llvm", "-c"] : ["-c"];
            Run(options.CC, [.. mode, .. RuntimeCFlags, $"--target={triple}", source, "-o", tmp]);
            File.Move(tmp, output, overwrite: true);
        }
        return [.. outputs];
    }

    /// <param name="runtimeLinkedIn">The runtime was already linked into the program as bitcode (LTO).</param>
    public void Link(string triple, string programObject, string output, bool runtimeLinkedIn = false)
    {
        string[] runtime = runtimeLinkedIn ? [] : RuntimeObjects(triple);
        Run(options.CC, [$"--target={triple}", programObject, .. runtime, "-lm", "-pthread", "-o", output]);
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
