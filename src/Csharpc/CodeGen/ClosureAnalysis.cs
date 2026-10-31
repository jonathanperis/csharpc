using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// One function in a closure tree: the method body itself, or a lambda / local function nested in it.
/// Nested functions take the environment of their creation scope as a hidden first parameter.
/// </summary>
public sealed class ClosureFunction
{
    public required ControlFlowGraph Cfg { get; init; }
    public required IMethodSymbol? Symbol { get; init; }
    public required ClosureFunction? Parent { get; init; }
    /// <summary>Innermost region of <see cref="Parent"/> enclosing this function's creation site.</summary>
    public required ControlFlowRegion? CreationRegion { get; init; }
    public required ClosureAnalysis Analysis { get; init; }
    /// <summary>The environment passed in (innermost env scope enclosing the creation site), or null.</summary>
    public EnvScope? Incoming { get; set; }
    public List<ClosureFunction> Children { get; } = [];
    public bool Enqueued { get; set; }
    /// <summary>
    /// A lambda converted to System.Linq.Expressions.Expression&lt;T&gt; (or nested in one): data, lowered to
    /// factory calls where it is converted (FunctionCodeGen.ExpressionTrees.cs), never to a function.
    /// </summary>
    public bool IsExpressionTree { get; init; }
    /// <summary>Variables of enclosing functions this function (or one nested in it) uses, as Roslyn's scope tree records them.</summary>
    public HashSet<ISymbol> CapturedVariables { get; } = new(SymbolEqualityComparer.Default);
}

/// <summary>
/// A heap-allocated environment ("display class" in Roslyn terms) holding the captured variables
/// declared in one CFG region. Layout: { ptr header, ptr parent, vars... }. A fresh environment is
/// allocated every time control enters the region, which gives C#'s per-iteration semantics for
/// variables declared inside loops (and for foreach iteration variables) for free.
/// </summary>
public sealed class EnvScope
{
    public required ClosureFunction Owner { get; init; }
    public required ControlFlowRegion Region { get; init; }
    public EnvScope? Parent { get; set; }
    public List<ISymbol> Vars { get; } = [];
    public Dictionary<ISymbol, uint> FieldIndex { get; } = new(SymbolEqualityComparer.Default);
    /// <summary>Captured variable types may mention type parameters, so the LLVM layout is built per substitution.</summary>
    public LLVMTypeRef TypeFor(TypeSubstitution subst)
    {
        if (_types.TryGetValue(subst.Key, out var type)) return type;
        var llvm = Module!.Llvm;
        List<LLVMTypeRef> fields = [llvm.Ptr, llvm.Ptr];
        if (ThisField is not null) fields.Add(llvm.Ptr);
        foreach (var v in Vars)
            fields.Add(Module.Types.Storage(subst.Apply(v switch
            {
                ILocalSymbol l => l.Type,
                IParameterSymbol p => p.Type,
                _ => throw new InvalidOperationException(),
            })));
        return _types[subst.Key] = llvm.Struct([.. fields]);
    }

    private readonly Dictionary<string, LLVMTypeRef> _types = [];
    public ModuleCodeGen? Module { get; set; }
    /// <summary>Field holding a captured <c>this</c> (root scope only).</summary>
    public uint? ThisField { get; set; }
    /// <summary>
    /// An expression tree reads variables of this scope: the environment is a .NET display class to it
    /// (Expression.Field over Expression.Constant(env)), so it gets a method table with Roslyn's name.
    /// </summary>
    public bool CapturedByExpressionTree { get; set; }
    public const uint ParentField = 1;
}

/// <summary>
/// Closure conversion analysis for one root CFG (a method body or initializer) and every lambda and
/// local function nested inside it — the same job Roslyn's ClosureConversion lowering does for IL.
/// </summary>
public sealed class ClosureAnalysis
{
    private readonly Dictionary<ControlFlowGraph, ClosureFunction> _byCfg = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IMethodSymbol, ClosureFunction> _bySymbol = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(ClosureFunction, ControlFlowRegion), EnvScope> _scopes = [];
    private readonly Dictionary<ISymbol, EnvScope> _varScope = new(SymbolEqualityComparer.Default);
    private EnvScope? _thisScope;

    public ClosureFunction Root { get; }
    public bool HasCaptures => _scopes.Count > 0;

    public ClosureFunction? FunctionFor(ControlFlowGraph cfg) => _byCfg.GetValueOrDefault(cfg);
    public IEnumerable<EnvScope> Scopes => _scopes.Values;
    public ClosureFunction? FunctionFor(IMethodSymbol symbol) => _bySymbol.GetValueOrDefault(symbol);
    public EnvScope? ScopeOf(ClosureFunction f, ControlFlowRegion region) => _scopes.GetValueOrDefault((f, region));
    public EnvScope? ScopeOfVar(ISymbol symbol) => _varScope.GetValueOrDefault(symbol);
    public EnvScope? ThisScope => _thisScope;

    public ClosureAnalysis(ControlFlowGraph root, IMethodSymbol? rootSymbol, ModuleCodeGen module)
    {
        Root = Register(root, rootSymbol, null, null);
        Discover(Root);

        // Which function owns each local / parameter?
        Dictionary<ILocalSymbol, (ClosureFunction F, ControlFlowRegion R)> localOwner = new(SymbolEqualityComparer.Default);
        foreach (var f in _byCfg.Values)
            foreach (var region in Regions(f.Cfg.Root))
                foreach (var local in region.Locals)
                    localOwner[local] = (f, region);

        // Find captures: references to variables owned by another function.
        foreach (var f in _byCfg.Values)
        {
            foreach (var op in Operations(f.Cfg))
            {
                switch (op)
                {
                    case ILocalReferenceOperation l when localOwner.TryGetValue(l.Local, out var owner) && owner.F != f:
                        Capture(l.Local, owner.F, owner.R, f);
                        break;
                    case IParameterReferenceOperation p when p.Parameter.ContainingSymbol is IMethodSymbol m
                                                             && FunctionFor(m) is { } pf && pf != f:
                        Capture(p.Parameter, pf, pf.Cfg.Root, f);
                        break;
                    // The receiver of a C# 14 extension block belongs to the block, not to a method:
                    // nested functions of an extension member capture it from the member (the root).
                    case IParameterReferenceOperation { Parameter.ContainingSymbol: INamedTypeSymbol { TypeKind: TypeKind.Extension } } p
                        when f != Root:
                        Capture(p.Parameter, Root, Root.Cfg.Root, f);
                        break;
                    case IInstanceReferenceOperation when f != Root:
                    // A primary-constructor parameter used from a nested function is read through `this`.
                    case IParameterReferenceOperation { Parameter.ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } }
                        when f != Root:
                        _thisScope ??= GetScope(Root, Root.Cfg.Root);
                        break;
                }
            }
        }

        // Incoming environments (parents first), then each scope's parent link.
        foreach (var f in TreeOrder(Root))
        {
            f.Incoming = f.Parent is null ? null : InnermostScope(f.Parent, f.CreationRegion);
            foreach (var scope in _scopes.Values.Where(s => s.Owner == f))
                scope.Parent = InnermostScope(f, scope.Region.EnclosingRegion);
        }

        // Field indices (layouts are built lazily per substitution, see EnvScope.TypeFor).
        foreach (var scope in _scopes.Values)
        {
            scope.Module = module;
            uint next = 2;
            if (scope == _thisScope) scope.ThisField = next++;
            foreach (var v in scope.Vars) scope.FieldIndex[v] = next++;
        }
    }

    private ClosureFunction Register(ControlFlowGraph cfg, IMethodSymbol? symbol, ClosureFunction? parent, ControlFlowRegion? creation, bool expressionTree = false)
    {
        var f = new ClosureFunction
        {
            Cfg = cfg, Symbol = symbol, Parent = parent, CreationRegion = creation, Analysis = this,
            IsExpressionTree = expressionTree || parent is { IsExpressionTree: true },
        };
        _byCfg[cfg] = f;
        if (symbol is not null) _bySymbol[symbol] = f;
        parent?.Children.Add(f);
        return f;
    }

    private void Discover(ClosureFunction f)
    {
        foreach (var region in Regions(f.Cfg.Root))
            foreach (var local in region.LocalFunctions.Where(l => !l.IsExtern)) // extern = P/Invoke, no body
                Discover(Register(f.Cfg.GetLocalFunctionControlFlowGraph(local), local, f, region));

        foreach (var block in f.Cfg.Blocks)
        {
            IEnumerable<IOperation> roots = block.BranchValue is { } bv ? [.. block.Operations, bv] : block.Operations;
            foreach (var lambda in roots.SelectMany(r => r.DescendantsAndSelf()).OfType<IFlowAnonymousFunctionOperation>())
                Discover(Register(f.Cfg.GetAnonymousFunctionControlFlowGraph(lambda), lambda.Symbol, f, block.EnclosingRegion, IsExpressionTreeLambda(lambda)));
        }
    }

    /// <summary>A lambda converted to Expression&lt;T&gt; (or, with C# 10's natural type, to Expression / LambdaExpression).</summary>
    internal static bool IsExpressionTreeLambda(IOperation lambda) =>
        lambda.Parent is IDelegateCreationOperation { Type: var d } && IsExpressionType(d)
        || lambda.Parent is IConversionOperation { Type: var c } && IsExpressionType(c);

    internal static bool IsExpressionType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Expression" or "LambdaExpression", ContainingNamespace: { Name: "Expressions", ContainingNamespace: { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } };

    private void Capture(ISymbol variable, ClosureFunction owner, ControlFlowRegion region, ClosureFunction user)
    {
        if (!_varScope.TryGetValue(variable, out var scope))
        {
            scope = GetScope(owner, region);
            scope.Vars.Add(variable);
            _varScope[variable] = scope;
        }
        if (user.IsExpressionTree && !owner.IsExpressionTree) scope.CapturedByExpressionTree = true;
        for (var f = user; f is not null && f != owner; f = f.Parent) f.CapturedVariables.Add(variable);
    }

    /// <summary>Some lambda of this body is converted to an expression tree.</summary>
    public bool HasExpressionTrees => _hasExpressionTrees ??= _byCfg.Values.Any(f => f.IsExpressionTree);
    private bool? _hasExpressionTrees;

    /// <summary>The functions of this body: the root, its lambdas and local functions, nested ones too.</summary>
    public IEnumerable<ClosureFunction> Functions => _byCfg.Values;

    /// <summary>FunctionCodeGen.ExpressionTrees: Roslyn's view of these closures (display classes), computed once.</summary>
    internal object? ExpressionTreeModel { get; set; }

    private EnvScope GetScope(ClosureFunction f, ControlFlowRegion region)
    {
        if (!_scopes.TryGetValue((f, region), out var scope))
            _scopes[(f, region)] = scope = new EnvScope { Owner = f, Region = region };
        return scope;
    }

    private EnvScope? InnermostScope(ClosureFunction f, ControlFlowRegion? region)
    {
        for (var r = region; r is not null; r = r.EnclosingRegion)
            if (_scopes.TryGetValue((f, r), out var scope)) return scope;
        return f.Incoming;
    }

    private static IEnumerable<ClosureFunction> TreeOrder(ClosureFunction f) =>
        f.Children.SelectMany(TreeOrder).Prepend(f);

    public static IEnumerable<ControlFlowRegion> Regions(ControlFlowRegion region) =>
        region.NestedRegions.SelectMany(Regions).Prepend(region);

    private static IEnumerable<IOperation> Operations(ControlFlowGraph cfg) =>
        cfg.Blocks.SelectMany(b => b.BranchValue is { } bv ? [.. b.Operations, bv] : b.Operations)
            .SelectMany(o => o.DescendantsAndSelf());
}
