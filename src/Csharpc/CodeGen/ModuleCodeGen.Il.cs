using System.Reflection;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    internal Dictionary<IFieldSymbol, (Llvm.LLVMValueRef Global, int Size)> IlRvaGlobals { get; } = new(SymbolEqualityComparer.Default);

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
        if (method.MethodKind == MethodKind.StaticConstructor && TypeSubstitution.IsGenericContext(method))
            throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: static constructors in generic types ('{method.ContainingType}', {assembly.Name}.dll)", null);
        var block = assembly.Body(method)
            ?? throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: '{method}' has no IL body (runtime-implemented method in {assembly.Name}.dll)", null);
        IlMethodBody body;
        try { body = IlMethodBody.Decode(assembly, method, block); }
        catch (Exception e) when (e is IlException or BadImageFormatException)
        {
            throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: {e.Message} (in '{method}', {assembly.Name}.dll)", null);
        }
        if (IlDump is { } dump)
        {
            dump.WriteLine($"=== IL {method.ToDisplayString()}  [{Mangler.Method(method)}]");
            dump.Write(body.Dump());
        }
        new FunctionCodeGen(this, method, GetFunction(method)).EmitIl(body);
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
