using System.Diagnostics;
using Csharpc.CodeGen;
using Csharpc.Frontend;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.Driver;

/// <summary>
/// The csharpc pipeline (docs/02-architecture.md):
///
///   C# source ──Roslyn──▶ bound tree / IOperation ──Roslyn──▶ ControlFlowGraph ("MIR")
///             ──csharpc codegen──▶ LLVM IR ──LLVM opt──▶ object ──clang/ld──▶ native executable
/// </summary>
public sealed class Compiler(CompilerOptions options)
{
    private readonly Stopwatch _clock = new();

    public int Run()
    {
        _clock.Start();
        // The target decides the corelib's per-OS code (CSHARPC_TARGET_*) before anything is parsed.
        var triple = options.Target ?? TargetMachine.HostTriple;
        var darwin = TargetMachine.IsDarwinTriple(triple);
        var windows = TargetMachine.IsWindowsTriple(triple);
        var wasm = TargetMachine.IsWasmTriple(triple);
        if (wasm && WasmTargetError(triple) is { } wasmError) return Fail(wasmError);
        if (wasm) options.Target = triple = "wasm32-unknown-wasip1"; // clang's spelling (the runtime's bitcode)
        if (wasm && options.Sanitize is not null)
            return Fail("--sanitize is not supported on WebAssembly (there is no sanitizer runtime for wasm32-wasip1)");
        if (wasm && options.Hardening == HardeningLevel.Max)
            return Fail("--hardening max is not supported on WebAssembly: the engine already enforces control-flow integrity (structured control flow, typed indirect calls, return addresses outside linear memory); use the default level");
        if (windows && !triple.StartsWith("x86_64", StringComparison.Ordinal))
            return Fail($"target '{triple}': Windows on this architecture is not supported by csharpc yet (x86-64 only)");
        if (windows && (triple.EndsWith("-msvc", StringComparison.Ordinal) || triple.EndsWith("-windows", StringComparison.Ordinal)))
            return Fail($"target '{triple}': csharpc targets Windows through MinGW-w64 (x86_64-w64-windows-gnu); the MSVC environment is not supported yet");
        if (darwin && !(triple.StartsWith("arm64", StringComparison.Ordinal) || triple.StartsWith("aarch64", StringComparison.Ordinal)))
            return Fail($"target '{triple}': macOS on x86-64 is not supported by csharpc yet (Apple silicon / arm64 only)");
        if (darwin && options.Static)
            return Fail("--static is not supported on macOS: Apple does not support statically linked executables (libSystem is always linked dynamically)");
        string[] coreLibDefines = [wasm ? "CSHARPC_TARGET_WASI" : windows ? "CSHARPC_TARGET_WINDOWS" : darwin ? "CSHARPC_TARGET_MACOS" : "CSHARPC_TARGET_LINUX"];
        FrontendSettings settings;
        if (options.Project is { } project)
        {
            var (projectSettings, root, all) = ProjectLoader.Load(project, options.Configuration, options.Verbose ? Console.Error : null, options.Properties);
            Phase($"evaluate project ({all.Count} project(s), {projectSettings.Files.Count} file(s))");
            if (!root.OutputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) && !root.OutputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)
                && options.Emit == EmitKind.Exe)
                return Fail($"'{Path.GetFileName(project)}' has OutputType={root.OutputType}; csharpc builds executables (use --emit obj for a library object)");
            foreach (var p in all.Where(p => p.UnresolvedReferences.Count > 0))
                return Fail($"{Path.GetFileName(p.ProjectPath)}: <Reference> not found: {string.Join(", ", p.UnresolvedReferences)} (build the referenced assembly first, or give it a HintPath)");
            options.DefaultOutputName = root.AssemblyName;
            settings = projectSettings with { Defines = [.. projectSettings.Defines, .. options.Defines], CoreLibDefines = coreLibDefines };
        }
        else
        {
            settings = new FrontendSettings
            {
                Files = options.Inputs,
                Defines = [.. ProjectLoader.ImplicitDefines(options.Configuration), .. options.Defines],
                GlobalUsings = options.ImplicitUsings ? RoslynFrontend.DefaultImplicitUsings : null,
                CoreLibDefines = coreLibDefines,
                RuntimeConfig = options.Inputs.Count > 0 ? [.. ProjectLoader.FileBasedRuntimeConfig(options.Inputs[0])] : [],
                AssemblyVersion = options.Inputs.Count > 0 ? ProjectLoader.FileBasedAssemblyVersion(options.Inputs[0]) : "1.0.0.0",
            };
        }
        // Assemblies without sources: bound against like any reference, compiled from IL (ADR 0010).
        IlAssemblySet il;
        try { il = IlAssemblySet.Load([.. settings.IlReferencePaths, .. options.References]); }
        catch (Exception e) when (e is InvalidOperationException or IOException or BadImageFormatException) { return Fail(e.Message); }
        using var ilScope = il;
        if (!il.IsEmpty)
        {
            settings = settings with { IlReferences = il.References };
            Phase($"load {il.Assemblies.Count} IL assembl{(il.Assemblies.Count == 1 ? "y" : "ies")}");
        }
        // Nullable analysis only produces warnings, never different code. Lowering uses a compilation
        // with the nullable context disabled: with it enabled, Roslyn's CFG for `lock (System.Threading.Lock)`
        // inside lambdas degrades to an Invalid operation. Warnings (-W) still come from the real settings.
        var analyzed = RoslynFrontend.Analyze(settings);
        var compilation = (Microsoft.CodeAnalysis.CSharp.CSharpCompilation)analyzed.WithOptions(
            analyzed.Options.WithNullableContextOptions(Microsoft.CodeAnalysis.NullableContextOptions.Disable));
        var diagnostics = RoslynFrontend.UserDiagnostics(options.ShowWarnings ? analyzed : compilation);
        if (settings.Projects.Count > 1)
        {
            // Referenced projects are folded into one compilation for codegen (ADR 0006), but C#
            // checks each project on its own, as `dotnet build` does.
            var perProject = RoslynFrontend.ProjectDiagnostics(settings);
            var mergedOnly = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !RoslynFrontend.DefiniteAssignmentErrors.Contains(d.Id)).ToList();
            diagnostics = options.ShowWarnings ? perProject : [.. perProject.Where(d => d.Severity == DiagnosticSeverity.Error)];
            if (!perProject.Any(d => d.Severity == DiagnosticSeverity.Error) && mergedOnly.Count > 0)
            {
                foreach (var d in mergedOnly) WriteDiagnostic(d.ToString(), isError: true);
                return Fail("these errors only appear when the referenced projects are compiled together; csharpc folds ProjectReferences into one program and can't compile this combination yet");
            }
        }
        Phase("parse + bind (Roslyn)");

        var errors = 0;
        foreach (var d in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || (options.ShowWarnings && d.Severity == DiagnosticSeverity.Warning)))
        {
            WriteDiagnostic(d.ToString(), d.Severity == DiagnosticSeverity.Error);
            if (d.Severity == DiagnosticSeverity.Error) errors++;
        }
        // Entry-point errors (several Mains, a bad <StartupObject>) are compilation-level diagnostics
        // that the per-tree diagnostics above don't include; only look for them when there's no entry
        // point, since collecting them binds the whole corelib.
        if (errors == 0 && options.Emit == EmitKind.Exe && compilation.GetEntryPoint(CancellationToken.None) is null)
            foreach (var d in compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id is "CS0017" or "CS1555" or "CS1556" or "CS1558" or "CS5001" or "CS7022"))
            {
                WriteDiagnostic(d.ToString(), isError: true);
                errors++;
            }
        if (errors > 0) return Fail($"{errors} error(s) from the C# front-end");

        using var llvm = new LlvmContext();
        using var target = TargetMachine.Create(options.Target, options.OptLevel switch
        {
            0 => LLVMCodeGenOptLevel.None,
            1 => LLVMCodeGenOptLevel.Less,
            3 => LLVMCodeGenOptLevel.Aggressive,
            _ => LLVMCodeGenOptLevel.Default,
        }, options.Cpu, options.Features);
        using var module = new LlvmModule(llvm, compilation.AssemblyName ?? "program");
        module.ConfigureFor(target);
        module.SourceFileName = settings.Files.Count > 0 ? settings.Files[0] : "program";

        // Codegen binds against a nullable-oblivious copy of the compilation: nullability never affects
        // generated code, and Roslyn's nullable re-inference can surface error types in IOperation
        // (e.g. `Select` over an array of tuples with mixed element names yields `Select<T, ?>`).
        // IL assemblies are imported with their private and internal members: layouts need every field.
        var codegenOptions = compilation.Options.WithNullableContextOptions(NullableContextOptions.Disable);
        if (!il.IsEmpty) codegenOptions = codegenOptions.WithMetadataImportOptions(MetadataImportOptions.All);
        var codegenCompilation = MethodGroupRewriter.Rewrite(compilation.WithOptions(codegenOptions));
        if (!il.IsEmpty) il.Bind(codegenCompilation);
        // Stack traces cost a landing pad per call site: only for programs that can look at one.
        StackTraceInfo? stackTraces = null;
        if (options.StackTraces || StackTraceInfo.FindObservation(compilation) is not null)
        {
            var symbols = settings.Symbols ?? new DebugSymbolSettings { Optimize = options.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase) };
            stackTraces = new StackTraceInfo(compilation, symbols);
            Phase($"stack-trace metadata (emit + PDB, {(stackTraces.HasPdb ? "ok" : "failed")})");
        }
        var codegen = new ModuleCodeGen(codegenCompilation, llvm, module, target)
        {
            StackTraces = stackTraces,
            CfgDump = options.DumpCfg ? Console.Out : null,
            IlDump = options.DumpIl ? Console.Out : null,
            Il = il,
            IlSurvey = options.IlSurvey,
            RuntimeConfig = settings.RuntimeConfig,
            ProgramAssemblies = ProgramAssembly.From(settings, codegenCompilation.AssemblyName ?? "program"),
        };
        using var debugInfo = options.Debug
            ? codegen.Debug = new DebugInfo(codegen, settings.Files.Count > 0 ? settings.Files[0] : "program.cs", options.OptLevel > 0)
            : null;
        try { codegen.Generate(); }
        catch (CompileError e) { codegen.Errors.Add(e); }
        debugInfo?.Finish();
        Phase("lower CFG -> LLVM IR");
        if (options.Verbose) Console.Error.WriteLine($"             {codegen.Stats}");

        if (stackTraces is not null) codegen.Warnings.AddRange(stackTraces.Warnings);
        foreach (var w in codegen.Warnings.DistinctBy(w => w.Format()))
        {
            var text = w.Format();
            WriteDiagnostic(text.StartsWith("error ", StringComparison.Ordinal) ? "warning " + text["error ".Length..] : text.Replace(": error ", ": warning "), isError: false);
        }
        if (codegen.Errors.Count > 0)
        {
            var distinct = codegen.Errors.DistinctBy(e => e.Format()).ToList();
            foreach (var e in distinct) WriteDiagnostic(e.Format(), isError: true);
            return Fail($"{distinct.Count} construct(s) not supported by csharpc yet");
        }

        if (!module.Verify(out var verifyError))
        {
            var dump = Path.ChangeExtension(options.OutputPath, ".invalid.ll");
            File.WriteAllText(dump, module.PrintToString());
            Console.Error.WriteLine($"internal compiler error: csharpc produced invalid LLVM IR (written to {dump}):\n{verifyError}");
            return 70;
        }
        Phase("verify");

        // Whole-program LTO with the runtime (exe builds at -O1+): csrt's allocation fast path etc.
        // inline into user code. --no-runtime-lto links the runtime as ordinary objects instead.
        var toolchain = new Toolchain(options, target);
        Hardening.ApplyFunctionAttributes(module, llvm, options.Hardening, target); // generated functions only
        Hardening.ApplyModuleFlags(module, llvm, options.Hardening, target);
        // Sanitizer builds link the instrumented runtime as objects (no runtime LTO). WebAssembly always
        // links it in, at every optimization level and for every output kind: exceptions and GC roots
        // are lowered over the whole program (ExceptionLowering, RootSpilling).
        var runtimeLto = wasm || (options.Emit == EmitKind.Exe && options.OptLevel > 0 && options.RuntimeLto && options.Sanitize is null);
        if (runtimeLto)
        {
            RuntimeLto.LinkInto(module, llvm, toolchain.RuntimeBitcode(target.Triple));
            // Everything but main is internal now: drop the runtime code this program never reaches
            // before the (much more expensive) optimization pipeline sees it.
            module.RunPasses("globaldce", target);
            Phase("link runtime bitcode (LTO)");
        }
        if (wasm)
        {
            Csharpc.Wasm.ExceptionLowering.Run(module, llvm);
            if (VerifyOrDump(module, "after exception lowering") is { } code) return code;
            Phase("lower exceptions to explicit propagation (wasm)");
        }

        if (options.OptLevel == 0 && codegen.HasCoroutines)
        {
            // async methods / iterators are LLVM coroutines: CoroSplit must run even at -O0.
            module.RunPasses("default<O0>", target);
            Phase("coroutine lowering (default<O0>)");
        }
        if (options.OptLevel > 0)
        {
            var pipeline = options.SizeLevel switch { 1 => "default<Os>", 2 => "default<Oz>", _ => $"default<O{options.OptLevel}>" };
            if (options.SizeLevel > 0) Optimizations.MarkOptimizeForSize(module, llvm, minimize: options.SizeLevel == 2);
            module.RunPasses(pipeline, target);
            Phase($"optimize ({pipeline})");
        }

        if (wasm)
        {
            Csharpc.Wasm.RootSpilling.Run(module, llvm);
            if (VerifyOrDump(module, "after GC root spilling") is { } code) return code;
            // wasi-libc's start code calls main(argc, argv) under this name (clang renames it the same way).
            module.GetFunction("main").SetName("__main_argc_argv");
            Phase("spill GC roots to the stack (wasm)");
        }

        var output = options.OutputPath;
        if (Path.GetDirectoryName(Path.GetFullPath(output)) is { } outDir) Directory.CreateDirectory(outDir);
        switch (options.Emit)
        {
            case EmitKind.LlvmIr: File.WriteAllText(output, module.PrintToString()); break;
            case EmitKind.LlvmBc: module.WriteBitcode(output); break;
            case EmitKind.Asm: target.Emit(module, output, LLVMCodeGenFileType.Assembly); break;
            case EmitKind.Obj: target.Emit(module, output, LLVMCodeGenFileType.Object); break;
            case EmitKind.Exe:
                var obj = Path.Combine(Path.GetTempPath(), $"csharpc-{Environment.ProcessId}-{Path.GetFileNameWithoutExtension(output)}.o");
                try
                {
                    target.Emit(module, obj, LLVMCodeGenFileType.Object);
                    Phase("codegen (LLVM backend)");
                    toolchain.Link(target.Triple, obj, output, runtimeLinkedIn: runtimeLto, WeakImports(module));
                    Phase("link");
                }
                finally { if (Environment.GetEnvironmentVariable("CSHARPC_KEEP_OBJ") is not { Length: > 0 } keep) File.Delete(obj); else File.Move(obj, keep, overwrite: true); }
                break;
        }
        if (options.Emit != EmitKind.Exe) Phase("emit");
        if (options.Verbose) Console.Error.WriteLine($"csharpc: wrote {output}");
        return 0;
    }

    /// <summary>
    /// csharpc's WebAssembly target is WASI preview 1 (wasi-libc; run with wasmtime or another WASI
    /// runtime): wasm32-wasip1, or its older spelling wasm32-wasi.
    /// </summary>
    private static string? WasmTargetError(string triple) => triple switch
    {
        "wasm32-wasip1" or "wasm32-wasi" or "wasm32-unknown-wasi" or "wasm32-unknown-wasip1" => null,
        _ when triple.StartsWith("wasm64", StringComparison.Ordinal) =>
            $"target '{triple}': wasm64 (memory64) is not supported by csharpc yet; use wasm32-wasip1",
        _ when triple.Contains("threads", StringComparison.Ordinal) =>
            $"target '{triple}': wasi-threads is not supported by csharpc yet; use wasm32-wasip1 (single-threaded)",
        _ => $"target '{triple}': csharpc supports WebAssembly through WASI preview 1 only (wasm32-wasip1)",
    };

    private int? VerifyOrDump(LlvmModule module, string stage)
    {
        if (module.Verify(out var error)) return null;
        var dump = Path.ChangeExtension(options.OutputPath, ".invalid.ll");
        File.WriteAllText(dump, module.PrintToString());
        Console.Error.WriteLine($"internal compiler error: csharpc produced invalid LLVM IR {stage} (written to {dump}):\n{error}");
        return 70;
    }

    /// <summary>Undefined extern_weak functions (statically bound P/Invoke targets that may be missing).</summary>
    private static List<string> WeakImports(LlvmModule module)
    {
        List<string> names = [];
        for (var fn = Native.LLVMGetFirstFunction(module.Ref); !fn.IsNull; fn = Native.LLVMGetNextFunction(fn))
            if (Native.LLVMIsDeclaration(fn) != 0 && (int)Native.LLVMGetLinkage(fn) == 12 /* ExternalWeak */)
                names.Add(Native.TakeName(fn));
        return names;
    }

    private TimeSpan _last, _lastCpu;
    private long _lastAllocated;

    private void Phase(string name)
    {
        if (!options.Verbose) return;
        var now = _clock.Elapsed;
        // Wall time is noisy on a loaded machine; allocations and GCs per phase are not.
        var allocated = GC.GetTotalAllocatedBytes();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        // Peak RSS so far (managed heap + LLVM's native allocations): compile-time scale is memory too.
        Console.Error.WriteLine($"  {(now - _last).TotalMilliseconds,8:F1} ms  {(cpu - _lastCpu).TotalMilliseconds,8:F1} ms cpu  {(allocated - _lastAllocated) / 1048576.0,7:F1} MB alloc  gc {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}  peak {process.PeakWorkingSet64 / 1048576,5} MB  {name}");
        _last = now;
        _lastAllocated = allocated;
        _lastCpu = cpu;
    }

    private static readonly bool s_color =
        !Console.IsErrorRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null && Environment.GetEnvironmentVariable("TERM") != "dumb";

    /// <summary>Roslyn-format diagnostics ("path(l,c): error ID: msg"), colored on a terminal like dotnet.</summary>
    private static void WriteDiagnostic(string text, bool isError)
    {
        if (!s_color) { Console.Error.WriteLine(text); return; }
        var marker = isError ? ": error " : ": warning ";
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        var color = isError ? "\e[1;31m" : "\e[1;33m";
        Console.Error.WriteLine(at < 0
            ? $"{color}{text}\e[0m"
            : $"\e[1m{text[..at]}\e[0m: {color}{marker[2..^1]}\e[0m {text[(at + marker.Length)..]}");
    }

    private static int Fail(string message)
    {
        WriteDiagnostic($"csharpc: error : {message}", isError: true);
        return 1;
    }
}
