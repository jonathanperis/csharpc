namespace Csharpc.Driver;

public enum EmitKind { Exe, Obj, Asm, LlvmIr, LlvmBc }

public sealed record CompilerOptions
{
    public List<string> Inputs { get; } = [];
    public string? Output { get; set; }
    public EmitKind Emit { get; set; } = EmitKind.Exe;
    public int OptLevel { get; set; } = 2;
    public string? Target { get; set; }
    public bool DumpCfg { get; set; }
    public bool Verbose { get; set; }
    public bool ShowWarnings { get; set; }
    public bool ImplicitUsings { get; set; } = true;
    public List<string> Defines { get; } = [];
    public string CC { get; set; } = Environment.GetEnvironmentVariable("CSHARPC_CC") ?? "clang";
    /// <summary>A .csproj to evaluate instead of loose files (drop-in mode).</summary>
    public string? Project { get; set; }
    public string Configuration { get; set; } = "Release";
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
          -c, --configuration <c> Debug (-O0) | Release (-O2, default); explicit -O wins
          -p:<Name>=<Value>      MSBuild global property used when evaluating a project
          --emit <kind>          exe (default) | obj | asm | llvm-ir | llvm-bc
          -O0 | -O1 | -O2 | -O3  Optimization level (default: -O2)
          --target <triple>      Target triple (default: host)
          -D <symbol>            Define a preprocessor symbol
          --no-implicit-usings   Don't add the SDK's implicit global usings
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
                case "-O0" or "-O1" or "-O2" or "-O3": o.OptLevel = a[2] - '0'; o.OptLevelExplicit = true; break;
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
                case "-D": o.Defines.Add(Next()); break;
                case "--no-implicit-usings": o.ImplicitUsings = false; break;
                case "--cc": o.CC = Next(); break;
                case "--dump-cfg": o.DumpCfg = true; break;
                case "-W": o.ShowWarnings = true; break;
                case "-v" or "--verbose": o.Verbose = true; break;
                case var _ when a.StartsWith('-'): throw new ArgumentException($"unknown option '{a}'");
                default: o.Inputs.Add(a); break;
            }
        }
        if (!o.OptLevelExplicit)
            o.OptLevel = o.Configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) ? 0 : 2;

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
        EmitKind.Exe => OperatingSystem.IsWindows() ? ".exe" : null,
        EmitKind.Obj => ".o",
        EmitKind.Asm => ".s",
        EmitKind.LlvmIr => ".ll",
        EmitKind.LlvmBc => ".bc",
        _ => null,
    };
}
