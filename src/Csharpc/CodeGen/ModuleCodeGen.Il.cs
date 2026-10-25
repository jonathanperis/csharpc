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

    /// <summary>
    /// --il-survey (tools/il-survey): also lower every method of the IL assemblies that needs no type
    /// arguments, so one compile reports everything a package would need, not only what a program reaches.
    /// The failures stay CSLLVM0007 warnings unless the program itself reaches them.
    /// </summary>
    public bool IlSurvey { get; init; }

    /// <summary>--il-survey=A,B: the IL assemblies to survey (empty: all).</summary>
    public IReadOnlyList<string> IlSurveyAssemblies { get; init; } = [];

    /// <summary>--il-survey-shard i/n: a large assembly (Roslyn) is surveyed in several compiles of bounded memory.</summary>
    public (int Index, int Count) IlSurveyShard { get; init; } = (0, 1);

    private void QueueIlSurveyMethods()
    {
        // CSHARPC_CORELIB_SURVEY=1: the precompiled corelib's methods too (what the IL front-end must cover, ADR 0015).
        IEnumerable<INamedTypeSymbol> types = IlTypes;
        if (Environment.GetEnvironmentVariable("CSHARPC_CORELIB_SURVEY") == "1")
        {
            var all = new List<INamedTypeSymbol>();
            void Collect(INamespaceOrTypeSymbol container)
            {
                foreach (var member in container.GetMembers())
                    if (member is INamespaceSymbol ns) Collect(ns);
                    else if (member is INamedTypeSymbol type) { all.Add(type); Collect(type); }
            }
            foreach (var assembly in Il.Assemblies.Where(a => a.IsCoreLib)) Collect(assembly.Symbol.GlobalNamespace);
            types = types.Concat(all);
        }
        foreach (var type in types)
        {
            if (TypeSubstitution.IsGenericContext(type) || type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) continue;
            if (IlSurveyAssemblies.Count > 0 && !IlSurveyAssemblies.Contains(type.ContainingAssembly.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (IlSurveyShard.Count > 1 && StableHash(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) % (uint)IlSurveyShard.Count != IlSurveyShard.Index) continue;
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                // MetadataToken 0: members Roslyn synthesizes with no metadata row (a struct's default constructor).
                if (!method.IsGenericMethod && !method.IsAbstract && !IsExternal(method) && method.MethodKind is not MethodKind.StaticConstructor
                    && method.MetadataToken != 0)
                    Guard(method, () => GetFunction(method));
        }
    }

    /// <summary>FNV-1a: the same shard for a type on every run and platform (string.GetHashCode is randomized).</summary>
    private static uint StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) h = (h ^ c) * 16777619u;
        return h;
    }

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
            // The precompiled corelib's types stay out, as the source corelib's do (ADR 0015).
            foreach (var assembly in Il.Assemblies.Where(a => !a.IsCoreLib)) Collect(assembly.Symbol.GlobalNamespace);
            return _ilTypes = types;
        }
    }

    /// <summary>Source and IL types: the universe for class-hierarchy analysis (devirtualization, generic virtuals).</summary>
    private IEnumerable<INamedTypeSymbol> HierarchyTypes => Il.HasLibraries ? _userTypes.Concat(IlTypes) : _userTypes;

    private readonly Dictionary<IlAssembly, (SyntaxNode Plain, SyntaxNode Base)> _ilSyntax = [];

    /// <summary>
    /// Synthetic operations need a syntax node (diagnostics read its location; `base.M()` is recognized by
    /// its syntax). One tiny tree per assembly, named after the file, so errors point at the DLL.
    /// </summary>
    internal (SyntaxNode Plain, SyntaxNode Base) IlSyntax(IlAssembly assembly)
    {
        if (_ilSyntax.TryGetValue(assembly, out var nodes)) return nodes;
        // The precompiled corelib lives in the cache directory: name it like the source corelib's trees instead.
        var path = assembly.IsCoreLib ? CoreLib.TreePrefix + assembly.Name + ".dll" : assembly.Path;
        var plain = CSharpSyntaxTree.Create(SyntaxFactory.IdentifierName("IL"), path: path).GetRoot();
        var @base = CSharpSyntaxTree.Create(SyntaxFactory.BaseExpression(), path: path).GetRoot();
        return _ilSyntax[assembly] = (plain, @base);
    }

    /// <summary>
    /// Framework methods csharpc deliberately doesn't compile: a boundary that keeps a whole subsystem out
    /// of programs that only reach it in a corner — System.Xml's resolver downloads http(s) resources with
    /// HttpClient (System.Net.Http and the socket, TLS and QUIC stacks under it). Such a method becomes a
    /// stub that ends the program with the message, and a program that can reach it gets a warning.
    /// </summary>
    private static string? IlBoundary(IMethodSymbol method) =>
        (method.ContainingType.ToDisplayString(), method.Name) switch
        {
            ("System.Xml.XmlDownloadManager", "GetNonFileStreamAsync") =>
                "not supported by csharpc yet: System.Xml reading a resource that isn't a file (an http(s) URI needs System.Net.Http)",
            _ => null,
        };

    private readonly List<(IMethodSymbol Method, string Message)> _ilBoundaries = [];

    private void EmitIlMethod(IlAssembly assembly, IMethodSymbol method)
    {
        if (method.IsAbstract || IsExternal(method)) return;
        if (!assembly.IsCoreLib && IlBoundary(method) is { } boundary)
        {
            _ilBoundaries.Add((method, boundary));
            ReplaceWithStub(method, boundary);
            return;
        }
        var location = IlSyntax(assembly).Plain.GetLocation();
        try
        {
            if (method.OriginalDefinition.MetadataToken == 0)
                throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: '{method}' has no metadata definition (synthesized by Roslyn) [in {assembly.Name}.dll]", location);
            var block = assembly.Body(method)
                ?? throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: '{method}' has no IL body (runtime-implemented method in {assembly.Name}.dll)", location);
            IlMethodBody body;
            var decodeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try { body = IlMethodBody.For(assembly, method, block); }
            catch (Exception e) when (e is IlException or BadImageFormatException)
            {
                throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: {e.Message} [in '{method}' ({assembly.Name}.dll)]", location);
            }
            finally { Stats.IlDecodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - decodeStart; }
            if (IlDump is { } dump)
            {
                dump.WriteLine($"=== IL {method.ToDisplayString()}  [{Mangler.Method(method)}]");
                dump.Write(body.Dump());
            }
            new FunctionCodeGen(this, method, _functions[method]).EmitIl(body);
        }
        // The corelib has no unreachable-stub policy: like the source corelib, what it reaches must compile.
        catch (CompileError) when (assembly.IsCoreLib) { throw; }
        catch (CompileError e) { IlMethodFailed(method, e); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (s_trace) Console.Error.WriteLine(e);
            if (assembly.IsCoreLib) throw;
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
    internal void NoteReference(IMethodSymbol method, bool virtualCall) => _references.Add((_currentCaller, method, virtualCall));

    /// <summary>A virtual or interface call site: every override / implementation of the member may run.</summary>
    internal void NoteVirtualCall(IMethodSymbol target) => NoteReference(target, virtualCall: true);

    /// <summary>A check that runs <paramref name="type"/>'s static constructor.</summary>
    internal void NoteClassInit(INamedTypeSymbol type)
    {
        if (type.StaticConstructors.FirstOrDefault() is { } cctor) NoteReference(cctor, virtualCall: false);
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
        catch (CompileError e) when (IsIlMethod(method) && !Il.IsCoreLib(method))
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
        ReplaceWithStub(method, error);
    }

    /// <summary>A method of the program's sources that can't be compiled: a stub, an error if reachable (ReportIlFailures).</summary>
    private void SourceMethodFailed(IMethodSymbol method, CompileError error)
    {
        if (error.Location is not { IsInSource: true } && method.Locations.FirstOrDefault() is { IsInSource: true } at)
            error = new CompileError(error.Id, error.Message, at);
        ReplaceWithStub(method, error);
    }

    /// <summary>The program's own source methods (not the corelib, not IL): whether they run is decided by reachability.</summary>
    private bool IsProgramSourceMethod(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, Compilation.Assembly) && !CoreLib.IsCoreLib(method);

    private void ReplaceWithStub(IMethodSymbol method, CompileError error)
    {
        _ilFailures.Add((method, error));
        ReplaceWithStub(method, $"'{method}' could not be compiled: {error.Message}");
    }

    private void ReplaceWithStub(IMethodSymbol method, string message)
    {
        var old = _functions[method];
        var name = Native.TakeName(old.Fn);
        var stub = UnsupportedStub(name + "$unsupported", old.Type, message);
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
        if (_ilFailures.Count == 0 && _ilBoundaries.Count == 0) return;
        var edges = _references.GroupBy(r => (ISymbol?)r.From?.OriginalDefinition, SymbolEqualityComparer.Default)
            .ToDictionary(g => g.Key ?? Compilation.Assembly, g => g.ToList(), SymbolEqualityComparer.Default);
        var reached = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        // CSHARPC_WHY=1: how the program reaches each failing method (the first reference chain found).
        var why = Environment.GetEnvironmentVariable("CSHARPC_WHY") == "1"
            ? new Dictionary<ISymbol, IMethodSymbol?>(SymbolEqualityComparer.Default) : null;
        var work = new Queue<IMethodSymbol>();
        void Reach(IMethodSymbol m, IMethodSymbol? from)
        {
            if (!reached.Add(m.OriginalDefinition)) return;
            work.Enqueue(m);
            if (why != null) why[m.OriginalDefinition] = from?.OriginalDefinition;
        }
        void Follow(IMethodSymbol? from, IMethodSymbol to, bool isVirtual)
        {
            Reach(to, from);
            if (isVirtual) foreach (var implementation in PossibleTargets(to)) Reach(implementation, from);
        }
        // Roots: the entry point and what .NET itself calls in the program (overrides and implementations of
        // library members: ToString, Finalize, IComparable...; static constructors, module initializers and
        // native exports), and references made outside the program's methods: by the corelib, by the code
        // csharpc generates (method tables, type metadata, main), and by lambdas and local functions
        // (their bodies are reached through closures the graph doesn't follow).
        if (Compilation.GetEntryPoint(default) is { } entry) Reach(entry, null);
        foreach (var root in RuntimeCalledMethods()) Reach(root, null);
        foreach (var (from, to, isVirtual) in _references)
            if (from is null || IsNestedFunction(from) || (IsIlMethod(from) ? Il.IsCoreLib(from) : !IsProgramSourceMethod(from))) Follow(from, to, isVirtual);
        while (work.TryDequeue(out var method))
            if (edges.TryGetValue(method.OriginalDefinition, out var outgoing))
                foreach (var (_, to, isVirtual) in outgoing) Follow(method, to, isVirtual);

        foreach (var (method, message) in _ilBoundaries)
            if (reached.Contains(method.OriginalDefinition))
                Warnings.Add(new CompileError("CSLLVM0008", $"{message} — '{method}' is compiled as a stub that ends the program with this message if it is called",
                    Il.OwnerOf(method) is { } owner ? IlSyntax(owner).Plain.GetLocation() : null));

        // A construct that also fails elsewhere (a type's layout, a field) is reported there already.
        var reported = Errors.Select(e => (e.Message, e.Location?.ToString())).ToHashSet();
        foreach (var (method, error) in _ilFailures)
        {
            if (why != null && reached.Contains(method.OriginalDefinition))
            {
                Console.Error.WriteLine($"why {method.OriginalDefinition}:");
                ISymbol? step = method.OriginalDefinition;
                for (int depth = 0; step != null && depth < 40 && why.TryGetValue(step, out var parent); depth++, step = parent)
                    Console.Error.WriteLine($"    <- {(parent == null ? "(program)" : parent.ToString())}");
            }
            if (reached.Contains(method.OriginalDefinition)) Errors.Add(error);
            else if (!reported.Contains((error.Message, error.Location?.ToString()))) Warnings.Add(new CompileError("CSLLVM0007",
                $"{error.Message} — the program does not reach this method (class-hierarchy analysis), so it was compiled as a stub that ends the program if it is ever called",
                error.Location));
        }
    }

    /// <summary>
    /// Source methods the runtime or the library may call without a call site in the program: overrides of
    /// library virtuals, implementations of library interfaces, static constructors, module initializers
    /// and [UnmanagedCallersOnly] exports.
    /// </summary>
    private IEnumerable<IMethodSymbol> RuntimeCalledMethods()
    {
        foreach (var type in _userTypes)
        {
            if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, Compilation.Assembly) || CoreLib.IsCoreLib(type)) continue;
            foreach (var m in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (m.MethodKind == MethodKind.StaticConstructor
                    || (m.IsOverride && !IsUserMethod(ClassLayout.RootOf(m)) && !IsUndispatchedLibraryVirtual(ClassLayout.RootOf(m)))
                    || m.ExplicitInterfaceImplementations.Any(i => !IsUserType(i.ContainingType))
                    || m.GetAttributes().Any(a => a.AttributeClass?.Name is "ModuleInitializerAttribute" or "UnmanagedCallersOnlyAttribute"))
                    yield return m;
            }
            foreach (var itf in type.AllInterfaces)
            {
                if (IsUserType(itf)) continue;
                foreach (var member in itf.GetMembers().OfType<IMethodSymbol>())
                    if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation
                        && SymbolEqualityComparer.Default.Equals(implementation.ContainingAssembly, Compilation.Assembly))
                        yield return implementation;
            }
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
                    {
                        yield return implementation;
                        // The implementing method may be a virtual of a base class that `type` (or a class
                        // derived from it) overrides: the interface call dispatches to the override.
                        if (implementation.IsVirtual || implementation.IsAbstract || implementation.IsOverride)
                            foreach (var overrider in ClassOverrides(implementation)) yield return overrider;
                    }
                }
            yield break;
        }
        foreach (var overrider in ClassOverrides(definition)) yield return overrider;
    }

    /// <summary>
    /// Every method of the hierarchy that fills <paramref name="definition"/>'s vtable slot. Roots are compared
    /// through the corelib's shadows: the corelib's StreamReader calls StreamImpl.Read, which DeflateStream (IL)
    /// overrides as System.IO.Stream.Read.
    /// </summary>
    private List<IMethodSymbol> ClassOverrides(IMethodSymbol definition)
    {
        var root = CoreLib.Map(ClassLayout.RootOf(definition.OriginalDefinition));
        if (_classOverrides.TryGetValue(root, out var known)) return known;
        var found = new List<IMethodSymbol>();
        foreach (var type in HierarchyTypes)
            foreach (var m in type.GetMembers(root.Name).OfType<IMethodSymbol>())
                if ((m.IsVirtual || m.IsOverride) && SymbolEqualityComparer.Default.Equals(CoreLib.Map(ClassLayout.RootOf(m)), root))
                    found.Add(m);
        return _classOverrides[root] = found;
    }

    private readonly Dictionary<IMethodSymbol, List<IMethodSymbol>> _classOverrides = new(SymbolEqualityComparer.Default);

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
