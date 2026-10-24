using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Building a method's ControlFlowGraph (Roslyn binds the body on demand) is most of the time spent
/// lowering it. A method is lowered when the worklist reaches it, so its graph is built ahead, on the
/// thread pool, as soon as the method is queued; lowering itself stays on this thread, in the same order
/// (the output doesn't depend on timing). Roslyn's semantic models are safe to query concurrently.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<SyntaxNode, Task<ControlFlowGraph?>> _prefetchedCfgs = [];

    private static readonly bool s_prefetchCfgs =
        Environment.ProcessorCount > 1 && Environment.GetEnvironmentVariable("CSHARPC_SERIAL_CFG") != "1";

    /// <summary>The syntax EmitMethodCore builds the method's graph from, if it builds one from a method body.</summary>
    private static SyntaxNode? CfgSyntax(IMethodSymbol method)
    {
        if (method.IsAbstract || method.IsExtern || method.IsImplicitlyDeclared) return null;
        var definition = method.OriginalDefinition;
        var syntax = (definition.PartialImplementationPart ?? definition).DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return syntax switch
        {
            AccessorDeclarationSyntax { Body: null, ExpressionBody: null } => null,
            MethodDeclarationSyntax or ConstructorDeclarationSyntax or AccessorDeclarationSyntax or ArrowExpressionClauseSyntax
                or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => syntax,
            _ => null,
        };
    }

    private void PrefetchCfg(IMethodSymbol method)
    {
        if (!s_prefetchCfgs || CfgSyntax(method) is not { } syntax || _prefetchedCfgs.ContainsKey(syntax)) return;
        var model = ModelFor(syntax.SyntaxTree);
        // A failure is reported when this thread builds the graph again (TakePrefetchedCfg returns null).
        _prefetchedCfgs[syntax] = Task.Run(() => { try { return ControlFlowGraph.Create(syntax, model); } catch { return null; } });
    }

    /// <summary>The graph built ahead for this syntax, once (a generic method's next instantiation builds its own).</summary>
    private ControlFlowGraph? TakePrefetchedCfg(SyntaxNode syntax) =>
        _prefetchedCfgs.Remove(syntax, out var task) ? task.Result : null;
}
