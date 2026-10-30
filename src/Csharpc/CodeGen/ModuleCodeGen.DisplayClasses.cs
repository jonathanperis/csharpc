using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.CodeGen;

/// <summary>
/// Closure environments that expression trees read (FunctionCodeGen.ExpressionTrees.cs) as the display
/// classes the C# compiler generates: a method table named `Outer+&lt;&gt;c__DisplayClass{method}_{closure}`,
/// a Type entry (a sealed nested private class deriving from Object), and FieldInfo records over the
/// captured variables' slots. Only those environments get one; the others keep csrt_Object_MT.
/// </summary>
public sealed partial class ModuleCodeGen
{
    internal sealed record DisplayClassType(string Name, INamedTypeSymbol Owner);

    /// <summary>
    /// The program's own C# build is optimized (Release, or Optimize=true): Roslyn then merges closure
    /// environments, which changes the display classes expression trees show.
    /// </summary>
    public bool SourceOptimized { get; init; }

    private readonly Dictionary<EnvScope, LLVMValueRef> _displayClasses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(EnvScope, ISymbol), LLVMValueRef> _envFieldRecords = [];
    private readonly Dictionary<(EnvScope, EnvScope), LLVMValueRef> _envParentRecords = [];

    internal LLVMValueRef DisplayClassMethodTable(EnvScope scope, string name, INamedTypeSymbol owner, ulong size)
    {
        if (_displayClasses.TryGetValue(scope, out var mt)) return mt;
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(name), Llvm.ConstInt(Llvm.I32, (long)size), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, [.. defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn)]));
        mt = Module.AddGlobal("csrt.displayclass." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        RegisterTypeMethodTable(mt, new DisplayClassType(name, owner));
        return _displayClasses[scope] = mt;
    }

    /// <summary>The FieldInfo record of a captured variable: a public field of the display class, as Roslyn hoists it.</summary>
    internal LLVMValueRef EnvFieldRecordGlobal(EnvScope scope, ISymbol variable, LLVMValueRef displayClass, TypeSubstitution subst)
    {
        if (_envFieldRecords.TryGetValue((scope, variable), out var existing)) return existing;
        var envType = scope.TypeFor(subst);
        var index = scope.FieldIndex[variable];
        var type = subst.Apply(variable switch { ILocalSymbol l => l.Type, IParameterSymbol p => p.Type, _ => throw new InvalidOperationException() });
        var name = Mangler.TypeSymbol(scope.Owner.Analysis.Root.Symbol!.ContainingType, "envfield") + "." + variable.Name;
        var getter = SyntheticFunction(name + ".get", Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr),
            (g, syntax) => g.EmitEnvFieldGetter(envType, index, type, syntax));
        var setter = SyntheticFunction(name + ".set", Llvm.FunctionType(Llvm.Void, Llvm.Ptr, Llvm.Ptr),
            (g, syntax) => g.EmitEnvFieldSetter(envType, index, type, syntax));
        var global = Module.AddGlobal("csharpc.reflection.field", FieldRecordType, constant: true, linkage: LLVMLinkage.Private);
        var nameString = CString(variable.Name);
        var fieldType = MetadataMethodTable(type);
        _fieldRecordInitializers.Add((global, () => FieldRecordConstant(new FieldRecordData(nameString, displayClass, fieldType, getter, setter, [],
            (uint)System.Reflection.FieldAttributes.Public))));
        return _envFieldRecords[(scope, variable)] = global;
    }

    /// <summary>
    /// The FieldInfo record of a frame's link to its parent frame (`CS$&lt;&gt;8__locals1`): csharpc's environments
    /// link every scope to its enclosing one, so the getter follows as many links as lie between the two.
    /// </summary>
    internal LLVMValueRef EnvParentFieldRecordGlobal(EnvScope from, EnvScope to, string name, LLVMValueRef fromMt, LLVMValueRef toMt, TypeSubstitution subst)
    {
        if (_envParentRecords.TryGetValue((from, to), out var existing)) return existing;
        var hops = new List<LLVMTypeRef>();
        for (var s = from; s != to; s = s.Parent ?? throw CompileError.Unsupported("an expression tree reading a closure scope that doesn't enclose it", (ISymbol?)null!))
            hops.Add(s.TypeFor(subst));
        var symbol = Mangler.TypeSymbol(from.Owner.Analysis.Root.Symbol!.ContainingType, "envlink") + "." + name;
        var getter = SyntheticFunction(symbol + ".get", Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr), (g, _) => g.EmitEnvParentGetter(hops));
        var global = Module.AddGlobal("csharpc.reflection.field", FieldRecordType, constant: true, linkage: LLVMLinkage.Private);
        var nameString = CString(name);
        _fieldRecordInitializers.Add((global, () => FieldRecordConstant(new FieldRecordData(nameString, fromMt, toMt, getter, LlvmContext.Null(Llvm.Ptr), [],
            (uint)System.Reflection.FieldAttributes.Public))));
        return _envParentRecords[(from, to)] = global;
    }

    private TypeInfoRecord DisplayClassRecord(LLVMValueRef mt, DisplayClassType displayClass)
    {
        var none = LlvmContext.Null(Llvm.Ptr);
        LLVMValueRef Str(string? s) => s is null ? none : CString(s);
        return new TypeInfoRecord
        {
            Fields = [mt, MetadataMethodTable(Compilation.GetSpecialType(SpecialType.System_Object)), none, none, MetadataMethodTable(displayClass.Owner.OriginalDefinition), none],
            Namespace = Str(NamespaceOf(displayClass.Owner)), Assembly = Str(AssemblyOf(displayClass.Owner)),
            Flags = TI.Class | TI.Sealed | TI.Nested | TI.NestedPrivate | TI.TypeDefinition | TI.ReferenceType,
            ArgumentCount = 0, Rank = 0, Variance = 0,
        };
    }
}

public sealed partial class FunctionCodeGen
{
    internal void EmitEnvFieldGetter(LLVMTypeRef envType, uint index, ITypeSymbol type, SyntaxNode syntax)
    {
        var at = new IlValueOperation(syntax, ObjectType, () => default);
        var value = LoadValue(_b.StructGep(envType, ParamOf(0), index), type);
        _b.Ret(ToReflectionResult(value, type, at));
        Finish();
    }

    internal void EmitEnvParentGetter(List<LLVMTypeRef> hops)
    {
        var env = ParamOf(0);
        foreach (var type in hops) env = _b.Load(L.Ptr, _b.StructGep(type, env, EnvScope.ParentField));
        _b.Ret(env);
        Finish();
    }

    internal void EmitEnvFieldSetter(LLVMTypeRef envType, uint index, ITypeSymbol type, SyntaxNode syntax)
    {
        var value = FromReflectionArgument(ParamOf(1), type);
        StoreValue(ToStorage(value, type), _b.StructGep(envType, ParamOf(0), index));
        _b.RetVoid();
        Finish();
    }
}

/// <summary>
/// Roslyn's closure environments for one method body (ClosureConversion.Analysis), as far as expression
/// trees can see them: which display class each environment is (`&lt;&gt;c__DisplayClass{m}_{c}`, m the
/// method's index among its type's members, c the environment's index in pre-order of the scope tree,
/// GetClosureId), and which environments link to their parent (`CS$&lt;&gt;8__locals{n}`, numbered in the
/// order the frames are introduced). A tree reads a captured variable as Roslyn's FramePointer resolves
/// it: through the parent links of the frames nested inside the variable's frame.
///
/// Roslyn's scopes are syntax (the method body with its parameters, blocks, for / foreach / using
/// statements, lambda and local function bodies); csharpc's are CFG regions, so one Roslyn environment
/// may be two csharpc environments (a method's parameters and its body's locals) with the same name.
/// Invalid (with the reason) where csharpc can't tell for sure: generic display classes, release-mode
/// environment merging of several scopes, struct environments of local functions, variables declared
/// in other constructs. A tree that needs an invalid model is a not-supported error, never a guess.
/// </summary>
internal sealed class RoslynClosureModel
{
    internal sealed class Env
    {
        public required SyntaxNode Node;
        public int Ordinal;
        public string Name = "";
        public Env? Parent;
        public int ParentFieldId;
        public List<EnvScope> Scopes = [];

        /// <summary>The innermost csharpc environment of this one (the body's locals rather than the parameters).</summary>
        public EnvScope Representative => Scopes.MaxBy(Depth)!;

        private static int Depth(EnvScope s)
        {
            var depth = 0;
            for (var p = s.Parent; p is not null; p = p.Parent) depth++;
            return depth;
        }
    }

    public string? Invalid { get; private set; }
    public INamedTypeSymbol? Owner { get; }
    private readonly Dictionary<EnvScope, Env> _envOf = new(ReferenceEqualityComparer.Instance);
    private readonly List<Env> _envs = [];

    public static RoslynClosureModel For(ClosureAnalysis analysis, ModuleCodeGen module) =>
        (RoslynClosureModel)(analysis.ExpressionTreeModel ??= new RoslynClosureModel(analysis, module));

    public Env? EnvOf(EnvScope scope) => _envOf.GetValueOrDefault(scope);

    private RoslynClosureModel(ClosureAnalysis analysis, ModuleCodeGen module)
    {
        if (analysis.Root.Symbol is not { MethodKind: not (MethodKind.AnonymousFunction or MethodKind.LocalFunction) } root || root.ContainingType is null)
        {
            Invalid = "a closure outside a method";
            return;
        }
        Owner = root.ContainingType;
        for (var t = root.ContainingType; t is not null; t = t.ContainingType)
            if (t.IsGenericType) { Invalid = "generic display classes"; return; }
        if (root.IsGenericMethod) { Invalid = "generic display classes"; return; }
        var methodOrdinal = root.ContainingType.GetMembers().IndexOf(root.OriginalDefinition, SymbolEqualityComparer.Default);
        if (methodOrdinal < 0) { Invalid = "a closure in a member csharpc can't number"; return; }
        if (analysis.Functions.Any(f => f.Symbol is { MethodKind: MethodKind.LocalFunction } && f.CapturedVariables.Count > 0))
        {
            Invalid = "local functions that capture variables (struct display classes)";
            return;
        }

        var byNode = new Dictionary<SyntaxNode, Env>();
        foreach (var scope in analysis.Scopes)
        {
            if (scope.Vars.Count == 0 || scope.Owner.IsExpressionTree) continue;
            if (ScopeNode(scope, root) is not { } node)
            {
                Invalid = $"variables declared where csharpc can't place Roslyn's scope ({string.Join(", ", scope.Vars.Select(v => v.Name))})";
                return;
            }
            if (!byNode.TryGetValue(node, out var env)) byNode[node] = env = new Env { Node = node };
            env.Scopes.Add(scope);
            _envOf[scope] = env;
        }
        _envs.AddRange(byNode.Values);
        _envs.Sort((a, b) => a.Node.SpanStart != b.Node.SpanStart ? a.Node.SpanStart.CompareTo(b.Node.SpanStart) : b.Node.Span.Length.CompareTo(a.Node.Span.Length));
        // Release builds merge the environments of scopes that aren't in loops: only one scope is certain.
        if (_envs.Count > 1 && module.SourceOptimized)
        {
            Invalid = "several closure scopes in a release build";
            return;
        }
        var owner = ModuleCodeGen.ClrName(root.ContainingType);
        for (var i = 0; i < _envs.Count; i++)
        {
            _envs[i].Ordinal = i;
            _envs[i].Name = owner + "+<>c__DisplayClass" + methodOrdinal + "_" + i;
        }

        // ComputeLambdaScopesAndFrameCaptures: a function lives in the nearest environment it captures; every
        // environment between that one and the outermost it captures links to its parent.
        foreach (var f in analysis.Functions)
        {
            if (f == analysis.Root || f.Symbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } syntax) continue;
            var captured = new HashSet<Env>(ReferenceEqualityComparer.Instance);
            foreach (var v in f.CapturedVariables)
                if (analysis.ScopeOfVar(v) is { } s && EnvOf(s) is { } e) captured.Add(e);
            if (captured.Count == 0) continue;
            Env? old = null;
            foreach (var env in Enclosing(syntax))
            {
                if (captured.Count == 0) break;
                if (old is null)
                {
                    if (captured.Remove(env)) old = env;
                    continue;
                }
                if (old.Parent is not null && old.Parent != env) { Invalid = "an unexpected closure parent"; return; }
                old.Parent = env;
                old = env;
                captured.Remove(env);
            }
            if (captured.Count > 0) { Invalid = "a closure capturing a scope csharpc can't place"; return; }
        }
        var id = 1;
        foreach (var env in _envs)
            if (env.Parent is not null) env.ParentFieldId = id++;
    }

    /// <summary>The environments whose scope contains <paramref name="syntax"/> (not its own body), innermost first.</summary>
    public IEnumerable<Env> Enclosing(SyntaxNode syntax) =>
        _envs.Where(e => e.Node != syntax && e.Node.Span.Contains(syntax.Span)).OrderBy(e => e.Node.Span.Length);

    private static SyntaxNode? ScopeNode(EnvScope scope, IMethodSymbol root)
    {
        SyntaxNode? node = null;
        foreach (var v in scope.Vars)
        {
            var n = VariableScope(v, root);
            if (n is null || node is not null && n != node) return null;
            node = n;
        }
        return node;
    }

    private static SyntaxNode? VariableScope(ISymbol variable, IMethodSymbol root)
    {
        switch (variable)
        {
            case IParameterSymbol { ContainingSymbol: IMethodSymbol owner }:
                return FunctionNode(owner, root);
            case ILocalSymbol local when local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is { } declaration:
                switch (declaration)
                {
                    case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: var holder } }:
                        return holder switch
                        {
                            LocalDeclarationStatementSyntax statement => BlockScope(statement.Parent, root),
                            ForStatementSyntax or UsingStatementSyntax => holder,
                            _ => null,
                        };
                    case ForEachStatementSyntax forEach:
                        return forEach;
                    default:
                        return null;
                }
            default:
                return null;
        }
    }

    /// <summary>The scope of a statement's locals: its block, or the function whose body that block is.</summary>
    private static SyntaxNode? BlockScope(SyntaxNode? parent, IMethodSymbol root) => parent switch
    {
        GlobalStatementSyntax => RootNode(root),
        BlockSyntax { Parent: BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax } body =>
            body.Parent is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax ? body.Parent : RootNode(root),
        BlockSyntax block => block,
        _ => null,
    };

    private static SyntaxNode? FunctionNode(IMethodSymbol owner, IMethodSymbol root) =>
        SymbolEqualityComparer.Default.Equals(owner.OriginalDefinition, root.OriginalDefinition) ? RootNode(root)
        : owner.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction ? owner.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
        : null;

    private static SyntaxNode? RootNode(IMethodSymbol root) => root.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
}
