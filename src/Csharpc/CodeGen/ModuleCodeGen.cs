using System.Text;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Csharpc.CodeGen;

public readonly record struct LoweredFunction(LLVMValueRef Fn, LLVMTypeRef Type);

/// <summary>
/// Lowers a whole Roslyn <see cref="Compilation"/> into one LLVM module. This is csharpc's
/// equivalent of clang's CodeGenModule / rustc's codegen_llvm crate-level driver.
/// </summary>
public sealed partial class ModuleCodeGen
{
    public Compilation Compilation { get; }
    public LlvmContext Llvm { get; }
    public LlvmModule Module { get; }
    public TypeMapper Types { get; }
    public List<CompileError> Errors { get; } = [];
    public TextWriter? CfgDump { get; init; }

    private readonly Dictionary<IMethodSymbol, LoweredFunction> _functions = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ClassLayout> _layouts = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, LLVMValueRef> _staticFields = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, LoweredFunction> _runtime = [];
    private readonly Dictionary<string, LLVMValueRef> _globals = [];
    private readonly Dictionary<string, LLVMValueRef> _strings = [];
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];
    /// <summary>Generic instantiations (constructed methods) whose bodies still need lowering.</summary>
    private readonly Queue<IMethodSymbol> _pendingInstantiations = new();
    private readonly Queue<(ClosureFunction Function, IMethodSymbol Symbol, TypeSubstitution Subst)> _pendingNested = new();
    private readonly HashSet<(ClosureFunction, string)> _enqueuedNested = [];
    private readonly Dictionary<IMethodSymbol, Dictionary<string, LoweredFunction>> _nestedFunctions = new(SymbolEqualityComparer.Default);
    private int _lambdaCounter;
    private readonly List<INamedTypeSymbol> _userTypes = [];

    public ModuleCodeGen(Compilation compilation, LlvmContext llvm, LlvmModule module, TargetMachine target)
    {
        Compilation = compilation;
        Llvm = llvm;
        Module = module;
        Types = new TypeMapper(llvm, target);
        Types.StructType = ResolveStructType;
    }

    public SemanticModel ModelFor(SyntaxTree tree) =>
        _models.TryGetValue(tree, out var model) ? model : _models[tree] = Compilation.GetSemanticModel(tree);

    public bool IsUserType(ITypeSymbol? type) =>
        type is INamedTypeSymbol && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, Compilation.Assembly);

    public bool IsUserMethod(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, Compilation.Assembly);

    // ======================================================================================
    // Driver
    // ======================================================================================

    public void Generate()
    {
        var entryPoint = Compilation.GetEntryPoint(CancellationToken.None)
            ?? throw new CompileError("CSLLVM0003", "program has no entry point (Main method or top-level statements)", null);

        CollectTypes(Compilation.Assembly.GlobalNamespace);

        // 1. Layouts and method tables for every class (base classes first, via GetLayout recursion).
        foreach (var type in _userTypes)
            Guard(type, () =>
            {
                switch (type.TypeKind)
                {
                    // Generic definitions get layouts per instantiation, on demand (monomorphization).
                    case TypeKind.Class when !type.IsStatic && !TypeSubstitution.IsGenericContext(type): GetLayout(type); break;
                    case TypeKind.Class or TypeKind.Enum or TypeKind.Interface or TypeKind.Extension: break;
                    case TypeKind.Struct when !TypeSubstitution.IsGenericContext(type): GetStructLayout(type); break;
                    case TypeKind.Struct: break;
                    case TypeKind.Delegate: break; // runtime-implemented: { mt, target, fn } objects
                    default: throw CompileError.Unsupported($"{type.TypeKind} '{type.Name}'", type);
                }
            });

        // 2. Method bodies of non-generic code; generic instantiations are queued as they're referenced.
        foreach (var type in _userTypes.Where(t => t.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Struct or TypeKind.Extension
                     && !TypeSubstitution.IsGenericContext(t)))
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => !m.IsGenericMethod))
                Guard(method, () => EmitMethod(method));

        DrainWorklists();

        // 3. Static initialization + the native entry point.
        Guard(entryPoint, EmitStaticInitializer);
        Guard(entryPoint, () => EmitNativeMain(entryPoint));
        DrainWorklists();
    }

    private void DrainWorklists()
    {
        while (true)
        {
            if (_pendingNested.TryDequeue(out var nested))
                Guard(nested.Symbol, () => EmitNestedFunction(nested.Function, nested.Symbol, nested.Subst));
            else if (_pendingInstantiations.TryDequeue(out var instance))
                Guard(instance, () => EmitMethod(instance));
            else break;
        }
    }

    private static readonly bool s_trace = Environment.GetEnvironmentVariable("CSHARPC_TRACE") == "1";

    private void Guard(ISymbol symbol, Action action)
    {
        if (s_trace) Console.Error.WriteLine($"[csharpc] lowering {symbol.ToDisplayString()}");
        try { action(); }
        catch (CompileError error)
        {
            if (s_trace) Console.Error.WriteLine(error.StackTrace);
            // Errors raised while mapping library types carry metadata locations; point at our symbol instead.
            Errors.Add(error.Location is not { IsInSource: true } && symbol.Locations.FirstOrDefault() is { IsInSource: true } loc
                ? new CompileError(error.Id, error.Message, loc)
                : error);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A bug in csharpc: report it against the symbol being lowered instead of crashing.
            if (s_trace) Console.Error.WriteLine(e);
            Errors.Add(new CompileError("CSLLVM9999",
                $"internal compiler error while lowering '{symbol.ToDisplayString()}': {e.GetType().Name}: {e.Message} (set CSHARPC_TRACE=1 for details)",
                symbol.Locations.FirstOrDefault()));
        }
    }

    private void CollectTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns) CollectTypes(ns);
            else if (member is INamedTypeSymbol type && IsUserType(type) && type.Locations.Any(l => l.IsInSource))
            {
                if (type.IsImplicitlyDeclared && type.TypeKind != TypeKind.Class) continue;
                _userTypes.Add(type);
                CollectTypes(type);
            }
        }
    }

    // ======================================================================================
    // Functions
    // ======================================================================================

    public LoweredFunction GetFunction(IMethodSymbol method)
    {
        if (IsNestedFunction(method)) return GetNestedFunction(method, TypeSubstitution.Identity);
        // Generic code is keyed by the *constructed* symbol: one function per instantiation.
        var generic = TypeSubstitution.IsGenericContext(method);
        if (!generic) method = method.OriginalDefinition;
        if (method.PartialDefinitionPart is { } partialDefinition) method = partialDefinition; // one function per partial member
        if (_functions.TryGetValue(method, out var existing)) return existing;
        if (generic && TypeSubstitution.IsOpen(method))
            throw CompileError.Unsupported($"open generic instantiation '{method}'", method);

        var type = FunctionTypeOf(method);
        var fn = Module.AddFunction(Mangler.Method(method), type);
        // Closed-world, whole-program compilation (like NativeAOT): everything but main is internal,
        // which lets LLVM's IPO passes inline, specialize and delete freely.
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "uwtable", 2); // C# code may throw: keep async unwind tables (ADR 0005)
        MarkIfCoroutine(method, fn);
        if (generic && IsUserMethod(method) && !method.IsAbstract) _pendingInstantiations.Enqueue(method);
        return _functions[method] = new LoweredFunction(fn, type);
    }

    /// <summary>
    /// Lambdas and local functions get one copy per substitution of their enclosing generic code
    /// (plus their own type arguments for generic local functions), keyed by (symbol, substitution).
    /// </summary>
    public LoweredFunction GetNestedFunction(IMethodSymbol method, TypeSubstitution subst)
    {
        var definition = method.OriginalDefinition;
        if (!_nestedFunctions.TryGetValue(definition, out var bySubst)) _nestedFunctions[definition] = bySubst = [];
        if (bySubst.TryGetValue(subst.Key, out var existing)) return existing;
        var type = FunctionTypeOf(definition, subst);
        var name = Mangler.Method(method);
        if (method.MethodKind == MethodKind.AnonymousFunction) name += "$lambda" + _lambdaCounter++;
        if (!subst.IsIdentity) name += "I" + subst.Key + "E";
        var fn = Module.AddFunction(name, type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "uwtable", 2); // lambdas / local functions may throw (ADR 0005)
        MarkIfCoroutine(method, fn);
        return bySubst[subst.Key] = new LoweredFunction(fn, type);
    }

    /// <summary>
    /// Whether the lowered function takes `this`. Non-static local functions report IsStatic=false
    /// but have no receiver (capturing ones get an environment from closure conversion instead).
    /// </summary>
    public static bool HasThis(IMethodSymbol method) => !method.IsStatic && !IsNestedFunction(method) && !IsExtensionMember(method);

    /// <summary>C# 14 `extension(T x) { ... }` members: lowered as static functions of their receiver.</summary>
    public static bool IsExtensionMember(IMethodSymbol method) => method.ContainingType?.TypeKind == TypeKind.Extension;

    private HashSet<IMethodSymbol>? _extensionImplementations;

    private bool IsExtensionImplementation(IMethodSymbol method)
    {
        if (!method.IsStatic || !method.IsImplicitlyDeclared) return false;
        _extensionImplementations ??= new HashSet<IMethodSymbol>(
            _userTypes.Where(t => t.TypeKind == TypeKind.Extension)
                .SelectMany(t => t.GetMembers().OfType<IMethodSymbol>())
                .Select(m => m.AssociatedExtensionImplementation)
                .OfType<IMethodSymbol>(), SymbolEqualityComparer.Default);
        return _extensionImplementations.Contains(method.OriginalDefinition);
    }

    public static bool HasExtensionReceiver(IMethodSymbol method) => IsExtensionMember(method) && !method.IsStatic;

    /// <summary>
    /// env (nested functions) or this (instance) + parameters (by-ref as ptr) -> return; constructors
    /// return void. <paramref name="subst"/> maps type parameters when the signature mentions them.
    /// </summary>
    public LLVMTypeRef FunctionTypeOf(IMethodSymbol method, TypeSubstitution? subst = null)
    {
        subst ??= TypeSubstitution.Identity;
        List<LLVMTypeRef> parameters = [];
        if (IsNestedFunction(method)) parameters.Add(Llvm.Ptr); // closure environment (may be null)
        else if (HasThis(method)) parameters.Add(Llvm.Ptr);
        else if (HasExtensionReceiver(method) && method.ContainingType.ExtensionParameter is { } receiver)
            parameters.Add(receiver.RefKind == RefKind.None ? Types.Value(subst.Apply(receiver.Type)) : Llvm.Ptr);
        foreach (var p in method.Parameters)
            parameters.Add(p.RefKind == RefKind.None ? Types.Value(subst.Apply(p.Type)) : Llvm.Ptr);
        var returnType = method.MethodKind == MethodKind.Constructor ? Llvm.Void
            : method.RefKind != RefKind.None ? Llvm.Ptr // `ref T M()` returns the address
            : Types.Value(subst.Apply(method.ReturnType));
        return Llvm.FunctionType(returnType, [.. parameters]);
    }

    /// <summary>Declares (once) an external function implemented by the C runtime (csrt).</summary>
    public LoweredFunction Runtime(string name, LLVMTypeRef ret, params LLVMTypeRef[] parameters)
    {
        if (_runtime.TryGetValue(name, out var existing)) return existing;
        var type = Llvm.FunctionType(ret, parameters);
        var fn = Module.GetOrDeclare(name, type);
        if (name.StartsWith("csrt_throw", StringComparison.Ordinal) || name == "csrt_rethrow")
        {
            Llvm.AddFunctionAttribute(fn, "noreturn");
            Llvm.AddFunctionAttribute(fn, "cold");
        }
        if (!RuntimeMayThrow(name))
        {
            Llvm.AddFunctionAttribute(fn, "nounwind");
            _noThrow.Add(fn.Handle);
        }
        // C ABI: bool/uint8_t/uint16_t arguments must be zero-extended by the caller (clang's
        // callees rely on it). Without `zeroext`, an i1 produced by `trunc` can carry garbage bits.
        for (var i = 0; i < parameters.Length; i++)
            if (parameters[i].IsInteger && parameters[i].IntWidth < 32 && !name.StartsWith("llvm.", StringComparison.Ordinal))
                Llvm.AddParameterAttribute(fn, (uint)i, "zeroext"); // (LLVM intrinsics have their own attributes, e.g. immarg)
        // Allocation returns fresh memory nothing else points to (non-moving GC), which lets LLVM
        // forward stores into new objects — e.g. devirtualize and inline a just-created delegate.
        if (name is "csrt_alloc" or "csrt_new_array") Llvm.AddReturnAttribute(fn, "noalias");
        if (name.StartsWith("csrt_", StringComparison.Ordinal)) Optimizations.AnnotateRuntimeDeclaration(Llvm, fn, ret, parameters);
        return _runtime[name] = new LoweredFunction(fn, type);
    }

    private readonly HashSet<nint> _noThrow = [];

    /// <summary>
    /// Can a runtime helper raise a managed exception (directly, or through a virtual call back into
    /// C# code)? Conservative: every csrt_* function may, except this allowlist of helpers that
    /// provably don't. LLVM intrinsics and libm never do. (Only matters inside try regions, where a
    /// may-throw call becomes an `invoke`.)
    /// </summary>
    private static bool RuntimeMayThrow(string name) =>
        name.StartsWith("csrt_", StringComparison.Ordinal)
        && !(s_noThrowRuntime.Contains(name)
             || name.StartsWith("csrt_string_from_", StringComparison.Ordinal)
             || name.StartsWith("csrt_console_write_", StringComparison.Ordinal)
             || name.StartsWith("csrt_gc_", StringComparison.Ordinal));

    private static readonly HashSet<string> s_noThrowRuntime =
    [
        "csrt_init", "csrt_shutdown", "csrt_get_args", "csrt_alloc", "csrt_begin_catch", "csrt_personality",
        "csrt_isinst_class", "csrt_string_concat", "csrt_string_equals", "csrt_string_is_null_or_empty",
        "csrt_console_newline", "csrt_object_to_string_default", "csrt_object_equals_default",
        "csrt_object_get_hash_code_default", "csrt_exception_init", "csrt_environment_tick_count64",
        "csrt_environment_exit",
    ];

    /// <summary>False for callees known not to unwind (LLVM intrinsics, libm, most of csrt): no invoke needed.</summary>
    public bool MayThrow(LLVMValueRef callee) => !_noThrow.Contains(callee.Handle);

    /// <summary>Declares an LLVM intrinsic or libm function, e.g. "llvm.sqrt.f64" or "pow".</summary>
    public LoweredFunction Intrinsic(string name, LLVMTypeRef ret, params LLVMTypeRef[] parameters) =>
        Runtime(name, ret, parameters);

    private void EmitMethod(IMethodSymbol method)
    {
        if (method.IsAbstract || method.IsExtern) return;
        if (method.IsPartialDefinition && method.PartialImplementationPart is null)
        {
            // Unimplemented partial void method: Roslyn drops the calls; give it an empty body anyway.
            if (!method.ReturnsVoid) return;
            var stub = GetFunction(method);
            using var sb = new IrBuilder(Llvm);
            sb.PositionAtEnd(sb.AppendBlock(stub.Fn, "entry"));
            sb.RetVoid();
            return;
        }
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor
            or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.UserDefinedOperator or MethodKind.Conversion
            or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.ExplicitInterfaceImplementation))
            return;
        // Field-like events have synthesized accessors; `+=`/`-=` update the backing field inline instead.
        if (method.MethodKind is MethodKind.EventAdd or MethodKind.EventRemove && method.IsImplicitlyDeclared) return;
        if (method.MethodKind == MethodKind.Constructor && method.ContainingType.IsStatic) return;
        if (method.MethodKind == MethodKind.StaticConstructor && TypeSubstitution.IsGenericContext(method))
            throw CompileError.Unsupported("static constructors in generic types", method);
        // Static field initializers run from __csharpc_static_init; only explicit cctors get a function.
        if (method.MethodKind == MethodKind.StaticConstructor && method.IsImplicitlyDeclared) return;

        // Extension blocks: Roslyn also synthesizes static "implementation" methods (sharing the member's
        // syntax); we lower the extension members themselves instead.
        if (IsExtensionImplementation(method)) return;

        // Records: members the compiler synthesizes have no syntax; generate their bodies.
        if (method.ContainingType.IsRecord && method.IsImplicitlyDeclared
            && !(method.MethodKind == MethodKind.Constructor && method.Parameters.Length != 1))
        {
            var recordGen = new FunctionCodeGen(this, method, GetFunction(method));
            if (method.AssociatedSymbol is IPropertySymbol { Name: not "EqualityContract" }) recordGen.EmitAutoPropertyAccessor();
            else recordGen.EmitRecordMember();
            return;
        }
        var definition = method.OriginalDefinition;
        var syntax = (definition.PartialImplementationPart ?? definition).DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var fn = GetFunction(method);
        var gen = new FunctionCodeGen(this, method, fn);

        List<ControlFlowGraph> prologue = [];
        if (method.MethodKind == MethodKind.Constructor)
        {
            if (syntax is TypeDeclarationSyntax { ParameterList: not null } typeDecl && !method.IsImplicitlyDeclared)
            {
                // Primary constructor: captured-parameter fields, field initializers, then base(...).
                // Roslyn exposes the base call as a ConstructorBodyOperation on the type declaration.
                var model = ModelFor(typeDecl.SyntaxTree);
                var body = model.GetOperation(typeDecl) is not null ? ControlFlowGraph.Create(typeDecl, model) : null;
                if (body is not null) Dump(method, body);
                gen.EmitPrimaryConstructor([.. InitializerGraphs(method.ContainingType, isStatic: false)], body);
                return;
            }
            // C# runs field initializers before the base constructor call, except when chaining to this(...).
            var chainsToThis = syntax is ConstructorDeclarationSyntax { Initializer.ThisOrBaseKeyword.Text: "this" };
            // A struct's implicit parameterless constructor means default(S): no initializers run.
            var structDefault = method.IsImplicitlyDeclared && method.ContainingType.IsValueType;
            if (!chainsToThis && !structDefault) prologue.AddRange(InitializerGraphs(method.ContainingType, isStatic: false));
        }

        switch (syntax)
        {
            case AccessorDeclarationSyntax { Body: null, ExpressionBody: null }:
                gen.EmitAutoPropertyAccessor(); // `{ get; set; }`
                break;
            case MethodDeclarationSyntax or ConstructorDeclarationSyntax or AccessorDeclarationSyntax
                or ArrowExpressionClauseSyntax or CompilationUnitSyntax or OperatorDeclarationSyntax
                or ConversionOperatorDeclarationSyntax:
                var cfg = ControlFlowGraph.Create(syntax, ModelFor(syntax.SyntaxTree))
                    ?? throw CompileError.Unsupported("method without a body", method);
                Dump(method, cfg);
                gen.Emit(prologue, cfg);
                break;
            case null or ClassDeclarationSyntax or StructDeclarationSyntax when method.MethodKind == MethodKind.Constructor:
                gen.EmitImplicitConstructor(prologue, zeroThis: method.ContainingType.IsValueType); // compiler-generated
                break;
            default:
                throw CompileError.Unsupported($"method body of kind {syntax.GetType().Name}", method);
        }
    }

    /// <summary>Lambdas and local functions use the closure calling convention: env first.</summary>
    public static bool IsNestedFunction(IMethodSymbol method) =>
        method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction;

    /// <summary>Queues the nested functions of a just-lowered body, instantiated with its substitution.</summary>
    internal void EnqueueNested(ClosureFunction function, TypeSubstitution subst)
    {
        foreach (var child in function.Children)
        {
            if (child.Symbol!.IsGenericMethod) continue; // generic local functions: instantiated on use
            RequestNested(child, child.Symbol, subst);
        }
    }

    /// <summary>Ensures (function, substitution) gets lowered; used directly for generic local functions.</summary>
    internal void RequestNested(ClosureFunction function, IMethodSymbol symbol, TypeSubstitution subst)
    {
        if (_enqueuedNested.Add((function, subst.Key))) _pendingNested.Enqueue((function, symbol, subst));
    }

    private void EmitNestedFunction(ClosureFunction function, IMethodSymbol symbol, TypeSubstitution subst)
    {
        Dump(symbol, function.Cfg);
        new FunctionCodeGen(this, symbol, GetNestedFunction(symbol, subst), function, subst).Emit([], function.Cfg);
    }

    /// <summary>
    /// Adapts a static method to the delegate convention fn(target, args...): the thunk ignores target.
    /// </summary>
    public LLVMValueRef StaticThunk(IMethodSymbol method)
    {
        var key = "thunk:" + Mangler.Method(method);
        if (_globals.TryGetValue(key, out var existing)) return existing;
        var target = GetFunction(method);
        List<LLVMTypeRef> parameters = [Llvm.Ptr];
        foreach (var p in method.Parameters) parameters.Add(p.RefKind == RefKind.None ? Types.Value(p.Type) : Llvm.Ptr);
        var returnType = Types.Value(method.ReturnType);
        var type = Llvm.FunctionType(returnType, [.. parameters]);
        var thunk = Module.AddFunction(Mangler.Method(method) + "$thunk", type);
        Native.LLVMSetLinkage(thunk, LLVMLinkage.Internal);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(thunk, "entry"));
        var args = Enumerable.Range(1, method.Parameters.Length).Select(i => thunk.Param((uint)i)).ToArray();
        var result = b.Call(target.Type, target.Fn, args);
        if (returnType.IsVoid) b.RetVoid(); else b.Ret(result);
        return _globals[key] = thunk;
    }

    /// <summary>Delegate objects get a per-type method table with System.Object's default vtable.</summary>
    public LLVMValueRef DelegateMethodTable(INamedTypeSymbol type)
    {
        var name = Mangler.MetadataName(type); // includes generic arguments, e.g. System.Func`2[System.Int32,System.Int32]
        var key = "dmt:" + name;
        if (_globals.TryGetValue(key, out var g)) return g;
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(name), Llvm.ConstInt(Llvm.I32, 32), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, [.. defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn)]));
        g = Module.AddGlobal("csrt.dmt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        return _globals[key] = g;
    }

    /// <summary>Field and auto-property initializers of a type, each as its own small CFG.</summary>
    private IEnumerable<ControlFlowGraph> InitializerGraphs(INamedTypeSymbol type, bool isStatic)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsStatic != isStatic || member is IFieldSymbol { IsConst: true } || member.IsImplicitlyDeclared) continue;
            EqualsValueClauseSyntax? init = member switch
            {
                IFieldSymbol f => f.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax v ? v.Initializer : null,
                IPropertySymbol p => p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax pd ? pd.Initializer : null,
                _ => null,
            };
            if (init is null) continue;
            var cfg = ControlFlowGraph.Create(init, ModelFor(init.SyntaxTree));
            if (cfg is not null) yield return cfg;
        }
    }

    private void Dump(IMethodSymbol method, ControlFlowGraph cfg)
    {
        if (CfgDump is null) return;
        CfgDump.WriteLine($"=== CFG {method.ToDisplayString()}  [{Mangler.Method(method)}]");
        foreach (var block in cfg.Blocks)
        {
            CfgDump.WriteLine($"  B{block.Ordinal} ({block.Kind}{(block.IsReachable ? "" : ", unreachable")})");
            foreach (var op in block.Operations)
                CfgDump.WriteLine($"      {op.Kind,-24} {OneLine(op.Syntax.ToString())}");
            if (block.BranchValue is { } bv)
                CfgDump.WriteLine($"    branch-value: {bv.Kind} {OneLine(bv.Syntax.ToString())}  [{block.ConditionKind}]");
            if (block.ConditionalSuccessor is { } cs)
                CfgDump.WriteLine($"    if {block.ConditionKind} -> B{cs.Destination?.Ordinal} ({cs.Semantics})");
            if (block.FallThroughSuccessor is { } fs)
                CfgDump.WriteLine($"    next -> {(fs.Destination is null ? "-" : "B" + fs.Destination.Ordinal)} ({fs.Semantics})");
        }
        static string OneLine(string s) => s.ReplaceLineEndings(" ") is var t && t.Length > 70 ? t[..67] + "..." : t;
    }

    // ======================================================================================
    // Classes: layout + method tables
    // ======================================================================================

    public ClassLayout GetLayout(INamedTypeSymbol type)
    {
        if (!TypeSubstitution.IsGenericContext(type)) type = type.OriginalDefinition;
        if (_layouts.TryGetValue(type, out var existing)) return existing;
        if (!IsUserType(type))
            return IsLibraryException(type)
                ? GetLibraryExceptionLayout(type)
                : throw CompileError.Unsupported($"instances of library class '{type}'", type);
        if (TypeSubstitution.IsOpen(type)) throw CompileError.Unsupported($"open generic type '{type}'", type);

        ClassLayout? baseLayout = null;
        if (type.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            if (!IsUserType(baseType) && !IsLibraryException(baseType))
                throw CompileError.Unsupported($"deriving from library class '{baseType}'", type);
            baseLayout = GetLayout(baseType);
        }

        var structType = Llvm.NamedStruct("class." + Mangler.MetadataName(type));
        var layout = new ClassLayout { Type = type, InstanceType = structType, Base = baseLayout };
        _layouts[type] = layout;

        // Fields: header, then inherited fields (same indices as in the base), then our own.
        // Inherit the base's storage verbatim (including synthesized library bases like System.Exception),
        // so every inherited field/event keeps its index.
        var elements = layout.Elements;
        if (baseLayout is null) elements.Add(Llvm.Ptr);
        else
        {
            elements.AddRange(baseLayout.Elements);
            layout.Fields.AddRange(baseLayout.Fields);
            foreach (var (f, index) in baseLayout.FieldIndex) layout.FieldIndex[f] = index;
            foreach (var (e, index) in baseLayout.EventFieldIndex) layout.EventFieldIndex[e] = index;
            layout.SlotOrder.AddRange(baseLayout.SlotOrder);
        }
        foreach (var f in type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst))
        {
            layout.FieldIndex[f] = (uint)elements.Count;
            layout.Fields.Add(f);
            layout.SlotOrder.Add(f);
            elements.Add(Types.Storage(f.Type));
        }
        foreach (var e in type.GetMembers().OfType<IEventSymbol>().Where(e => !e.IsStatic && e.AddMethod is null or { IsImplicitlyDeclared: true }))
        {
            layout.EventFieldIndex[e] = (uint)elements.Count;
            layout.SlotOrder.Add(e);
            elements.Add(Llvm.Ptr);
        }
        LlvmContext.SetBody(structType, [.. elements]);
        layout.InstanceSize = Types.Target.SizeOf(structType);

        // VTable: inherit the base's slots, override in place, append new virtuals.
        if (baseLayout is not null)
        {
            layout.VTable.AddRange(baseLayout.VTable);
            foreach (var (root, slot) in baseLayout.SlotOf) layout.SlotOf[root] = slot;
            foreach (var (slot, fn) in baseLayout.RuntimeSlots) layout.RuntimeSlots[slot] = fn;
        }
        else AddObjectSlots(layout);
        foreach (var m in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!(m.IsVirtual || m.IsAbstract || m.IsOverride)) continue;
            var root = ClassLayout.RootOf(m);
            if (m.IsOverride)
            {
                if (!layout.SlotOf.TryGetValue(root, out var slot))
                    throw CompileError.Unsupported($"overriding library member '{root}'", m);
                layout.VTable[slot] = m;
            }
            else
            {
                layout.SlotOf[m.OriginalDefinition] = layout.VTable.Count;
                layout.VTable.Add(m.IsAbstract ? null : m);
            }
        }
        EmitMethodTable(layout);
        return layout;
    }

    private void AddObjectSlots(ClassLayout layout)
    {
        layout.VTable.AddRange([null, null, null]);
        var obj = Compilation.GetSpecialType(SpecialType.System_Object);
        foreach (var m in obj.GetMembers().OfType<IMethodSymbol>())
        {
            switch (m.Name)
            {
                case "ToString" when m.Parameters.IsEmpty: layout.SlotOf[m] = ClassLayout.ToStringSlot; break;
                case "Equals" when m.Parameters.Length == 1 && m.IsVirtual: layout.SlotOf[m] = ClassLayout.EqualsSlot; break;
                case "GetHashCode" when m.Parameters.IsEmpty: layout.SlotOf[m] = ClassLayout.GetHashCodeSlot; break;
            }
        }
    }

    // ======================================================================================
    // Library exception classes (System.Exception & friends) — layouts shared with runtime/eh.c
    // ======================================================================================

    /// <summary>Exception types whose method tables csrt defines (runtime/eh.c, "csrt_MT_" + name).</summary>
    private static readonly HashSet<string> s_runtimeExceptionTypes =
    [
        "System.Exception", "System.SystemException", "System.ArgumentException", "System.ArgumentNullException",
        "System.ArgumentOutOfRangeException", "System.InvalidOperationException", "System.ObjectDisposedException",
        "System.NotSupportedException", "System.NotImplementedException", "System.NullReferenceException",
        "System.IndexOutOfRangeException", "System.ArithmeticException", "System.DivideByZeroException",
        "System.OverflowException", "System.InvalidCastException", "System.FormatException",
        "System.Collections.Generic.KeyNotFoundException",
    ];

    public const int ExceptionMessageSlot = 3;
    public const uint ExceptionMessageField = 1, ExceptionInnerField = 2, ExceptionParamNameField = 3;

    public bool IsLibraryException(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || IsUserType(named)) return false;
        for (var t = named; t is not null; t = t.BaseType)
            if (t.SpecialType == SpecialType.System_Object) return false;
            else if (t.ContainingNamespace?.ToDisplayString() == "System" && t.MetadataName == "Exception") return true;
        return false;
    }

    /// <summary>
    /// System.Exception and its library subclasses: { mt, message, inner, paramName }, vtable
    /// [ToString, Equals, GetHashCode, get_Message] implemented in csrt. Types csrt doesn't define
    /// (e.g. SwitchExpressionException) get a compiler-emitted method table chained to their base.
    /// </summary>
    private ClassLayout GetLibraryExceptionLayout(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (_layouts.TryGetValue(type, out var existing)) return existing;
        var name = Mangler.MetadataName(type);
        var baseLayout = type.BaseType is { SpecialType: not SpecialType.System_Object } b ? GetLibraryExceptionLayout(b) : null;

        var structType = Llvm.NamedStruct("class." + name);
        LlvmContext.SetBody(structType, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
        var layout = new ClassLayout { Type = type, InstanceType = structType, Base = baseLayout, IsLibrary = true };
        layout.Elements.AddRange([Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr]);
        layout.InstanceSize = Types.Target.SizeOf(structType);
        AddObjectSlots(layout);
        layout.VTable.Add(null);
        layout.RuntimeSlots[ClassLayout.ToStringSlot] = "csrt_exception_to_string";
        layout.RuntimeSlots[ClassLayout.EqualsSlot] = "csrt_object_equals_default";
        layout.RuntimeSlots[ClassLayout.GetHashCodeSlot] = "csrt_object_get_hash_code_default";
        layout.RuntimeSlots[ExceptionMessageSlot] = "csrt_exception_get_message";
        var exception = Compilation.GetTypeByMetadataName("System.Exception")!;
        var getMessage = exception.GetMembers("Message").OfType<IPropertySymbol>().First().GetMethod!;
        layout.SlotOf[getMessage.OriginalDefinition] = ExceptionMessageSlot;
        _layouts[type] = layout;

        if (s_runtimeExceptionTypes.Contains(name))
            layout.MethodTable = ExternalGlobal("csrt_MT_" + name.Replace('.', '_'));
        else
            EmitMethodTable(layout);
        return layout;
    }

    private LLVMValueRef RuntimeSlotFunction(string name) => name switch
    {
        "csrt_object_equals_default" => Runtime(name, Llvm.I1, Llvm.Ptr, Llvm.Ptr).Fn,
        "csrt_object_get_hash_code_default" => Runtime(name, Llvm.I32, Llvm.Ptr).Fn,
        _ => Runtime(name, Llvm.Ptr, Llvm.Ptr).Fn,
    };

    private void EmitMethodTable(ClassLayout layout)
    {
        if (layout.IsLibrary && !layout.MethodTable.IsNull) return; // defined by csrt
        var vtableType = LlvmContext.Array(Llvm.Ptr, (ulong)layout.VTable.Count);
        // Same prefix as TypeMapper.MethodTableHeader / csrt_MethodTable, followed by the vtable.
        layout.MethodTableType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32, vtableType);
        var global = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "MT"), layout.MethodTableType,
            constant: true, linkage: LLVMLinkage.Internal);
        layout.MethodTable = global; // set before initializing: methods may reference it

        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var slots = layout.VTable.Select((m, i) => m is not null
            ? GetFunction(m).Fn
            : layout.RuntimeSlots.TryGetValue(i, out var runtimeSlot)
                ? RuntimeSlotFunction(runtimeSlot)
            : i < ClassLayout.ObjectSlotCount
                ? Runtime(defaults[i], Llvm.Ptr, Llvm.Ptr).Fn
                : LlvmContext.Null(Llvm.Ptr)).ToArray();

        var baseMt = layout.Base?.MethodTable ?? ExternalGlobal("csrt_Object_MT");
        var flags = layout.Type.IsAbstract ? 1 : 0;
        var (interfaceMap, interfaceCount) = EmitInterfaceMap(layout);
        Native.LLVMSetInitializer(global, Llvm.ConstStruct(
            baseMt,
            CString(Mangler.MetadataName(layout.Type)),
            Llvm.ConstInt(Llvm.I32, (long)layout.InstanceSize),
            Llvm.ConstInt(Llvm.I32, flags),
            interfaceMap,
            Llvm.ConstInt(Llvm.I32, interfaceCount),
            Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, slots)));
    }

    // ======================================================================================
    // Globals: static fields, string literals, runtime symbols
    // ======================================================================================

    public LLVMValueRef StaticField(IFieldSymbol field)
    {
        if (!TypeSubstitution.IsGenericContext(field.ContainingType)) field = field.OriginalDefinition;
        if (_staticFields.TryGetValue(field, out var global)) return global;
        if (!IsUserType(field.ContainingType)) throw CompileError.Unsupported($"static field '{field}' of a library type", field);
        var type = Types.Storage(field.Type);
        return _staticFields[field] = Module.AddGlobal(Mangler.StaticField(field), type, LlvmContext.Null(type),
            linkage: LLVMLinkage.Internal);
    }

    /// <summary>A statically allocated, immutable instance of a library class (e.g. EventArgs.Empty).</summary>
    public LLVMValueRef LibrarySingleton(INamedTypeSymbol type)
    {
        var key = "single:" + Mangler.MetadataName(type);
        if (_globals.TryGetValue(key, out var g)) return g;
        var init = Llvm.ConstStruct(DelegateMethodTable(type), Llvm.ConstInt(Llvm.I64, 0));
        return _globals[key] = Module.AddGlobal("csrt.singleton." + Mangler.MetadataName(type), Native.LLVMTypeOf(init), init,
            constant: true, linkage: LLVMLinkage.Private);
    }

    /// <summary>Delegate slot of a static field-like event.</summary>
    public LLVMValueRef StaticEventField(IEventSymbol e)
    {
        var name = Mangler.TypeSymbol(e.ContainingType, e.Name + "$event");
        if (_globals.TryGetValue(name, out var g)) return g;
        return _globals[name] = Module.AddGlobal(name, Llvm.Ptr, LlvmContext.Null(Llvm.Ptr), linkage: LLVMLinkage.Internal);
    }

    public LLVMValueRef ExternalGlobal(string name)
    {
        if (_globals.TryGetValue(name, out var g)) return g;
        return _globals[name] = Module.AddGlobal(name, Llvm.I8, constant: true);
    }

    /// <summary>A NUL-terminated UTF-8 constant (for type names in method tables).</summary>
    public LLVMValueRef CString(string text)
    {
        var key = "c:" + text;
        if (_globals.TryGetValue(key, out var g)) return g;
        var init = Llvm.ConstCString(text);
        g = Module.AddGlobal(".str.c", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        Native.LLVMSetUnnamedAddress(g, 2);
        return _globals[key] = g;
    }

    /// <summary>
    /// String literals become constant, statically-initialized System.String objects in .rodata:
    /// { ptr @csrt_String_MT, i32 len, [len x i16] } — no allocation or interning table at runtime.
    /// </summary>
    public LLVMValueRef StringLiteral(string value)
    {
        if (_strings.TryGetValue(value, out var g)) return g;
        var chars = value.Select(c => Llvm.ConstInt(Llvm.I16, c)).ToArray();
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_String_MT"), Llvm.ConstInt(Llvm.I32, value.Length),
            LlvmContext.ConstArray(Llvm.I16, chars));
        g = Module.AddGlobal(".str", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        Native.LLVMSetUnnamedAddress(g, 2);
        Native.LLVMSetAlignment(g, 8);
        return _strings[value] = g;
    }

    private readonly Dictionary<INamedTypeSymbol, LoweredFunction> _enumFormatters = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Enum.ToString(): a generated `switch` from value to name literal; undefined values fall back
    /// to the number, as in .NET. [Flags] combinations are not decomposed yet.
    /// </summary>
    public LoweredFunction EnumFormatter(INamedTypeSymbol enumType)
    {
        enumType = enumType.OriginalDefinition;
        if (_enumFormatters.TryGetValue(enumType, out var existing)) return existing;
        var isFlags = enumType.GetAttributes().Any(a => a.AttributeClass?.Name == "FlagsAttribute");
        var underlying = enumType.EnumUnderlyingType!;
        var valueType = Types.Value(underlying);
        var type = Llvm.FunctionType(Llvm.Ptr, valueType);
        var fn = Module.AddFunction(Mangler.TypeSymbol(enumType, "ToString"), type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        var lowered = _enumFormatters[enumType] = new LoweredFunction(fn, type);

        using var b = new IrBuilder(Llvm);
        var entry = b.AppendBlock(fn, "entry");
        var fallback = b.AppendBlock(fn, "number");
        List<(LLVMValueRef, LLVMBasicBlockRef)> cases = [];
        HashSet<ulong> seen = [];
        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
        {
            var raw = field.ConstantValue switch
            {
                ulong u => u, uint u => u, ushort u => u, byte u => u,
                var v => unchecked((ulong)Convert.ToInt64(v)),
            };
            if (!seen.Add(raw)) continue; // first declared name wins for duplicate values
            var block = b.AppendBlock(fn, field.Name);
            b.PositionAtEnd(block);
            b.Ret(StringLiteral(field.Name));
            cases.Add((Llvm.ConstUInt(valueType, raw), block));
        }
        b.PositionAtEnd(entry);
        b.Switch(fn.Param(0), fallback, cases);
        b.PositionAtEnd(fallback);
        if (isFlags)
        {
            // Not an exact name: let the runtime decompose into flag names (or fall back to the number).
            var members = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
                .Select(f => (Raw: f.ConstantValue switch { ulong u => u, uint u => u, ushort u => u, byte u => u, var v => unchecked((ulong)Convert.ToInt64(v)) }, f.Name))
                .Select(m => (Raw: valueType.IntWidth == 64 ? m.Raw : m.Raw & ((1UL << (int)valueType.IntWidth) - 1), m.Name))
                .GroupBy(m => m.Raw).Select(g => g.First()).OrderBy(m => m.Raw).ToList();
            var valuesInit = LlvmContext.ConstArray(Llvm.I64, [.. members.Select(m => Llvm.ConstUInt(Llvm.I64, m.Raw))]);
            var namesInit = LlvmContext.ConstArray(Llvm.Ptr, [.. members.Select(m => StringLiteral(m.Name))]);
            var valuesTable = Module.AddGlobal(Mangler.TypeSymbol(enumType, "FlagValues"), Native.LLVMTypeOf(valuesInit), valuesInit, constant: true, linkage: LLVMLinkage.Internal);
            var namesTable = Module.AddGlobal(Mangler.TypeSymbol(enumType, "FlagNames"), Native.LLVMTypeOf(namesInit), namesInit, constant: true, linkage: LLVMLinkage.Internal);
            var signed = TypeMapper.IsSigned(underlying);
            var asU64 = valueType.IntWidth == 64 ? fn.Param(0) : b.Cast(LLVMOpcode.ZExt, fn.Param(0), Llvm.I64);
            var asI64 = valueType.IntWidth == 64 ? fn.Param(0) : b.Cast(signed ? LLVMOpcode.SExt : LLVMOpcode.ZExt, fn.Param(0), Llvm.I64);
            var flags = Runtime("csrt_enum_format_flags", Llvm.Ptr, Llvm.I64, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I1, Llvm.I64);
            b.Ret(b.Call(flags.Type, flags.Fn, [asU64, valuesTable, namesTable, Llvm.ConstInt(Llvm.I32, members.Count),
                signed ? Llvm.True : Llvm.False, asI64]));
            return lowered;
        }
        var (name, argType, arg) = TypeMapper.IsSigned(underlying)
            ? ("csrt_string_from_i64", Llvm.I64, valueType.IntWidth == 64 ? fn.Param(0) : b.Cast(LLVMOpcode.SExt, fn.Param(0), Llvm.I64))
            : ("csrt_string_from_u64", Llvm.I64, valueType.IntWidth == 64 ? fn.Param(0) : b.Cast(LLVMOpcode.ZExt, fn.Param(0), Llvm.I64));
        var format = Runtime(name, Llvm.Ptr, argType);
        b.Ret(b.Call(format.Type, format.Fn, [arg]));
        return lowered;
    }

    // ======================================================================================
    // Program startup
    // ======================================================================================

    private LoweredFunction _staticInit;

    /// <summary>
    /// Runs every static field initializer and static constructor, in declaration order, before Main.
    /// (Deviation: .NET runs these lazily per type — docs/03-lowering.md#static-initialization.)
    /// </summary>
    private void EmitStaticInitializer()
    {
        var type = Llvm.FunctionType(Llvm.Void);
        var fn = Module.AddFunction("__csharpc_static_init", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        _staticInit = new LoweredFunction(fn, type);

        List<ControlFlowGraph> graphs = [];
        List<IMethodSymbol> cctors = [];
        foreach (var t in _userTypes.Where(t => t.TypeKind is TypeKind.Class or TypeKind.Struct))
        {
            if (TypeSubstitution.IsGenericContext(t))
            {
                if (InitializerGraphs(t, isStatic: true).Any())
                    throw CompileError.Unsupported($"static field initializers in generic type '{t.Name}'", t);
                continue;
            }
            graphs.AddRange(InitializerGraphs(t, isStatic: true));
            cctors.AddRange(t.StaticConstructors.Where(c => !c.IsImplicitlyDeclared));
        }
        new FunctionCodeGen(this, null, _staticInit).EmitStaticInit(graphs, cctors);
    }

    /// <summary>True once any async method or iterator was lowered (the driver must run CoroSplit).</summary>
    public bool HasCoroutines { get; set; }

    /// <summary>
    /// C-ABI entry points the runtime uses to resume/destroy coroutine frames. The split resume and
    /// destroy functions are fastcc, so the runtime goes through llvm.coro.resume/destroy here.
    /// </summary>
    private void MarkIfCoroutine(IMethodSymbol method, LLVMValueRef fn)
    {
        if (Coroutines.KindOf(method) == CoroutineKind.None) return;
        Llvm.AddFunctionAttribute(fn, "presplitcoroutine"); // async/iterator: split by CoroSplit
        EnsureCoroutineSupport();
    }

    private void EnsureCoroutineSupport()
    {
        if (HasCoroutines) return;
        HasCoroutines = true;
        foreach (var (name, intrinsic) in new[] { ("csrt_coro_resume", "llvm.coro.resume"), ("csrt_coro_destroy", "llvm.coro.destroy") })
        {
            var type = Llvm.FunctionType(Llvm.Void, Llvm.Ptr);
            var fn = Module.AddFunction(name, type);
            var target = Intrinsic(intrinsic, Llvm.Void, Llvm.Ptr);
            using var b = new IrBuilder(Llvm);
            b.PositionAtEnd(b.AppendBlock(fn, "entry"));
            b.Call(target.Type, target.Fn, [fn.Param(0)]);
            b.RetVoid();
        }
    }

    /// <summary>
    /// ptr clone(ptr args): re-invokes an iterator method with its saved arguments, for
    /// GetEnumerator() on an enumerable that has already been enumerated.
    /// </summary>
    public LLVMValueRef IteratorCloneThunk(IMethodSymbol method, LoweredFunction target, LLVMTypeRef argsType, int count)
    {
        var key = "clone:" + Native.LLVMGetValueName(target.Fn);
        if (_globals.TryGetValue(key, out var existing)) return existing;
        var type = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr);
        var fn = Module.AddFunction(Mangler.Method(method) + "$clone", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        var args = new LLVMValueRef[count];
        for (var i = 0; i < count; i++)
            args[i] = b.Load(Native.LLVMStructGetTypeAtIndex(argsType, (uint)i), b.StructGep(argsType, fn.Param(0), (uint)i));
        b.Ret(b.Call(target.Type, target.Fn, args));
        return _globals[key] = fn;
    }

    private void EmitNativeMain(IMethodSymbol entryPoint)
    {
        var type = Llvm.FunctionType(Llvm.I32, Llvm.I32, Llvm.Ptr);
        var main = Module.AddFunction("main", type);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(main, "entry"));

        var init = Runtime("csrt_init", Llvm.Void, Llvm.I32, Llvm.Ptr);
        b.Call(init.Type, init.Fn, [main.Param(0), main.Param(1)]);
        b.Call(_staticInit.Type, _staticInit.Fn, []);

        var target = GetFunction(entryPoint);
        LLVMValueRef[] args = [];
        if (entryPoint.Parameters.Length == 1)
        {
            var getArgs = Runtime("csrt_get_args", Llvm.Ptr);
            args = [b.Call(getArgs.Type, getArgs.Fn, [])];
        }
        var result = b.Call(target.Type, target.Fn, args);
        var returnsVoid = entryPoint.ReturnsVoid;
        if (Coroutines.IsTaskLike(entryPoint.ReturnType))
        {
            // async Main: drive the scheduler until the returned task completes.
            var wait = Runtime("csrt_task_wait", Llvm.Void, Llvm.Ptr);
            b.Call(wait.Type, wait.Fn, [result]);
            returnsVoid = Coroutines.AsyncResultType(entryPoint.ReturnType) is null;
            if (!returnsVoid)
            {
                var payload = Runtime("csrt_task_result_ptr", Llvm.Ptr, Llvm.Ptr);
                result = b.Load(Llvm.I32, b.Call(payload.Type, payload.Fn, [result]));
            }
        }

        var shutdown = Runtime("csrt_shutdown", Llvm.Void);
        b.Call(shutdown.Type, shutdown.Fn, []);
        b.Ret(returnsVoid ? Llvm.ConstInt(Llvm.I32, 0) : result);
    }
}
