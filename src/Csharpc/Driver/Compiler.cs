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

    private static IEnumerable<string> FileGlobalization(IEnumerable<string> inputs)
    {
        var (invariant, predefined) = ProjectLoader.FileGlobalizationProperties(inputs);
        return ProjectLoader.GlobalizationDefines(invariant, predefined);
    }

    public int Run()
    {
        _clock.Start();
        Csharpc.Llvm.Native.EnsureSupportedVersion(); // an LLVM outside 18-20 aborts inside LLVM; say so here
        // The target decides the corelib's per-OS code (CSHARPC_TARGET_*) before anything is parsed.
        var triple = options.Target ?? TargetMachine.HostTriple;
        var darwin = TargetMachine.IsDarwinTriple(triple);
        var windows = TargetMachine.IsWindowsTriple(triple);
        var wasm = TargetMachine.IsWasmTriple(triple);
        NativeTls.On = NativeTls.Enabled(windows, darwin, linux: !windows && !darwin && !wasm);
        // Before the corelib index or the framework index is read: they depend on which shadows are mapped. The Windows
        // native-TLS mode is the same mechanism with a hand-picked family of shadows (NativeTls).
        try
        {
            IlFirst.Configure(options.IlFirst);
            if (NativeTls.On) NativeTls.Apply();
        }
        catch (InvalidOperationException e) { return Fail(e.Message); }
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
        string[] coreLibDefines = [wasm ? "CSHARPC_TARGET_WASI" : windows ? "CSHARPC_TARGET_WINDOWS" : darwin ? "CSHARPC_TARGET_MACOS" : "CSHARPC_TARGET_LINUX",
            // The targets whose X.509/TLS/RSA corelib code runs on OpenSSL 3 (docs/adr/0022-tls.md): libssl.so.3 on Linux,
            // Homebrew's openssl@3 on macOS, libssl-3-x64.dll beside the executable (or on PATH) on Windows.
            .. (!wasm && !NativeTls.On ? new[] { "CSHARPC_OPENSSL" } : Array.Empty<string>()),
            // Opt-in spike (docs/adr/0022-tls.md, "Windows without OpenSSL"): the TLS/X.509/RSA shadows are left out of the shadow
            // index and System.Net.Security / System.Security.Cryptography are compiled from the shared framework's IL.
            .. (NativeTls.On ? new[] { "CSHARPC_NATIVE_TLS" } : Array.Empty<string>()),
            // The architecture (RuntimeInformation.ProcessArchitecture, the runtime identifier).
            wasm ? "CSHARPC_TARGET_WASM32" : triple.StartsWith("aarch64", StringComparison.Ordinal) || triple.StartsWith("arm64", StringComparison.Ordinal) ? "CSHARPC_TARGET_ARM64" : "CSHARPC_TARGET_X64"];
        FrontendSettings settings;
        IlProgram? mainProgram = null;
        // NativeAOT's native-linking inputs (DirectPInvoke, DirectPInvokeList, NativeLibrary, NativeSystemLibrary, LinkerArg): the command
        // line's and, for a project, those of every project in the compilation.
        List<string> directPInvokeEntries = [.. options.DirectPInvokes], directPInvokeLists = [.. options.DirectPInvokeLists];
        List<string> nativeLibraries = [.. options.NativeLibraries], nativeSystemLibraries = [.. options.NativeSystemLibraries], linkerArgs = [.. options.LinkerArgs];
        if (options.Project is { } project)
        {
            var (projectSettings, root, all) = ProjectLoader.Load(project, options.Configuration, options.Verbose ? Console.Error : null, options.Properties,
                RuntimeIdentifiers.FromTriple(triple) ?? (wasm ? null : RuntimeIdentifiers.Host));
            Phase($"evaluate project ({all.Count} project(s), {projectSettings.Files.Count} file(s))");
            if (!root.OutputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) && !root.OutputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)
                && options.Emit == EmitKind.Exe)
                return Fail($"'{Path.GetFileName(project)}' has OutputType={root.OutputType}; csharpc builds executables (use --emit obj for a library object)");
            foreach (var p in all.Where(p => p.UnresolvedReferences.Count > 0))
                return Fail($"{Path.GetFileName(p.ProjectPath)}: <Reference> not found: {string.Join(", ", p.UnresolvedReferences)} (build the referenced assembly first, or give it a HintPath)");
            options.DefaultOutputName = root.AssemblyName;
            foreach (var p in all)
            {
                directPInvokeEntries.AddRange(p.DirectPInvokes);
                directPInvokeLists.AddRange(p.DirectPInvokeLists);
                nativeLibraries.AddRange(p.NativeLibraries);
                nativeSystemLibraries.AddRange(p.NativeSystemLibraries);
                linkerArgs.AddRange(p.LinkerArgs);
            }
            settings = projectSettings with
            {
                Defines = [.. projectSettings.Defines, .. options.Defines],
                CoreLibDefines = [.. coreLibDefines, .. ProjectLoader.GlobalizationDefines(root.InvariantGlobalization, root.PredefinedCulturesOnly)],
            };
        }
        else if (options.MainAssembly is { } mainAssembly)
        {
            // A program built by csc (ADR 0010, "The program as IL"): no sources; the assembly is an IL
            // reference whose entry point is the program's, and what `dotnet app.dll` would read next to
            // it (app.runtimeconfig.json) configures the runtime. File-based `#:` directives were
            // already applied by the build that produced it.
            try { mainProgram = IlProgram.Read(mainAssembly, RuntimeIdentifiers.FromTriple(triple)); }
            catch (InvalidOperationException e) { return Fail(e.Message); }
            options.DefaultOutputName = mainProgram.Name;
            settings = new FrontendSettings
            {
                Files = [],
                Defines = [.. ProjectLoader.ImplicitDefines(options.Configuration), .. options.Defines],
                CoreLibDefines = [.. coreLibDefines, .. ProjectLoader.GlobalizationDefines(
                    mainProgram.Switch("System.Globalization.Invariant"), mainProgram.Switch("System.Globalization.PredefinedCulturesOnly"))],
                RuntimeConfig = mainProgram.RuntimeConfig,
                JsonReflectionEnabled = mainProgram.Switch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault") ?? true,
                // The Roslyn compilation only binds; its name must not clash with the program assembly's.
                AssemblyName = mainProgram.Name + ".csharpc",
                AssemblyVersion = null,
                IlReferencePaths = [mainAssembly, .. mainProgram.AppLocalAssemblies],
                NativeAssets = mainProgram.NativeAssets,
                // The shared frameworks the host would run it on (Microsoft.AspNetCore.App), as --framework adds them.
                SharedFrameworks = mainProgram.Frameworks,
            };
        }
        else
        {
            settings = new FrontendSettings
            {
                Files = options.Inputs,
                Defines = [.. ProjectLoader.ImplicitDefines(options.Configuration), .. options.Defines],
                GlobalUsings = options.ImplicitUsings ? RoslynFrontend.DefaultImplicitUsings : null,
                CoreLibDefines = [.. coreLibDefines, .. FileGlobalization(options.Inputs)],
                RuntimeConfig = options.Inputs.Count > 0 ? [.. ProjectLoader.FileBasedRuntimeConfig(options.Inputs[0])] : [],
                AssemblyVersion = options.Inputs.Count > 0 ? ProjectLoader.FileBasedAssemblyVersion(options.Inputs[0]) : "1.0.0.0",
                AssemblyInfo = options.Inputs.Count > 0 ? ProjectLoader.FileBasedAssemblyInfo(options.Inputs[0], options.Configuration) : null,
            };
        }
        if (UsesSystemResourceKeys(settings.RuntimeConfig))
            return Fail(UseSystemResourceKeysUnsupported);
        if (options.SharedFrameworks.Count > 0)
            settings = settings with { SharedFrameworks = [.. settings.SharedFrameworks.Concat(options.SharedFrameworks).Distinct(StringComparer.OrdinalIgnoreCase)] };
        // Assemblies without sources: bound against like any reference, compiled from IL (ADR 0010).
        List<string> ilPaths = [.. settings.IlReferencePaths, .. options.References];
        // .NET's native shims the framework IL calls, linked from the NativeAOT runtime pack (NativeAotPack).
        var linkedShims = new SortedSet<string>(StringComparer.Ordinal);
        // The types a preserialized .resx holds (System.Drawing.Point ...): converted at run time by their TypeConverter.
        settings = settings with { ResourceTypes = mainProgram is null ? ResourceConverters.TypeNames(settings.EmbeddedResources)
            : ResourceConverters.TypeNamesOfAssemblies([mainProgram.Path, .. mainProgram.AppLocalAssemblies]) };
        try
        {
            var framework = FrameworkIl.Select(settings, ilPaths, options.FrameworkIl, options.FrameworkIlAutomatic, triple);
            foreach (var choice in framework)
            {
                ilPaths.Add(choice.Path);
                linkedShims.UnionWith(choice.NativeModules);
                if (options.Verbose) Console.Error.WriteLine($"  framework IL: {choice.Name} ({choice.Reason})");
            }
            Phase($"choose framework IL ({framework.Count} assembl{(framework.Count == 1 ? "y" : "ies")})");
        }
        catch (InvalidOperationException e) { return Fail(e.Message); }
        // The types of System.Private.CoreLib served from the framework's IL (CsharpcUpstream.dll, ADR 0023): IL assemblies of every compilation.
        foreach (var upstream in UpstreamSlice.ShippedAssemblies)
            if (!ilPaths.Contains(upstream, StringComparer.Ordinal)) ilPaths.Add(upstream);
        // The corelib as a precompiled assembly (ADR 0015): after the framework choice, which must not
        // follow the corelib's own references.
        if (options.CoreLibIl && RoslynFrontend.CoreLibFiles() is { Count: > 0 } coreLibFiles)
        {
            try
            {
                var built = CoreLibAssembly.Ensure(coreLibFiles, ["CSHARPC", .. settings.Defines, .. settings.CoreLibDefines],
                    RoslynFrontend.ReferenceAssemblies(), options.Verbose ? Console.Error : null);
                ilPaths.Add(built.Path);
                settings = settings with { CoreLibAsIl = true };
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Fail(e.Message);
            }
            Phase("corelib assembly");
        }
        IlAssemblySet il;
        try { il = IlAssemblySet.Load(ilPaths, settings.JsonReflectionEnabled == true ? mainProgram?.Path : null); }
        catch (Exception e) when (e is InvalidOperationException or IOException or BadImageFormatException) { return Fail(e.Message); }
        using var ilScope = il;
        if (!il.IsEmpty)
        {
            var programIl = mainProgram is null ? null : il.Assemblies.First(a => string.Equals(a.Path, Path.GetFullPath(mainProgram.Path), StringComparison.Ordinal));
            // Reflection-based JSON metadata is generated in the binding compilation, which must see the
            // program's internals as the program itself does (JsonReflectionContexts.Il.cs); before any binding.
            var jsonFromIl = programIl is not null && !IlFirst.Assemblies.Contains("System.Text.Json", StringComparer.OrdinalIgnoreCase) && settings.JsonReflectionEnabled == true && JsonReflectionContexts.ReferencesJsonSerializer(programIl.Reader)
                && JsonReflectionContexts.GrantInternalsAccess(programIl.Reference, settings.AssemblyName!);
            // Converter hooks for the program's resources and GetConverter calls are generated like the JSON contexts: before any binding.
            var convertersFromIl = programIl is not null && (settings.ResourceTypes.Count > 0 || ResourceConverters.CallsGetConverter(programIl.Reader))
                && JsonReflectionContexts.GrantInternalsAccess(programIl.Reference, settings.AssemblyName!);
            settings = settings with { IlReferences = il.References, IlSet = il, ProgramIl = programIl, JsonFromIl = jsonFromIl, ConvertersFromIl = convertersFromIl };
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
        if (errors == 0 && options.Emit == EmitKind.Exe && mainProgram is null && compilation.GetEntryPoint(CancellationToken.None) is null)
            foreach (var d in compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id is "CS0017" or "CS1555" or "CS1556" or "CS1558" or "CS5001" or "CS7022"))
            {
                WriteDiagnostic(d.ToString(), isError: true);
                errors++;
            }
        if (errors > 0) return Fail($"{errors} error(s) from the C# front-end");

        using var llvm = new LlvmContext();
        // Local value names ("%add", "%call3", block labels) only matter to someone reading the IR: they
        // cost a string-map entry per value in the program's memory and again in every partition's copy.
        // Globals keep theirs; --emit llvm-ir|llvm-bc, or CSHARPC_KEEP_VALUE_NAMES=1, keeps the rest.
        if (options.Emit is not (EmitKind.LlvmIr or EmitKind.LlvmBc) && Environment.GetEnvironmentVariable("CSHARPC_KEEP_VALUE_NAMES") != "1")
            Native.LLVMContextSetDiscardValueNames(llvm.Ref, 1);
        using var target = TargetMachine.Create(options.Target, options.OptLevel switch
        {
            0 => LLVMCodeGenOptLevel.None,
            1 => LLVMCodeGenOptLevel.Less,
            3 => LLVMCodeGenOptLevel.Aggressive,
            _ => LLVMCodeGenOptLevel.Default,
        }, options.Cpu, options.Features);
        using var module = new LlvmModule(llvm, mainProgram?.Name ?? compilation.AssemblyName ?? "program");
        module.ConfigureFor(target);
        module.SourceFileName = settings.Files.Count > 0 ? settings.Files[0] : mainProgram?.Path ?? "program";

        // Codegen binds against a nullable-oblivious copy of the compilation: nullability never affects
        // generated code, and Roslyn's nullable re-inference can surface error types in IOperation
        // (e.g. `Select` over an array of tuples with mixed element names yields `Select<T, ?>`).
        // IL assemblies are imported with their private and internal members: layouts need every field.
        var codegenOptions = compilation.Options.WithNullableContextOptions(NullableContextOptions.Disable);
        if (!il.IsEmpty) codegenOptions = codegenOptions.WithMetadataImportOptions(MetadataImportOptions.All);
        var codegenCompilation = MethodGroupRewriter.Rewrite(compilation.WithOptions(codegenOptions));
        if (!il.IsEmpty) il.Bind(codegenCompilation);
        IlAssembly? mainIl = null;
        IMethodSymbol? entryPoint = null;
        if (mainProgram is not null)
        {
            mainIl = il.Assemblies.First(a => string.Equals(a.Path, mainProgram.Path, StringComparison.Ordinal));
            entryPoint = mainIl.Definition(System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(mainProgram.EntryPointToken)) as IMethodSymbol;
            if (entryPoint is null) return Fail($"'{mainProgram.Path}': its entry point token 0x{mainProgram.EntryPointToken:X8} is not a method");
        }
        // Stack traces cost a landing pad per call site: only for programs that can look at one.
        StackTraceInfo? stackTraces = null;
        if (mainIl is not null)
        {
            // The program as IL: names from its metadata, lines from the PDB csc wrote beside it.
            if (options.StackTraces || StackTraceInfo.IlMayObserve(mainIl.Reader))
            {
                stackTraces = new StackTraceInfo(mainIl);
                Phase($"stack-trace metadata (IL + PDB, {(stackTraces.HasPdb ? "ok" : "no PDB")})");
            }
        }
        else if (options.StackTraces || StackTraceInfo.FindObservation(compilation) is not null)
        {
            var symbols = settings.Symbols ?? new DebugSymbolSettings { Optimize = options.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase) };
            stackTraces = new StackTraceInfo(compilation, symbols);
            Phase($"stack-trace metadata (emit + PDB, {(stackTraces.HasPdb ? "ok" : "failed")})");
        }
        // DirectPInvoke entries, and the files that satisfy them (NativeLibrary items, and static archives among the packages'
        // native assets named like a DirectPInvoke library: lib<name>.a, <name>.a, <name>.lib).
        HashSet<string> directPInvokes = new(StringComparer.Ordinal);
        foreach (var entry in directPInvokeEntries) if (entry.Trim() is { Length: > 0 } trimmed) directPInvokes.Add(trimmed);
        foreach (var list in directPInvokeLists)
        {
            if (!File.Exists(list)) return Fail($"DirectPInvokeList: file '{list}' not found");
            foreach (var line in File.ReadLines(list))
                if (line.Trim() is { Length: > 0 } entry && !entry.StartsWith('#')) directPInvokes.Add(entry);
        }
        var nativeAssetArchives = settings.NativeAssets.Where(IsStaticArchive).ToList();
        foreach (var archive in nativeAssetArchives)
            if (NativeArchiveNames(Path.GetFileName(archive)).Any(name => directPInvokes.Contains(name) || directPInvokes.Any(d => d.StartsWith(name + "!", StringComparison.Ordinal)))
                && !nativeLibraries.Contains(archive))
                nativeLibraries.Add(archive);
        foreach (var library in nativeLibraries)
            if ((IsNativeLibraryFile(library) || library.Contains(Path.DirectorySeparatorChar) || library.Contains('/')) && !File.Exists(library))
                return Fail($"NativeLibrary: file '{library}' not found");
        string[] nativeLinkArguments = [.. nativeLibraries, .. nativeSystemLibraries.Select(n => "-l" + n), .. linkerArgs];
        var codegen = new ModuleCodeGen(codegenCompilation, llvm, module, target)
        {
            DirectPInvokes = directPInvokes,
            StackTraces = stackTraces,
            ProgramCompilation = mainIl is null ? compilation : null,
            SourceOptimized = settings.Symbols?.Optimize ?? options.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase),
            CfgDump = options.DumpCfg ? Console.Out : null,
            IlDump = options.DumpIl ? Console.Out : null,
            Il = il,
            MainIl = mainIl,
            EntryPoint = entryPoint,
            IlSurvey = options.IlSurvey,
            IlSurveyAssemblies = options.IlSurveyAssemblies,
            IlSurveyShard = options.IlSurveyShard,
            RuntimeConfig = settings.RuntimeConfig,
            ProjectAnnotations = path => RoslynFrontend.NullableOf(settings.Projects.FirstOrDefault(p => p.Files.Any(f => string.Equals(Path.GetFullPath(f), path, StringComparison.Ordinal)))?.Nullable ?? settings.Nullable).AnnotationsEnabled(),
            ProgramResources = [.. settings.EmbeddedResources.Select(r => new ProgramResourceData(r.Assembly, r.Name, File.ReadAllBytes(r.Path)))],
            ProgramAssemblies = mainIl is null ? ProgramAssembly.From(settings, codegenCompilation.AssemblyName ?? "program") : [],
            LinkedShims = linkedShims,
            GcNursery = options.GcNursery,
            GcConcurrent = options.GcConcurrent && !options.GcNursery && !wasm,
        };
        using var debugInfo = options.Debug
            ? codegen.Debug = new DebugInfo(codegen, settings.Files.Count > 0 ? settings.Files[0] : "program.cs", options.OptLevel > 0)
            : null;
        try
        {
            codegen.Generate();
            if (options.GcNursery) codegen.InsertWriteBarriers();
            else if (codegen.GcConcurrent) codegen.InsertConcurrentBarriers();
        }
        catch (CompileError e)
        {
            if (Environment.GetEnvironmentVariable("CSHARPC_TRACE") == "1") Console.Error.WriteLine(e.StackTrace);
            codegen.Errors.Add(e);
        }
        debugInfo?.Finish();
        Phase("lower CFG -> LLVM IR");
        if (options.Verbose) Console.Error.WriteLine($"             {codegen.Stats}");
        // The managed heap peaks with lowering (Roslyn's bound bodies and CFGs, released by Generate):
        // when there were many, hand the memory back before LLVM allocates its own peak (a full
        // compacting collection costs ~0.1 s, so not for small programs).
        // Programs lowered from IL (Roslyn hosted from its package: ~120 000 IL methods, 2 Roslyn CFGs) peak there too.
        if (codegen.Stats.CfgBuilt + codegen.Stats.IlMethods >= 500)
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            Phase("release front-end memory");
        }

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
            // The verifier's message first: dumping a whole program's IR can take gigabytes.
            var dump = Path.ChangeExtension(options.OutputPath, ".invalid.ll");
            var shown = verifyError is { Length: > 8000 } ? verifyError[..8000] + "\n(...)" : verifyError;
            Console.Error.WriteLine($"internal compiler error: csharpc produced invalid LLVM IR:\n{shown}");
            if (module.PrintToFile(dump)) Console.Error.WriteLine($"(the module is written to {dump})");
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
        List<string>? prebuiltObjects = null, prebuiltWeakImports = null;
        string? prebuiltObjectBase = null;
        if (options.OptLevel > 0)
        {
            var pipeline = options.SizeLevel switch { 1 => "default<Os>", 2 => "default<Oz>", _ => $"default<O{options.OptLevel}>" };
            if (options.SizeLevel > 0) Optimizations.MarkOptimizeForSize(module, llvm, minimize: options.SizeLevel == 2);
            // The type data's function pointers stay out of the inliner's call graph (TypeDataDetach).
            var detached = TypeDataDetach.Detach(module, llvm);
            // Large executables: optimization and the backend run per partition, on several threads
            // (not with -g: one compile unit per program; not wasm: its passes need the whole program).
            if (options.Emit == EmitKind.Exe && !wasm && !options.Debug)
            {
                prebuiltWeakImports = WeakImports(module);
                prebuiltObjectBase = Path.Combine(Path.GetTempPath(), $"csharpc-{Environment.ProcessId}-{Path.GetFileNameWithoutExtension(options.OutputPath)}");
                prebuiltObjects = ParallelOptimize.TryRun(module, detached, pipeline, target, prebuiltObjectBase,
                    options.Verbose ? m => Console.Error.WriteLine($"             {m}") : null);
            }
            if (prebuiltObjects is null)
            {
                module.RunPasses(pipeline, target);
                Csharpc.CodeGen.ConcurrentBarrierElision.Run(module.Ref, target);
                TypeDataDetach.Reattach(module, detached, target);
                Phase($"optimize ({pipeline})");
            }
            else Phase($"optimize ({pipeline}) and codegen, {prebuiltObjects.Count} partitions");
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
                List<string> objects = [];
                try
                {
                    if (prebuiltObjects is not null)
                    {
                        objects = prebuiltObjects;
                        toolchain.Link(target.Triple, objects, output, runtimeLinkedIn: runtimeLto, prebuiltWeakImports!, ShimArchives(linkedShims, target.Triple), nativeLinkArguments);
                        Phase("link");
                        break;
                    }
                    var weakImports = WeakImports(module);
                    // Large programs: the backend runs on several threads, one object per partition
                    // (not with -g: one compile unit per program keeps the DWARF as gdb/lldb expect it).
                    var parallel = !wasm && !options.Debug
                        ? ParallelCodegen.TryEmit(module, target, Path.ChangeExtension(obj, null), options.Verbose ? m => Console.Error.WriteLine($"             {m}") : null)
                        : null;
                    if (parallel is not null) objects = parallel;
                    else
                    {
                        objects = [obj];
                        target.Emit(module, obj, LLVMCodeGenFileType.Object);
                    }
                    Phase(objects.Count > 1 ? $"codegen (LLVM backend, {objects.Count} partitions)" : "codegen (LLVM backend)");
                    toolchain.Link(target.Triple, objects, output, runtimeLinkedIn: runtimeLto, weakImports, ShimArchives(linkedShims, target.Triple), nativeLinkArguments);
                    Phase("link");
                }
                finally
                {
                    var keep = Environment.GetEnvironmentVariable("CSHARPC_KEEP_OBJ");
                    for (var i = 0; i < objects.Count; i++)
                        if (keep is not { Length: > 0 }) File.Delete(objects[i]);
                        else File.Move(objects[i], objects.Count == 1 ? keep : $"{keep}.{i}", overwrite: true);
                }
                break;
        }
        if (options.Emit != EmitKind.Exe) Phase("emit");
        // The packages' native libraries for the target (NuGet's runtimes/<rid>/native) go next to the executable, where
        // P/Invoke probing looks first (csrt_pinvoke_load_library), as `dotnet publish` places them for the host.
        if (options.Emit == EmitKind.Exe && settings.NativeAssets.Count > 0 && Path.GetDirectoryName(Path.GetFullPath(output)) is { } assetDirectory)
            foreach (var asset in settings.NativeAssets.Where(a => !IsStaticArchive(a)))
            {
                var destination = Path.Combine(assetDirectory, Path.GetFileName(asset));
                if (!string.Equals(Path.GetFullPath(asset), destination, StringComparison.Ordinal)) File.Copy(asset, destination, overwrite: true);
                if (options.Verbose) Console.Error.WriteLine($"csharpc: native asset {destination}");
            }
        if (options.Verbose) Console.Error.WriteLine($"csharpc: wrote {output}");
        return 0;
    }

    /// <summary>
    /// UseSystemResourceKeys=true (runtimeconfig System.Resources.UseSystemResourceKeys) makes .NET's libraries
    /// give their exception messages as resource keys ("ArgumentNull_Generic Arg_ParamName_Name, x" for "Value
    /// cannot be null. (Parameter 'x')"), under the JIT and NativeAOT alike. Libraries compiled from IL honour it
    /// (their SR reads the switch, or ILLink's substitution folds it), but csharpc's corelib and runtime write the
    /// full text, so every message they produce would differ: refused rather than printed differently. The value
    /// is "true" exactly when bool.TryParse reads true (ProjectLoader and IlProgram normalize booleans).
    /// </summary>
    internal static bool UsesSystemResourceKeys(IEnumerable<KeyValuePair<string, string>> runtimeConfig) =>
        runtimeConfig.LastOrDefault(kv => kv.Key == "System.Resources.UseSystemResourceKeys").Value is { } value
        && bool.TryParse(value, out var on) && on;

    internal const string UseSystemResourceKeysUnsupported =
        "UseSystemResourceKeys=true (System.Resources.UseSystemResourceKeys) is not supported by csharpc yet: .NET then gives "
        + "its exception messages as resource keys, and csharpc's library writes the full messages; remove the setting or set it to false";

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

    /// <summary>A static archive the packages ship (lib&lt;name&gt;.a): linked when a DirectPInvoke names it, never loaded.</summary>
    private static bool IsStaticArchive(string path) => path.EndsWith(".a", StringComparison.Ordinal);

    private static bool IsNativeLibraryFile(string path) =>
        path.EndsWith(".a", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lib", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".o", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);

    /// <summary>The DirectPInvoke library names an archive file satisfies: libfoo.a is "foo", "libfoo" and "libfoo.a".</summary>
    private static IEnumerable<string> NativeArchiveNames(string file)
    {
        var stem = file.EndsWith(".a", StringComparison.Ordinal) ? file[..^2] : file;
        yield return stem;
        yield return file;
        if (stem.StartsWith("lib", StringComparison.Ordinal)) yield return stem[3..];
    }

    /// <summary>The NativeAOT pack's static archives for the shims the framework IL calls, in link order.</summary>
    private static List<string> ShimArchives(IEnumerable<string> shims, string triple)
    {
        List<string> archives = [];
        if (!shims.Any() || NativeAotPack.Rid(triple) is not { } rid || NativeAotPack.NativeDirectory(rid, out _) is not { } dir) return archives;
        foreach (var shim in shims)
            foreach (var archive in NativeAotPack.Shims[shim])
                if (!archives.Contains(Path.Combine(dir, archive))) archives.Add(Path.Combine(dir, archive));
        return archives;
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
        // Current and peak RSS (managed heap + LLVM's native allocations): compile-time scale is memory too.
        Console.Error.WriteLine($"  {(now - _last).TotalMilliseconds,8:F1} ms  {(cpu - _lastCpu).TotalMilliseconds,8:F1} ms cpu  {(allocated - _lastAllocated) / 1048576.0,7:F1} MB alloc  gc {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}  rss {process.WorkingSet64 / 1048576,5} peak {process.PeakWorkingSet64 / 1048576,5} MB  {name}");
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
