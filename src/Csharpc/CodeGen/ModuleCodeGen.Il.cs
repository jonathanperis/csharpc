using System.Reflection;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.CodeGen;

/// <summary>
/// The IL front-end's module-level hooks (docs/adr/0010-il-frontend.md). Types of assemblies compiled
/// from IL are "user" types — csharpc owns their layout, method tables and code, exactly like source
/// types — but their methods are lowered on demand, from IL, as the program reaches them.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>Assemblies compiled from IL (empty when the program has none).</summary>
    public IlAssemblySet Il { get; init; } = IlAssemblySet.Empty;

    /// <summary>--dump-il: where to print the decoded IL of lowered methods.</summary>
    public TextWriter? IlDump { get; init; }

    public bool IsIlType(ITypeSymbol? type) => type is INamedTypeSymbol && Il.Contains(type.ContainingAssembly);

    public bool IsIlMethod(IMethodSymbol method) => Il.Contains(method.ContainingAssembly);

    private List<INamedTypeSymbol>? _ilTypes;

    /// <summary>Constant globals holding RVA field data (array initializers, span literals), per field.</summary>
    internal Dictionary<IFieldSymbol, (LLVMValueRef Global, int Size)> IlRvaGlobals { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>Every type defined in an IL assembly (part of the closed world's class hierarchy).</summary>
    internal IReadOnlyList<INamedTypeSymbol> IlTypes
    {
        get
        {
            if (_ilTypes is not null) return _ilTypes;
            var types = new List<INamedTypeSymbol>();
            void Collect(INamespaceOrTypeSymbol container)
            {
                foreach (var member in container.GetMembers())
                    if (member is INamespaceSymbol ns) Collect(ns);
                    else if (member is INamedTypeSymbol type) { types.Add(type); Collect(type); }
            }
            foreach (var assembly in Il.Assemblies) Collect(assembly.Symbol.GlobalNamespace);
            return _ilTypes = types;
        }
    }

    /// <summary>Source and IL types: the universe for class-hierarchy analysis (devirtualization, generic virtuals).</summary>
    private IEnumerable<INamedTypeSymbol> HierarchyTypes => Il.IsEmpty ? _userTypes : _userTypes.Concat(IlTypes);

    private readonly Dictionary<IlAssembly, (SyntaxNode Plain, SyntaxNode Base)> _ilSyntax = [];

    /// <summary>
    /// Synthetic operations need a syntax node (diagnostics read its location; `base.M()` is recognized by
    /// its syntax). One tiny tree per assembly, named after the file, so errors point at the DLL.
    /// </summary>
    internal (SyntaxNode Plain, SyntaxNode Base) IlSyntax(IlAssembly assembly)
    {
        if (_ilSyntax.TryGetValue(assembly, out var nodes)) return nodes;
        var plain = CSharpSyntaxTree.Create(SyntaxFactory.IdentifierName("IL"), path: assembly.Path).GetRoot();
        var @base = CSharpSyntaxTree.Create(SyntaxFactory.BaseExpression(), path: assembly.Path).GetRoot();
        return _ilSyntax[assembly] = (plain, @base);
    }

    private void EmitIlMethod(IlAssembly assembly, IMethodSymbol method)
    {
        if (method.IsAbstract || method.IsExtern) return;
        var location = IlSyntax(assembly).Plain.GetLocation();
        try
        {
            var block = assembly.Body(method)
                ?? throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: '{method}' has no IL body (runtime-implemented method in {assembly.Name}.dll)", location);
            IlMethodBody body;
            try { body = IlMethodBody.Decode(assembly, method, block); }
            catch (Exception e) when (e is IlException or BadImageFormatException)
            {
                throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: {e.Message} [in '{method}' ({assembly.Name}.dll)]", location);
            }
            if (IlDump is { } dump)
            {
                dump.WriteLine($"=== IL {method.ToDisplayString()}  [{Mangler.Method(method)}]");
                dump.Write(body.Dump());
            }
            new FunctionCodeGen(this, method, _functions[method]).EmitIl(body);
        }
        catch (CompileError e) { IlMethodFailed(method, e); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (s_trace) Console.Error.WriteLine(e);
            IlMethodFailed(method, new CompileError("CSLLVM9999",
                $"internal compiler error while lowering '{method}' from IL: {e.GetType().Name}: {e.Message} (set CSHARPC_TRACE=1 for details)", location));
        }
    }

    // ======================================================================================
    // Methods that could not be compiled, and whether the program can reach them
    // ======================================================================================
    //
    // A package's method tables and interface maps reference every virtual method of the types the
    // program uses, so a library pulls in code the program never runs (async helpers, serialization
    // hooks...). A method csharpc cannot compile is therefore not reported as an error right away:
    // its function becomes a stub that ends the program with the diagnostic if it is ever called.
    // At the end, a class-hierarchy analysis from the program's own code decides: reachable stubs are
    // errors, as in the source path; unreachable ones are warnings (CSLLVM0007). The stub never runs
    // silently: calling it prints the diagnostic and aborts.

    private IMethodSymbol? _currentCaller;
    private bool _slotReference;
    private readonly List<(IMethodSymbol? From, IMethodSymbol To, bool Virtual)> _references = [];
    private readonly List<(IMethodSymbol Method, CompileError Error)> _ilFailures = [];

    /// <summary>The function lowered now references <paramref name="method"/> (a call, a delegate, a thunk...).</summary>
    internal void NoteReference(IMethodSymbol method, bool virtualCall)
    {
        if (Il.IsEmpty) return;
        _references.Add((_currentCaller, method, virtualCall));
    }

    /// <summary>A virtual or interface call site: every override / implementation of the member may run.</summary>
    internal void NoteVirtualCall(IMethodSymbol target) => NoteReference(target, virtualCall: true);

    /// <summary>A check that runs <paramref name="type"/>'s static constructor.</summary>
    internal void NoteClassInit(INamedTypeSymbol type)
    {
        if (!Il.IsEmpty && type.StaticConstructors.FirstOrDefault() is { } cctor) NoteReference(cctor, virtualCall: false);
    }

    /// <summary>
    /// A function for a method table or interface map slot. Such a reference doesn't make the method
    /// reachable by itself, and an IL method whose signature csharpc cannot represent (no call site can
    /// reach it either: lowering the call would fail first) fills its slot with a stub.
    /// </summary>
    internal LLVMValueRef SlotFunction(IMethodSymbol method)
    {
        var outer = _slotReference;
        _slotReference = true;
        try { return GetFunction(method).Fn; }
        catch (CompileError e) when (IsIlMethod(method))
        {
            return UnsupportedStub($"{Mangler.Method(method)}$unsupported", Llvm.FunctionType(Llvm.Void),
                $"'{method}' could not be compiled: {e.Message}");
        }
        finally { _slotReference = outer; }
    }

    private void IlMethodFailed(IMethodSymbol method, CompileError error)
    {
        if (!error.Message.Contains(" [in '", StringComparison.Ordinal))
            error = new CompileError(error.Id, $"{error.Message} [in '{method.OriginalDefinition}' ({Il.OwnerOf(method)?.Name}.dll)]",
                error.Location is { IsInSource: true } ? error.Location : Il.OwnerOf(method) is { } owner ? IlSyntax(owner).Plain.GetLocation() : null);
        _ilFailures.Add((method, error));
        var old = _functions[method];
        var name = Native.TakeName(old.Fn);
        var stub = UnsupportedStub(name + "$unsupported", old.Type, $"'{method}' could not be compiled: {error.Message}");
        Native.LLVMReplaceAllUsesWith(old.Fn, stub);
        Native.LLVMDeleteFunction(old.Fn);
        Native.LLVMSetValueName2(stub, name, (nuint)System.Text.Encoding.UTF8.GetByteCount(name));
        _functions[method] = new LoweredFunction(stub, old.Type);
    }

    private LLVMValueRef UnsupportedStub(string name, LLVMTypeRef type, string message)
    {
        var fn = Module.AddFunction(name, type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "cold");
        Llvm.AddFunctionAttribute(fn, "noinline");
        var fail = Runtime("csrt_unsupported_method", Llvm.Void, Llvm.Ptr);
        Llvm.AddFunctionAttribute(fail.Fn, "noreturn");
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        b.Call(fail.Type, fail.Fn, [CString(message)]);
        b.Unreachable();
        return fn;
    }

    /// <summary>Errors for failed IL methods the program can reach; warnings (and the stub) for the rest.</summary>
    private void ReportIlFailures()
    {
        if (_ilFailures.Count == 0) return;
        var edges = _references.GroupBy(r => (ISymbol?)r.From?.OriginalDefinition, SymbolEqualityComparer.Default)
            .ToDictionary(g => g.Key ?? Compilation.Assembly, g => g.ToList(), SymbolEqualityComparer.Default);
        var reached = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var work = new Queue<IMethodSymbol>();
        void Reach(IMethodSymbol m)
        {
            if (reached.Add(m.OriginalDefinition)) work.Enqueue(m);
        }
        void Follow(IMethodSymbol to, bool isVirtual)
        {
            Reach(to);
            if (isVirtual) foreach (var implementation in PossibleTargets(to)) Reach(implementation);
        }
        // Roots: references made by the program's own code (sources and the corelib).
        foreach (var (from, to, isVirtual) in _references)
            if (from is null || !IsIlMethod(from)) Follow(to, isVirtual);
        while (work.TryDequeue(out var method))
            if (edges.TryGetValue(method.OriginalDefinition, out var outgoing))
                foreach (var (_, to, isVirtual) in outgoing) Follow(to, isVirtual);

        foreach (var (method, error) in _ilFailures)
        {
            if (reached.Contains(method.OriginalDefinition)) Errors.Add(error);
            else Warnings.Add(new CompileError("CSLLVM0007",
                $"{error.Message} — the program does not reach this method (class-hierarchy analysis), so it was compiled as a stub that ends the program if it is ever called",
                error.Location));
        }
    }

    /// <summary>Overrides (class virtuals) or implementations (interface members) a virtual call to <paramref name="target"/> can run.</summary>
    private IEnumerable<IMethodSymbol> PossibleTargets(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        if (definition.ContainingType.TypeKind == TypeKind.Interface)
        {
            var itf = definition.ContainingType;
            foreach (var type in HierarchyTypes)
                foreach (var implemented in type.AllInterfaces.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, itf)))
                {
                    var member = implemented.GetMembers(definition.Name).OfType<IMethodSymbol>()
                        .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, definition));
                    if (member is not null && type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation)
                        yield return implementation;
                }
            yield break;
        }
        var root = ClassLayout.RootOf(definition);
        foreach (var type in HierarchyTypes)
            foreach (var m in type.GetMembers(definition.Name).OfType<IMethodSymbol>())
                if ((m.IsVirtual || m.IsOverride) && SymbolEqualityComparer.Default.Equals(ClassLayout.RootOf(m), root))
                    yield return m;
    }

    /// <summary>beforefieldinit: the type's initializer runs on first static field access, not on first use (ECMA-335 II.10.5.3.3).</summary>
    private bool IlBeforeFieldInit(INamedTypeSymbol type) =>
        Il.OwnerOf(type) is { } owner && (owner.AttributesOf(type) & TypeAttributes.BeforeFieldInit) != 0;

    /// <summary>
    /// IL types keep explicit layouts in metadata (pseudo-attributes), not in GetAttributes(): reject what
    /// csharpc's sequential layout would silently get wrong. An empty struct's `.size 1` is the default.
    /// </summary>
    private void RejectIlCustomLayout(INamedTypeSymbol type)
    {
        if (Il.OwnerOf(type) is not { } owner) return;
        var definition = owner.TypeDefinitionOf(type);
        var layout = definition.GetLayout();
        var hasFields = type.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic && !f.IsConst);
        if ((definition.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout
            || layout.PackingSize != 0 || (layout.Size != 0 && !(layout.Size == 1 && !hasFields)))
            throw CompileError.Unsupported($"explicit layout, .pack or .size on '{type}' ({owner.Name}.dll)", type);
    }
}
