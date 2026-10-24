namespace Csharpc.Driver;

public enum EmitKind { Exe, Obj, Asm, LlvmIr, LlvmBc }

public sealed record CompilerOptions
{
    public List<string> Inputs { get; } = [];
    public string? Output { get; set; }
    public EmitKind Emit { get; set; } = EmitKind.Exe;
    public int OptLevel { get; set; } = 2;
    /// <summary>0 = optimize for speed, 1 = -Os, 2 = -Oz (smallest).</summary>
    public int SizeLevel { get; set; }
    /// <summary>Strip symbols from executables (null = default: strip unless -g).</summary>
    public bool? Strip { get; set; }
    /// <summary>Link a fully static executable (no libc.so at run time).</summary>
    public bool Static { get; set; }
    public string? Target { get; set; }
    public string? Cpu { get; set; }
    public string? Features { get; set; }
    public bool DumpCfg { get; set; }
    /// <summary>Print the decoded IL blocks of methods compiled from IL (--dump-il).</summary>
    public bool DumpIl { get; set; }
    /// <summary>--il-survey: lower every non-generic method of the IL assemblies, reachable or not (tools/il-survey).</summary>
    public bool IlSurvey { get; set; }
    /// <summary>Assemblies without sources (-r): bound against and compiled from IL (docs/adr/0010-il-frontend.md).</summary>
    public List<string> References { get; } = [];
    /// <summary>--framework-il: shared-framework assemblies forced to compile from IL (docs/adr/0013-framework-il.md).</summary>
    public List<string> FrameworkIl { get; } = [];
    /// <summary>--framework-il none: no automatic choice of framework assemblies to compile from IL.</summary>
    public bool FrameworkIlAutomatic { get; set; } = true;
    /// <summary>
    /// --corelib il|source: the corelib as a precompiled IL assembly lowered through the IL front-end
    /// (docs/adr/0015-corelib-il.md), or parsed and bound from its C# sources in every compilation.
    /// </summary>
    public bool CoreLibIl { get; set; } = Environment.GetEnvironmentVariable("CSHARPC_CORELIB") == "il"
        // CSHARPC_CHECK_CORELIB=1 validates the corelib's sources inside a program's compilation.
        && Environment.GetEnvironmentVariable("CSHARPC_CHECK_CORELIB") != "1";
    /// <summary>Emit DWARF debug info (-g; implied by -c Debug).</summary>
    public bool Debug { get; set; }
    public bool DebugExplicit { get; set; }
    public bool Verbose { get; set; }
    /// <summary>
    /// --stack-traces: record stack traces even when the program never turns an exception into text
    /// (they then appear in the unhandled-exception report on stderr). Otherwise automatic.
    /// </summary>
    public bool StackTraces { get; set; }
    public bool ShowWarnings { get; set; }
    public bool ImplicitUsings { get; set; } = true;
    public HardeningLevel Hardening { get; set; } = HardeningLevel.Default;
    /// <summary>Sanitizers for the C runtime (e.g. "address,undefined"); test builds only.</summary>
    public string? Sanitize { get; set; } = Environment.GetEnvironmentVariable("CSHARPC_SANITIZE") is { Length: > 0 } s ? s : null;
    public bool RuntimeLto { get; set; } = Environment.GetEnvironmentVariable("CSHARPC_NO_RUNTIME_LTO") != "1";
    public List<string> Defines { get; } = [];
    public string CC { get; set; } = Environment.GetEnvironmentVariable("CSHARPC_CC") ?? "clang";
    /// <summary>A .csproj to evaluate instead of loose files (drop-in mode).</summary>
    public string? Project { get; set; }
    /// <summary>
    /// Debug (default) or Release, as in `dotnet build`/`dotnet run`: selects the #if symbols
    /// (DEBUG/RELEASE) and project properties only. Optimization and debug info have their own
    /// options, so the default build is a Debug-configured program at -O2.
    /// </summary>
    public string Configuration { get; set; } = "Debug";
    public bool OptLevelExplicit { get; set; }
    public Dictionary<string, string> Properties { get; } = [];
    /// <summary>Default output name when the input is a project (its AssemblyName).</summary>
    public string? DefaultOutputName { get; set; }

    public const string Usage = """
        csharpc — a C# compiler front-end for LLVM

        Usage: csharpc [options] <file.cs>...
               csharpc [options] [<project.csproj> | <directory>]   (drop-in: compiles a .NET project)

        Options:
          -o, --output <path>    Output file, or directory (trailing '/' or existing dir) for <AssemblyName>
          -c, --configuration <c> Debug (default) | Release, like dotnet: #if symbols and project properties only
          -p:<Name>=<Value>      MSBuild global property used when evaluating a project
          --emit <kind>          exe (default) | obj | asm | llvm-ir | llvm-bc
          -O0 | -O1 | -O2 | -O3  Optimization level (default: -O2)
          -Os | -Oz              Optimize for size (-Oz: smallest), at -O2 speed otherwise
          --strip | --no-strip   Strip symbols from the executable (default: strip unless -g)
          --static               Link a fully static executable (no shared libc at run time)
          -g, --debug            Emit DWARF debug info (C# source-level debugging with gdb/lldb)
          -g0                    No debug info (default)
          --target <triple>      Target triple (default: host)
          --cpu <name>           Target CPU: native, x86-64-v3, znver4, ... (default: best x86-64-vN the host supports; host CPU on AArch64)
          --features <list>      Extra LLVM target features, e.g. +avx512f
          -D <symbol>            Define a preprocessor symbol
          -r, --reference <dll>  Reference an assembly that ships only IL; reachable code is compiled from its IL
          --framework-il <names> Compile these shared-framework assemblies from their IL (comma-separated);
                                 'none' turns off the automatic choice (assemblies the program uses that
                                 csharpc's corelib does not implement)
          --corelib <il|source>  Lower the corelib from its precompiled IL (cached) or bind its C# sources
                                 (default: source, or $CSHARPC_CORELIB)
          --no-implicit-usings   Don't add the SDK's implicit global usings
          --no-runtime-lto       Link the runtime as objects instead of LTO-ing its bitcode
          --stack-traces         Record stack traces in every program (default: only programs that print
                                 an exception or read Exception.StackTrace)
          --hardening <level>    none | default (full RELRO, NX, stack probes, hardened runtime) | max (+ CET IBT/SHSTK or BTI/PAC, SLH)
          --sanitize <list>      Build the runtime with sanitizers, e.g. address,undefined (testing; $CSHARPC_SANITIZE)
          --cc <path>            C compiler used as linker driver (default: $CSHARPC_CC or clang)
          --dump-cfg             Print Roslyn's control-flow graphs (csharpc's "MIR")
          --dump-il              Print the IL blocks of methods compiled from IL
          --il-survey            Also lower every non-generic method of the referenced IL assemblies (tools/il-survey)
          --intrinsics           List the library members csharpc implements
          -j, --jobs <n>         Threads one compilation may use (default: $CSHARPC_THREADS or the processor count)
          -W                     Show Roslyn warnings
          -v, --verbose          Print pipeline phases and timings
          --version              Print version information
          -h, --help             Show this help
        """;

    /// <summary>Returns null and prints the reason on invalid arguments.</summary>
    public static CompilerOptions? Parse(string[] args, out bool exitEarly)
    {
        exitEarly = false;
        var o = new CompilerOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for '{a}'");
            switch (a)
            {
                case "-h" or "--help": Console.WriteLine(Usage); exitEarly = true; return o;
                case "--version": Console.WriteLine($"csharpc {typeof(CompilerOptions).Assembly.GetName().Version} (LLVM C API, Roslyn front-end)"); exitEarly = true; return o;
                case "--intrinsics":
                    foreach (var k in CodeGen.Intrinsics.All) Console.WriteLine(k);
                    exitEarly = true;
                    return o;
                case "-o" or "--output": o.Output = Next(); break;
                case "--emit":
                    o.Emit = Next() switch
                    {
                        "exe" or "link" => EmitKind.Exe,
                        "obj" => EmitKind.Obj,
                        "asm" => EmitKind.Asm,
                        "llvm-ir" or "ll" => EmitKind.LlvmIr,
                        "llvm-bc" or "bc" => EmitKind.LlvmBc,
                        var k => throw new ArgumentException($"unknown --emit kind '{k}'"),
                    };
                    break;
                case "-O0" or "-O1" or "-O2" or "-O3": o.OptLevel = a[2] - '0'; o.OptLevelExplicit = true; break;
                case "-Os" or "-Oz": o.OptLevel = 2; o.SizeLevel = a == "-Os" ? 1 : 2; o.OptLevelExplicit = true; break;
                case "--strip": o.Strip = true; break;
                case "--no-strip": o.Strip = false; break;
                case "--static": o.Static = true; break;
                case "-c" or "--configuration": o.Configuration = Next(); break;
                case var _ when a.StartsWith("-p:", StringComparison.Ordinal) || a.StartsWith("/p:", StringComparison.Ordinal):
                {
                    var kv = a[3..];
                    var eq = kv.IndexOf('=');
                    if (eq <= 0) throw new ArgumentException($"expected -p:Name=Value, got '{a}'");
                    o.Properties[kv[..eq]] = kv[(eq + 1)..];
                    break;
                }
                case "--target": o.Target = Next(); break;
                case "--cpu": o.Cpu = Next(); break;
                case "--features": o.Features = Next(); break;
                case "-D": o.Defines.Add(Next()); break;
                case "-r" or "--reference": o.References.Add(Path.GetFullPath(Next())); break;
                case "--framework-il":
                    foreach (var name in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (name.Equals("none", StringComparison.OrdinalIgnoreCase)) o.FrameworkIlAutomatic = false;
                        else o.FrameworkIl.Add(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name);
                    break;
                case var _ when a.StartsWith("-r:", StringComparison.Ordinal) || a.StartsWith("/r:", StringComparison.Ordinal):
                    o.References.Add(Path.GetFullPath(a[3..]));
                    break;
                case "--corelib":
                    o.CoreLibIl = Next() switch
                    {
                        "il" => true,
                        "source" => false,
                        var other => throw new ArgumentException($"--corelib: expected 'il' or 'source', got '{other}'"),
                    };
                    break;
                case "--no-implicit-usings": o.ImplicitUsings = false; break;
                case "--no-runtime-lto": o.RuntimeLto = false; break;
                case "--stack-traces": o.StackTraces = true; break;
                case "--hardening": o.Hardening = Driver.Hardening.Parse(Next()); break;
                case "--sanitize": o.Sanitize = Next(); break;
                case var _ when a.StartsWith("--sanitize="): o.Sanitize = a["--sanitize=".Length..]; break;
                case var _ when a.StartsWith("--hardening="): o.Hardening = Driver.Hardening.Parse(a["--hardening=".Length..]); break;
                case "--cc": o.CC = Next(); break;
                case "--dump-cfg": o.DumpCfg = true; break;
                case "--dump-il": o.DumpIl = true; break;
                case "--il-survey": o.IlSurvey = true; break;
                case "-g" or "--debug": o.Debug = true; o.DebugExplicit = true; break;
                case "-g0": o.Debug = false; o.DebugExplicit = true; break;
                case "-W": o.ShowWarnings = true; break;
                case "-j" or "--jobs":
                    Parallelism.Threads = int.TryParse(Next(), out var jobs) && jobs > 0 ? jobs : throw new ArgumentException($"'{a}' expects a positive number of threads");
                    break;
                case var _ when a.StartsWith("-j", StringComparison.Ordinal) && int.TryParse(a[2..], out var inlineJobs) && inlineJobs > 0:
                    Parallelism.Threads = inlineJobs;
                    break;
                case "-v" or "--verbose": o.Verbose = true; break;
                case var _ when a.StartsWith('-'): throw new ArgumentException($"unknown option '{a}'");
                default: o.Inputs.Add(a); break;
            }
        }

        // Drop-in mode: a .csproj, a directory, or nothing (the project in the current directory).
        var projects = o.Inputs.Where(i => i.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || Directory.Exists(i)).ToList();
        if (projects.Count > 1 || (projects.Count == 1 && o.Inputs.Count > 1))
            throw new ArgumentException("pass either one project (or directory) or a list of .cs files, not both");
        if (projects.Count == 1)
        {
            var target = projects[0];
            o.Project = Directory.Exists(target)
                ? ProjectLoader.FindProject(target) ?? throw new ArgumentException($"no .csproj found in '{target}'")
                : target;
            o.Inputs.Clear();
        }
        else if (o.Inputs.Count == 0)
        {
            o.Project = ProjectLoader.FindProject(Directory.GetCurrentDirectory())
                ?? throw new ArgumentException("no input files (and no .csproj in the current directory)");
        }
        return o;
    }

    public string OutputPath
    {
        get
        {
            var name = DefaultOutputName ?? Path.GetFileNameWithoutExtension(Inputs.Count > 0 ? Inputs[0] : Project ?? "program");
            var file = name + Extension; // not ChangeExtension: "My.App" must stay "My.App"
            if (Output is null)
                // Projects build into <projectdir>/bin/csharpc/<AssemblyName> by default.
                return Project is not null ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Project))!, "bin", "csharpc", file) : file;
            if (Output.EndsWith('/') || Output.EndsWith(Path.DirectorySeparatorChar) || Directory.Exists(Output))
                return Path.Combine(Output, file);
            return Output;
        }
    }

    private string? Extension => Emit switch
    {
        EmitKind.Exe when Target is { } t && Csharpc.Llvm.TargetMachine.IsWasmTriple(t) => ".wasm",
        EmitKind.Exe => (Target is { } t ? Csharpc.Llvm.TargetMachine.IsWindowsTriple(t) : OperatingSystem.IsWindows()) ? ".exe" : null,
        EmitKind.Obj => ".o",
        EmitKind.Asm => ".s",
        EmitKind.LlvmIr => ".ll",
        EmitKind.LlvmBc => ".bc",
        _ => null,
    };
}
