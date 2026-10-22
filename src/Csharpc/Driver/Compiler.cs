using System.Diagnostics;
using Csharpc.CodeGen;
using Csharpc.Frontend;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

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
                Defines = options.Defines,
                GlobalUsings = options.ImplicitUsings ? RoslynFrontend.DefaultImplicitUsings : null,
            };
        }
        var compilation = RoslynFrontend.Analyze(settings);
        var diagnostics = RoslynFrontend.UserDiagnostics(compilation);
        Phase("parse + bind (Roslyn)");

        var errors = 0;
        foreach (var d in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || (options.ShowWarnings && d.Severity == DiagnosticSeverity.Warning)))
        {
            WriteDiagnostic(d.ToString(), d.Severity == DiagnosticSeverity.Error);
            if (d.Severity == DiagnosticSeverity.Error) errors++;
        }
        if (errors > 0) return Fail($"{errors} error(s) from the C# front-end");

        using var llvm = new LlvmContext();
        using var target = TargetMachine.Create(options.Target, options.OptLevel switch
        {
            0 => LLVMCodeGenOptLevel.None,
            1 => LLVMCodeGenOptLevel.Less,
            3 => LLVMCodeGenOptLevel.Aggressive,
            _ => LLVMCodeGenOptLevel.Default,
        });
        using var module = new LlvmModule(llvm, compilation.AssemblyName ?? "program");
        module.ConfigureFor(target);
        module.SourceFileName = settings.Files.Count > 0 ? settings.Files[0] : "program";

        var codegen = new ModuleCodeGen(compilation, llvm, module, target) { CfgDump = options.DumpCfg ? Console.Out : null };
        try { codegen.Generate(); }
        catch (CompileError e) { codegen.Errors.Add(e); }
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

        if (options.OptLevel > 0)
        {
            module.RunPasses($"default<O{options.OptLevel}>", target);
            Phase($"optimize (default<O{options.OptLevel}>)");
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
                    new Toolchain(options).Link(target.Triple, obj, output);
                    Phase("link");
                }
                finally { File.Delete(obj); }
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
