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
    private readonly Queue<(IMethodSymbol Method, ControlFlowGraph Cfg, TypeSubstitution Subst)> _pendingLocalFunctions = new();
    /// <summary>Generic instantiations (constructed methods) whose bodies still need lowering.</summary>
    private readonly Queue<IMethodSymbol> _pendingInstantiations = new();
    private readonly Dictionary<string, LoweredFunction> _localFunctions = [];
    private readonly Dictionary<IMethodSymbol, (ControlFlowGraph Cfg, TypeSubstitution Subst)> _genericLocalFunctions = new(SymbolEqualityComparer.Default);
    private readonly List<INamedTypeSymbol> _userTypes = [];

    public ModuleCodeGen(Compilation compilation, LlvmContext llvm, LlvmModule module, TargetMachine target)
    {
        Compilation = compilation;
        Llvm = llvm;
        Module = module;
        Types = new TypeMapper(llvm, target);
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
                    case TypeKind.Class or TypeKind.Enum or TypeKind.Interface: break;
                    case TypeKind.Struct: throw CompileError.Unsupported($"struct '{type.Name}'", type);
                    case TypeKind.Delegate: throw CompileError.Unsupported($"delegate '{type.Name}'", type);
                    default: throw CompileError.Unsupported($"{type.TypeKind} '{type.Name}'", type);
                }
                if (type.IsRecord) throw CompileError.Unsupported($"record '{type.Name}'", type);
            });

        // 2. Method bodies of non-generic code; generic instantiations are queued as they're referenced.
        foreach (var type in _userTypes.Where(t => t.TypeKind is TypeKind.Class or TypeKind.Interface && !TypeSubstitution.IsGenericContext(t)))
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
            if (_pendingLocalFunctions.TryDequeue(out var local))
                Guard(local.Method, () => EmitLocalFunction(local.Method, local.Cfg, local.Subst));
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
            Errors.Add(error.Location is null && symbol.Locations.FirstOrDefault() is { } loc
                ? new CompileError(error.Id, error.Message, loc)
                : error);
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
        // Generic code is keyed by the *constructed* symbol: one function per instantiation.
        var generic = TypeSubstitution.IsGenericContext(method);
        if (!generic) method = method.OriginalDefinition;
        if (_functions.TryGetValue(method, out var existing)) return existing;
        if (method.MethodKind == MethodKind.LocalFunction) return GetLocalFunction(method, TypeSubstitution.Identity);
        if (generic && TypeSubstitution.IsOpen(method))
            throw CompileError.Unsupported($"open generic instantiation '{method}'", method);
        if (method.IsAsync) throw CompileError.Unsupported($"async method '{method.Name}'", method);

        var type = FunctionTypeOf(method);
        var fn = Module.AddFunction(Mangler.Method(method), type);
        // Closed-world, whole-program compilation (like NativeAOT): everything but main is internal,
        // which lets LLVM's IPO passes inline, specialize and delete freely.
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "nounwind"); // no unwinding until exceptions land (ADR 0005)
        if (generic && IsUserMethod(method) && !method.IsAbstract) _pendingInstantiations.Enqueue(method);
        return _functions[method] = new LoweredFunction(fn, type);
    }

    /// <summary>
    /// Local functions inside generic code are instantiated with their enclosing method's
    /// substitution, so they're keyed by (symbol, substitution).
    /// </summary>
    public LoweredFunction GetLocalFunction(IMethodSymbol method, TypeSubstitution subst)
    {
        var enqueue = false;
        if (method.IsGenericMethod)
        {
            // Instantiate a generic local function: its CFG + the enclosing substitution + its own type arguments.
            if (!_genericLocalFunctions.TryGetValue(method.OriginalDefinition, out var source))
                throw CompileError.Unsupported($"generic local function '{method.Name}' used before its declaration was lowered", method);
            if (TypeSubstitution.IsOpen(method)) throw CompileError.Unsupported($"open generic instantiation '{method}'", method);
            subst = source.Subst.With(method.OriginalDefinition.TypeParameters, method.TypeArguments, Compilation);
            enqueue = true;
        }
        var name = LocalFunctionName(method, subst);
        if (_localFunctions.TryGetValue(name, out var existing)) return existing;
        if (enqueue) _pendingLocalFunctions.Enqueue((method, _genericLocalFunctions[method.OriginalDefinition].Cfg, subst));
        var type = FunctionTypeOf(method, subst);
        var fn = Module.AddFunction(name, type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "nounwind");
        return _localFunctions[name] = new LoweredFunction(fn, type);
    }

    private static string LocalFunctionName(IMethodSymbol method, TypeSubstitution subst) =>
        Mangler.Method(method) + (subst.IsIdentity ? "" : "I" + subst.Key + "E");

    /// <summary>this (if instance) + parameters (by-ref as ptr) -> return; constructors return void.</summary>
    public LLVMTypeRef FunctionTypeOf(IMethodSymbol method, TypeSubstitution? subst = null)
    {
        subst ??= TypeSubstitution.Identity;
        List<LLVMTypeRef> parameters = [];
        if (!method.IsStatic) parameters.Add(Llvm.Ptr);
        foreach (var p in method.Parameters)
            parameters.Add(p.RefKind == RefKind.None ? Types.Value(subst.Apply(p.Type)) : Llvm.Ptr);
        var returnType = method.MethodKind == MethodKind.Constructor ? Llvm.Void : Types.Value(subst.Apply(method.ReturnType));
        return Llvm.FunctionType(returnType, [.. parameters]);
    }

    /// <summary>Declares (once) an external function implemented by the C runtime (csrt).</summary>
    public LoweredFunction Runtime(string name, LLVMTypeRef ret, params LLVMTypeRef[] parameters)
    {
        if (_runtime.TryGetValue(name, out var existing)) return existing;
        var type = Llvm.FunctionType(ret, parameters);
        var fn = Module.GetOrDeclare(name, type);
        if (name.StartsWith("csrt_throw", StringComparison.Ordinal))
        {
            Llvm.AddFunctionAttribute(fn, "noreturn");
            Llvm.AddFunctionAttribute(fn, "cold");
        }
        Llvm.AddFunctionAttribute(fn, "nounwind");
        return _runtime[name] = new LoweredFunction(fn, type);
    }

    /// <summary>Declares an LLVM intrinsic or libm function, e.g. "llvm.sqrt.f64" or "pow".</summary>
    public LoweredFunction Intrinsic(string name, LLVMTypeRef ret, params LLVMTypeRef[] parameters) =>
        Runtime(name, ret, parameters);

    private void EmitMethod(IMethodSymbol method)
    {
        if (method.IsAbstract || method.IsExtern || method.IsPartialDefinition) return;
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor
            or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.UserDefinedOperator or MethodKind.Conversion
            or MethodKind.ExplicitInterfaceImplementation))
            return;
        if (method.MethodKind == MethodKind.Constructor && method.ContainingType.IsStatic) return;
        if (method.MethodKind == MethodKind.StaticConstructor && TypeSubstitution.IsGenericContext(method))
            throw CompileError.Unsupported("static constructors in generic types", method);

        var syntax = method.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var fn = GetFunction(method);
        var gen = new FunctionCodeGen(this, method, fn);

        List<ControlFlowGraph> prologue = [];
        if (method.MethodKind == MethodKind.Constructor)
        {
            if (syntax is TypeDeclarationSyntax { ParameterList: not null } typeDecl)
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
            if (!chainsToThis) prologue.AddRange(InitializerGraphs(method.ContainingType, isStatic: false));
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
            case null or ClassDeclarationSyntax when method.MethodKind == MethodKind.Constructor:
                gen.EmitImplicitConstructor(prologue); // compiler-generated default constructor
                break;
            default:
                throw CompileError.Unsupported($"method body of kind {syntax.GetType().Name}", method);
        }
    }

    internal void EnqueueLocalFunctions(ControlFlowGraph cfg, TypeSubstitution subst)
    {
        foreach (var local in cfg.LocalFunctions)
        {
            var localCfg = cfg.GetLocalFunctionControlFlowGraph(local);
            if (local.IsGenericMethod) _genericLocalFunctions[local] = (localCfg, subst); // instantiated on use
            else _pendingLocalFunctions.Enqueue((local, localCfg, subst));
        }
    }

    private void EmitLocalFunction(IMethodSymbol method, ControlFlowGraph cfg, TypeSubstitution subst)
    {
        Dump(method, cfg);
        var fn = method.IsGenericMethod ? _localFunctions[LocalFunctionName(method, subst)] : GetLocalFunction(method, subst);
        new FunctionCodeGen(this, method, fn, subst).Emit([], cfg);
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
        if (!IsUserType(type)) throw CompileError.Unsupported($"instances of library class '{type}'", type);
        if (TypeSubstitution.IsOpen(type)) throw CompileError.Unsupported($"open generic type '{type}'", type);

        ClassLayout? baseLayout = null;
        if (type.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            if (!IsUserType(baseType)) throw CompileError.Unsupported($"deriving from library class '{baseType}'", type);
            baseLayout = GetLayout(baseType);
        }

        var structType = Llvm.NamedStruct("class." + Mangler.MetadataName(type));
        var layout = new ClassLayout { Type = type, InstanceType = structType, Base = baseLayout };
        _layouts[type] = layout;

        // Fields: header, then inherited fields (same indices as in the base), then our own.
        List<LLVMTypeRef> elements = [Llvm.Ptr];
        if (baseLayout is not null)
            foreach (var f in baseLayout.Fields)
            {
                layout.FieldIndex[f] = (uint)elements.Count;
                layout.Fields.Add(f);
                elements.Add(Types.Storage(f.Type));
            }
        foreach (var f in type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst))
        {
            layout.FieldIndex[f] = (uint)elements.Count;
            layout.Fields.Add(f);
            elements.Add(Types.Storage(f.Type));
        }
        LlvmContext.SetBody(structType, [.. elements]);
        layout.InstanceSize = Types.Target.SizeOf(structType);

        // VTable: inherit the base's slots, override in place, append new virtuals.
        if (baseLayout is not null)
        {
            layout.VTable.AddRange(baseLayout.VTable);
            foreach (var (root, slot) in baseLayout.SlotOf) layout.SlotOf[root] = slot;
        }
        else
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

    private void EmitMethodTable(ClassLayout layout)
    {
        var vtableType = LlvmContext.Array(Llvm.Ptr, (ulong)layout.VTable.Count);
        // Same prefix as TypeMapper.MethodTableHeader / csrt_MethodTable, followed by the vtable.
        layout.MethodTableType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32, vtableType);
        var global = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "MT"), layout.MethodTableType,
            constant: true, linkage: LLVMLinkage.Internal);
        layout.MethodTable = global; // set before initializing: methods may reference it

        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var slots = layout.VTable.Select((m, i) => m is not null
            ? GetFunction(m).Fn
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

    /// <summary>Minimal method table for library exception types we can throw (name only).</summary>
    public LLVMValueRef LibraryTypeMethodTable(INamedTypeSymbol type)
    {
        var name = Mangler.MetadataName(type);
        var key = "mt:" + name;
        if (_globals.TryGetValue(key, out var g)) return g;
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        g = Module.AddGlobal("csrt.mt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        return _globals[key] = g;
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
        foreach (var t in _userTypes.Where(t => t.TypeKind == TypeKind.Class))
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

        var shutdown = Runtime("csrt_shutdown", Llvm.Void);
        b.Call(shutdown.Type, shutdown.Fn, []);
        b.Ret(entryPoint.ReturnsVoid ? Llvm.ConstInt(Llvm.I32, 0) : result);
    }
}
