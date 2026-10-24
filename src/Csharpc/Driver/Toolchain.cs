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
    /// keeps clang from marking csrt functions nounwind, which would delete landing pads).
    /// -ffp-contract=off: clang's default fuses a*b+c into one FMA wherever the target has it
    /// (always on AArch64), which rounds differently from .NET's separate multiply and add.</summary>
    private static readonly string[] BaseRuntimeCFlags = ["-O2", "-std=c11", "-fPIC", "-funwind-tables", "-fexceptions", "-ffp-contract=off"];

    private static readonly string[] PortedCFlags = ["-DNDEBUG"];
    private string[] RuntimeCFlags => target.IsWasm
        ? [.. WasmRuntimeCFlags, .. Hardening.RuntimeCFlags(options.Hardening, target, lld: false)]
        : [.. BaseRuntimeCFlags, .. SysrootFlags, .. (target.IsWindows ? WindowsRuntimeCFlags : []), .. Hardening.RuntimeCFlags(options.Hardening, target, HasLld), .. SanitizerFlags];

    /// <summary>
    /// Windows: the UCRT's own printf family. _GNU_SOURCE/_POSIX_C_SOURCE would otherwise select
    /// MinGW's C99 replacement (__mingw_vfprintf: ~20 KB of pformat and gdtoa in every executable);
    /// the runtime only formats strings and integers, which the UCRT does the same way.
    /// </summary>
    private static readonly string[] WindowsRuntimeCFlags = ["-D__USE_MINGW_ANSI_STDIO=0"];

    /// <summary>wasm32: nothing unwinds (ExceptionLowering), executables are static (no -fPIC).</summary>
    private static readonly string[] WasmRuntimeCFlags = ["-O2", "-std=c11", "-ffp-contract=off"];

    /// <summary>
    /// The WASI SDK (clang, wasm-ld, wasi-libc, compiler-rt): $WASI_SDK_PATH, else /opt/wasi-sdk. Its
    /// clang must be LLVM 18, like csharpc's (wasi-sdk 22-24), because the runtime's bitcode is linked
    /// into the program.
    /// </summary>
    private static string WasiSdk => Environment.GetEnvironmentVariable("WASI_SDK_PATH") is { Length: > 0 } sdk ? sdk : "/opt/wasi-sdk";

    /// <summary>The C compiler and linker driver: --cc / $CSHARPC_CC, else clang (the WASI SDK's for wasm32).</summary>
    private string CC => target.IsWasm && options.CC == "clang" && Environment.GetEnvironmentVariable("CSHARPC_CC") is null
        ? Path.Combine(WasiSdk, "bin", OperatingSystem.IsWindows() ? "clang.exe" : "clang")
        : options.CC;

    /// <summary>macOS: headers and libSystem stubs come from the SDK (clang also honours $SDKROOT).</summary>
    private string[] SysrootFlags => target.IsDarwin && MacSdk is { } sdk ? ["-isysroot", sdk] : [];

    private static string? s_macSdk;

    /// <summary>$SDKROOT, else `xcrun --sdk macosx --show-sdk-path` (macOS hosts with the Command Line Tools).</summary>
    private string? MacSdk => s_macSdk ??= Environment.GetEnvironmentVariable("SDKROOT") is { Length: > 0 } root ? root
        : OperatingSystem.IsMacOS() ? Capture("xcrun", ["--sdk", "macosx", "--show-sdk-path"]).Trim()
        : throw new InvalidOperationException($"targeting '{target.Triple}' needs the macOS SDK: set SDKROOT to its path");

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
        // Subdirectories hold ported native code (globalization/: .NET's System.Globalization.Native).
        var files = Directory.GetFiles(RuntimeDir, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".c") || f.EndsWith(".h")).Order(StringComparer.Ordinal).ToArray();
        // Key the cache on everything that affects the output, including the flags below.
        var hashInput = string.Concat(files.Select(File.ReadAllText)) + triple + CC + string.Join(' ', RuntimeCFlags) + string.Join(' ', PortedCFlags) + (bitcode ? "bc" : "o");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16].ToLowerInvariant();
        var cacheDir = CacheDir;

        List<string> outputs = [];
        List<(string Source, string Output)> missing = [];
        // WASI has no dynamic loading, so no ICU: programs run in invariant globalization mode there and the
        // ICU shim isn't built (the corelib's Interop.Globalization has stubs for it).
        foreach (var source in files.Where(f => f.EndsWith(".c")
                     && !(target.IsWasm && Path.GetFileName(Path.GetDirectoryName(f)) == "globalization")))
        {
            var output = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(source)}-{hash}{(bitcode ? ".bc" : ".o")}");
            outputs.Add(output);
            if (!File.Exists(output)) missing.Add((source, output));
        }
        // A cache miss (first compile, or the runtime changed) compiles every file: one clang per thread.
        try { Parallel.ForEach(missing, Parallelism.Options, file => CompileRuntimeFile(file.Source, file.Output, bitcode, triple)); }
        catch (AggregateException e) { throw e.InnerExceptions[0]; }
        return [.. outputs];
    }

    private void CompileRuntimeFile(string source, string output, bool bitcode, string triple)
    {
        var tmp = output + "." + Environment.ProcessId + "." + Environment.CurrentManagedThreadId + ".tmp";
        string[] mode = bitcode ? ["-emit-llvm", "-c"] : ["-c"];
        // Ported .NET native code is built as .NET ships it: release, asserts off (they flag conditions
        // the code then handles, such as ICU failing to open a string search).
        string[] ported = Path.GetDirectoryName(source) != RuntimeDir.TrimEnd(Path.DirectorySeparatorChar) ? PortedCFlags : [];
        Run(CC, [.. mode, .. RuntimeCFlags, .. ported, $"--target={triple}", source, "-o", tmp]);
        File.Move(tmp, output, overwrite: true);
    }

    /// <summary>~/.cache/csharpc (or $XDG_CACHE_HOME/csharpc): the runtime objects and the corelib assembly.</summary>
    internal static string CacheDirectory => CacheDir;

    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
                "csharpc");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>lld's --symbol-ordering-file for 4 KiB-granular separate code (Hardening.AlignsSegments), else null.</summary>
    private string? SegmentOrderFile()
    {
        if (!Hardening.AlignsSegments(options.Hardening, target, HasLld)) return null;
        var path = Path.Combine(CacheDir, "segment-order.txt");
        if (!File.Exists(path) || File.ReadAllText(path) != Hardening.SegmentOrder)
        {
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, Hardening.SegmentOrder);
            File.Move(tmp, path, overwrite: true);
        }
        return path;
    }

    /// <param name="programObjects">The program's objects (several when code generation ran in parallel).</param>
    /// <param name="runtimeLinkedIn">The runtime was already linked into the program as bitcode (LTO).</param>
    /// <param name="weakImports">Undefined extern_weak symbols (P/Invoke targets that may be missing).</param>
    public void Link(string triple, IReadOnlyList<string> programObjects, string output, bool runtimeLinkedIn = false, IReadOnlyList<string>? weakImports = null)
    {
        string[] runtime = runtimeLinkedIn ? [] : RuntimeObjects(triple);
        string[] sanitize = options.Sanitize is { } list ? [$"-fsanitize={list}"] : [];
        // MinGW's linker writes exactly the name it is given; gcc and .NET name executables *.exe.
        if (target.IsWindows && Path.GetExtension(output).Length == 0) output += ".exe";
        if (target.IsDarwin)
        {
            // Apple's linker rejects undefined symbols even when weak; -U lets each bind to null at
            // run time (EntryPointNotFoundException at the call, as on Linux).
            var weak = (weakImports ?? []).SelectMany(name => new[] { "-Wl,-U,_" + name });
            Run(options.CC, [$"--target={triple}", .. SysrootFlags, .. programObjects, .. runtime, .. DarwinLinkFlags(), .. weak, .. sanitize, "-o", output]);
            // DWARF stays in the object files on macOS (the executable only records where): collect it
            // into output.dSYM before the temporary program object is deleted.
            if (options.Debug) Run(Environment.GetEnvironmentVariable("CSHARPC_DSYMUTIL") ?? "dsymutil", [output]);
            return;
        }
        if (target.IsWasm)
        {
            if (Path.GetExtension(output).Length == 0) output += ".wasm";
            Run(CC, [$"--target={triple}", .. programObjects, .. runtime, .. WasmLinkFlags(), "-o", output]);
            return;
        }
        if (target.IsWindows)
        {
            Run(options.CC, [$"--target={triple}", .. programObjects, .. runtime, .. WindowsLinkFlags(), .. Hardening.WindowsLinkFlags(options.Hardening), .. sanitize, "-o", output]);
            return;
        }
        Run(options.CC, [$"--target={triple}", .. programObjects, .. runtime, "-lm", "-pthread", .. LinkFlags(), .. Hardening.LinkFlags(options.Hardening, SegmentOrderFile()), .. sanitize, "-o", output]);
    }

    /// <summary>
    /// wasm-ld through the WASI SDK's clang (crt1-command.o, wasi-libc, compiler-rt builtins). The stack
    /// (8 MiB, the default main-thread stack of .NET on Linux) comes first in linear memory, so an
    /// overflow traps at address 0 instead of overwriting static data; the GC scans it (gc.c).
    /// </summary>
    private IEnumerable<string> WasmLinkFlags()
    {
        yield return "-Wl,--gc-sections";
        yield return "-Wl,--stack-first";
        yield return "-Wl,-z,stack-size=8388608";
        if (options.Strip ?? !options.Debug) yield return "-Wl,--strip-all";
    }

    /// <summary>
    /// PE/COFF counterparts of <see cref="LinkFlags"/>, for the MinGW-w64 toolchain (LLVM-MinGW: clang,
    /// lld, compiler-rt, libunwind, the UCRT). Everything but system DLLs is linked statically
    /// (-static: libunwind), so the executable runs without LLVM-MinGW's DLLs; the UCRT ships with
    /// Windows 10 and later. The runtime and libraries are named explicitly so a plain clang (not
    /// LLVM-MinGW's target wrapper) links the same way. No -pthread: the runtime implements the
    /// pthread subset it uses over Win32 (src/runtime/csrt_pthread.h), so winpthreads (~40 KB) is
    /// not linked, and a new use of a pthread function fails to link instead of pulling it back in.
    /// </summary>
    private IEnumerable<string> WindowsLinkFlags()
    {
        yield return "-fuse-ld=lld";
        yield return "-rtlib=compiler-rt";
        yield return "-unwindlib=libunwind";
        yield return "-static";
        yield return "-Wl,--gc-sections";
        yield return "-Wl,--icf=safe";
        // CoTaskMemAlloc/CoTaskMemFree: P/Invoke string returns and Marshal.*CoTaskMem*, as in .NET.
        yield return "-lole32";
        yield return "-lws2_32";  // System.Net.Sockets over Winsock
        if (options.Strip ?? !options.Debug) yield return "-s";
    }

    /// <summary>
    /// Mach-O counterparts of <see cref="LinkFlags"/>: dead-stripping instead of section GC and local-
    /// and debug-symbol stripping (ld64 has no full -s). Apple's ld by default; CSHARPC_LD=lld selects
    /// ld64.lld (e.g. to cross-link from Linux). Hardening needs no linker flags: Mach-O executables are
    /// always PIE with a non-executable stack, and __DATA_CONST is read-only after binding.
    /// </summary>
    private IEnumerable<string> DarwinLinkFlags()
    {
        if (Environment.GetEnvironmentVariable("CSHARPC_LD") == "lld") yield return "-fuse-ld=lld";
        yield return "-Wl,-dead_strip";
        if (options.Strip ?? !options.Debug) yield return "-Wl,-S,-x";
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

    private string Capture(string tool, string[] args)
    {
        var psi = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0) return stdout;
        }
        catch (System.ComponentModel.Win32Exception) { }
        throw new InvalidOperationException($"could not find the macOS SDK ('{tool} {string.Join(' ', args)}' failed); install the Xcode Command Line Tools or set SDKROOT");
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
