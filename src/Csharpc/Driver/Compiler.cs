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
        var compilation = RoslynFrontend.Analyze(options.Inputs, options.ImplicitUsings, options.Defines);
        var diagnostics = compilation.GetDiagnostics();
        Phase("parse + bind (Roslyn)");

        var errors = 0;
        foreach (var d in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || (options.ShowWarnings && d.Severity == DiagnosticSeverity.Warning)))
        {
            Console.Error.WriteLine(d.ToString());
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
        module.SourceFileName = options.Inputs[0];

        var codegen = new ModuleCodeGen(compilation, llvm, module, target) { CfgDump = options.DumpCfg ? Console.Out : null };
        try { codegen.Generate(); }
        catch (CompileError e) { codegen.Errors.Add(e); }
        Phase("lower CFG -> LLVM IR");

        if (codegen.Errors.Count > 0)
        {
            foreach (var e in codegen.Errors.DistinctBy(e => e.Format())) Console.Error.WriteLine(e.Format());
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
        var toolchain = new Toolchain(options);
        var runtimeLto = options.Emit == EmitKind.Exe && options.OptLevel > 0 && options.RuntimeLto;
        if (runtimeLto)
        {
            RuntimeLto.LinkInto(module, llvm, toolchain.RuntimeBitcode(target.Triple));
            Phase("link runtime bitcode (LTO)");
        }

        if (options.OptLevel > 0)
        {
            module.RunPasses($"default<O{options.OptLevel}>", target);
            Phase($"optimize (default<O{options.OptLevel}>)");
        }

        var output = options.OutputPath;
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

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"csharpc: {message}");
        return 1;
    }
}
