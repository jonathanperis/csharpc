using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Csharpc.Driver;

/// <summary>
/// Linking. Like rustc and gollvm, csharpc delegates the final link to the system C toolchain
/// (clang as a linker driver), which knows the platform's crt objects, libc and linker flags.
/// </summary>
public sealed class Toolchain(CompilerOptions options, Csharpc.Llvm.TargetMachine target)
{
    /// <summary>-fexceptions: C# exceptions unwind through runtime frames (and, with runtime LTO,
    /// keeps clang from marking csrt functions nounwind, which would delete landing pads).</summary>
    private static readonly string[] BaseRuntimeCFlags = ["-O2", "-std=c11", "-fPIC", "-funwind-tables", "-fexceptions"];

    private string[] RuntimeCFlags => [.. BaseRuntimeCFlags, .. Hardening.RuntimeCFlags(options.Hardening, target), .. SanitizerFlags];

    /// <summary>--sanitize: instrument the runtime (generated code is bounds/null-checked by construction).</summary>
    private string[] SanitizerFlags => options.Sanitize is { } list
        ? [$"-fsanitize={list}", "-fno-omit-frame-pointer", "-g", "-fno-sanitize-recover=undefined"]
        : [];

    private static string RuntimeDir => Path.Combine(AppContext.BaseDirectory, "runtime");

    /// <summary>Compiles every src/runtime/*.c once per (sources, target, cc) and caches the objects.</summary>
    public string[] RuntimeObjects(string triple) => CompileRuntime(triple, bitcode: false);

    /// <summary>
    /// The runtime as LLVM bitcode, for whole-program LTO: linked into the program module before
    /// optimization so allocation and other hot helpers inline into user code (see RuntimeLto).
    /// </summary>
    public string[] RuntimeBitcode(string triple)
    {
        // The per-file bitcode is merged once into a single cached module, so each compile parses
        // and links one file instead of ~20 (link-runtime time roughly halves).
        var parts = CompileRuntime(triple, bitcode: true);
        var merged = Path.Combine(Path.GetDirectoryName(parts[0])!, "runtime-merged-" + Path.GetFileNameWithoutExtension(parts[0]).Split('-')[^1] + ".bc");
        if (!File.Exists(merged))
        {
            var tmp = merged + "." + Environment.ProcessId + ".tmp";
            RuntimeLto.Merge(parts, tmp);
            File.Move(tmp, merged, overwrite: true);
        }
        return [merged];
    }

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
        string[] sanitize = options.Sanitize is { } list ? [$"-fsanitize={list}"] : [];
        Run(options.CC, [$"--target={triple}", programObject, .. runtime, "-lm", "-pthread", .. LinkFlags(), .. Hardening.LinkFlags(options.Hardening), .. sanitize, "-o", output]);
    }

    /// <summary>
    /// Size-oriented link flags (see benchmarks/RESULTS.md, "Size and startup"): lld when available
    /// (it doesn't pad segments to page boundaries in the file), section GC, safe identical-code folding
    /// (`safe` keeps functions whose address is taken distinct, which delegate equality relies on),
    /// packed RELR relocations for PIE when the host glibc understands them, and stripping.
    /// </summary>
    private IEnumerable<string> LinkFlags()
    {
        var lld = HasLld;
        if (lld) yield return "-fuse-ld=lld";
        yield return "-Wl,--gc-sections";
        if (lld) yield return "-Wl,--icf=safe";
        if (options.Static) yield return StaticFlag;
        else if (lld && options.Target is null && HostGlibcAtLeast(2, 36)) yield return "-Wl,-z,pack-relative-relocs";
        if (options.Strip ?? !options.Debug) yield return "-s";
    }

    private static bool? s_hasLld;

    private bool HasLld => s_hasLld ??= Environment.GetEnvironmentVariable("CSHARPC_LD") switch
    {
        "lld" => true,
        { Length: > 0 } => false,
        _ => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(dir => dir.Length > 0 && File.Exists(Path.Combine(dir, "ld.lld"))),
    };

    /// <summary>static-pie keeps ASLR for static executables (glibc supports it via rcrt1.o).</summary>
    private const string StaticFlag = "-static-pie";

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "gnu_get_libc_version")]
    private static extern nint GnuGetLibcVersion();

    private static bool HostGlibcAtLeast(int major, int minor)
    {
        try
        {
            if (!OperatingSystem.IsLinux()) return false;
            var text = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(GnuGetLibcVersion()) ?? "";
            return Version.TryParse(text, out var v) && (v.Major > major || (v.Major == major && v.Minor >= minor));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; } // musl etc.
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
