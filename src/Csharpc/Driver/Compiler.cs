using System.Diagnostics;
using Csharpc.CodeGen;
using Csharpc.Frontend;
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
        FrontendSettings settings;
        if (options.Project is { } project)
        {
            var (projectSettings, root, all) = ProjectLoader.Load(project, options.Configuration, options.Verbose ? Console.Error : null, options.Properties);
            Phase($"evaluate project ({all.Count} project(s), {projectSettings.Files.Count} file(s))");
            if (!root.OutputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) && !root.OutputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)
                && options.Emit == EmitKind.Exe)
                return Fail($"'{Path.GetFileName(project)}' has OutputType={root.OutputType}; csharpc builds executables (use --emit obj for a library object)");
            foreach (var p in all.Where(p => p.UnsupportedPackages.Count > 0))
                Warn($"{Path.GetFileName(p.ProjectPath)}: PackageReference not supported yet (csharpc compiles from source only): {string.Join(", ", p.UnsupportedPackages)}");
            options.DefaultOutputName = root.AssemblyName;
            settings = projectSettings with { Defines = [.. projectSettings.Defines, .. options.Defines] };
        }
        else
        {
            settings = new FrontendSettings
            {
                Files = options.Inputs,
                Defines = [.. ProjectLoader.ImplicitDefines(options.Configuration), .. options.Defines],
                GlobalUsings = options.ImplicitUsings ? RoslynFrontend.DefaultImplicitUsings : null,
            };
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
        var codegenCompilation = MethodGroupRewriter.Rewrite(
            compilation.WithOptions(compilation.Options.WithNullableContextOptions(NullableContextOptions.Disable)));
        var codegen = new ModuleCodeGen(codegenCompilation, llvm, module, target) { CfgDump = options.DumpCfg ? Console.Out : null };
        using var debugInfo = options.Debug
            ? codegen.Debug = new DebugInfo(codegen, settings.Files.Count > 0 ? settings.Files[0] : "program.cs", options.OptLevel > 0)
            : null;
        try { codegen.Generate(); }
        catch (CompileError e) { codegen.Errors.Add(e); }
        debugInfo?.Finish();
        Phase("lower CFG -> LLVM IR");

        if (codegen.Errors.Count > 0)
        {
            foreach (var e in codegen.Errors.DistinctBy(e => e.Format())) WriteDiagnostic(e.Format(), isError: true);
            return Fail($"{codegen.Errors.Count} construct(s) not supported by csharpc yet");
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
        // Sanitizer builds link the instrumented runtime as objects (no runtime LTO).
        var runtimeLto = options.Emit == EmitKind.Exe && options.OptLevel > 0 && options.RuntimeLto && options.Sanitize is null;
        if (runtimeLto)
        {
            RuntimeLto.LinkInto(module, llvm, toolchain.RuntimeBitcode(target.Triple));
            // Everything but main is internal now: drop the runtime code this program never reaches
            // before the (much more expensive) optimization pipeline sees it.
            module.RunPasses("globaldce", target);
            Phase("link runtime bitcode (LTO)");
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
                    toolchain.Link(target.Triple, obj, output, runtimeLinkedIn: runtimeLto);
                    Phase("link");
                }
                finally { if (Environment.GetEnvironmentVariable("CSHARPC_KEEP_OBJ") is not { Length: > 0 } keep) File.Delete(obj); else File.Move(obj, keep, overwrite: true); }
                break;
        }
        if (options.Emit != EmitKind.Exe) Phase("emit");
        if (options.Verbose) Console.Error.WriteLine($"csharpc: wrote {output}");
        return 0;
    }

    private TimeSpan _last;

    private void Phase(string name)
    {
        if (!options.Verbose) return;
        var now = _clock.Elapsed;
        Console.Error.WriteLine($"  {(now - _last).TotalMilliseconds,8:F1} ms  {name}");
        _last = now;
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

    private static void Warn(string message) => WriteDiagnostic($"csharpc: warning : {message}", isError: false);

    private static int Fail(string message)
    {
        WriteDiagnostic($"csharpc: error : {message}", isError: true);
        return 1;
    }
}
