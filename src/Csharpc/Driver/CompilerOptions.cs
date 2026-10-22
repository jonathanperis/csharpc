namespace Csharpc.Driver;

public enum EmitKind { Exe, Obj, Asm, LlvmIr, LlvmBc }

public sealed record CompilerOptions
{
    public List<string> Inputs { get; } = [];
    public string? Output { get; set; }
    public EmitKind Emit { get; set; } = EmitKind.Exe;
    public int OptLevel { get; set; } = 2;
    public string? Target { get; set; }
    public string? Cpu { get; set; }
    public string? Features { get; set; }
    public bool DumpCfg { get; set; }
    public bool Verbose { get; set; }
    public bool ShowWarnings { get; set; }
    public bool ImplicitUsings { get; set; } = true;
    public bool RuntimeLto { get; set; } = Environment.GetEnvironmentVariable("CSHARPC_NO_RUNTIME_LTO") != "1";
    public List<string> Defines { get; } = [];
    public string CC { get; set; } = Environment.GetEnvironmentVariable("CSHARPC_CC") ?? "clang";

    public const string Usage = """
        csharpc — a C# compiler front-end for LLVM

        Usage: csharpc [options] <file.cs>...

        Options:
          -o, --output <path>    Output file
          --emit <kind>          exe (default) | obj | asm | llvm-ir | llvm-bc
          -O0 | -O1 | -O2 | -O3  Optimization level (default: -O2)
          --target <triple>      Target triple (default: host)
          --cpu <name>           Target CPU: native, x86-64-v3, znver4, ... (default: best x86-64-vN the host supports)
          --features <list>      Extra LLVM target features, e.g. +avx512f
          -D <symbol>            Define a preprocessor symbol
          --no-implicit-usings   Don't add the SDK's implicit global usings
          --no-runtime-lto       Link the runtime as objects instead of LTO-ing its bitcode
          --cc <path>            C compiler used as linker driver (default: $CSHARPC_CC or clang)
          --dump-cfg             Print Roslyn's control-flow graphs (csharpc's "MIR")
          --intrinsics           List the library members csharpc implements
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
                case "-O0" or "-O1" or "-O2" or "-O3": o.OptLevel = a[2] - '0'; break;
                case "--target": o.Target = Next(); break;
                case "--cpu": o.Cpu = Next(); break;
                case "--features": o.Features = Next(); break;
                case "-D": o.Defines.Add(Next()); break;
                case "--no-implicit-usings": o.ImplicitUsings = false; break;
                case "--no-runtime-lto": o.RuntimeLto = false; break;
                case "--cc": o.CC = Next(); break;
                case "--dump-cfg": o.DumpCfg = true; break;
                case "-W": o.ShowWarnings = true; break;
                case "-v" or "--verbose": o.Verbose = true; break;
                case var _ when a.StartsWith('-'): throw new ArgumentException($"unknown option '{a}'");
                default: o.Inputs.Add(a); break;
            }
        }
        if (o.Inputs.Count == 0) throw new ArgumentException("no input files");
        return o;
    }

    public string OutputPath => Output ?? Path.ChangeExtension(Path.GetFileName(Inputs[0]), Emit switch
    {
        EmitKind.Exe => OperatingSystem.IsWindows() ? ".exe" : null,
        EmitKind.Obj => ".o",
        EmitKind.Asm => ".s",
        EmitKind.LlvmIr => ".ll",
        EmitKind.LlvmBc => ".bc",
        _ => null,
    });
}
