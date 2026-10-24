using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.Frontend;

/// <summary>
/// Rewrites method groups that bind to *library* static methods (`s.Where(char.IsLetterOrDigit)`,
/// `xs.Select(Math.Sqrt)`, `values.Count(T.IsZero)`) into equivalent lambdas
/// (`(a0) => char.IsLetterOrDigit(a0)`) in the compilation csharpc lowers. Library methods are
/// implemented as intrinsics / corelib redirects keyed on invocations, so a lambda gets them for free.
/// For static methods the rewrite is semantically identical.
/// </summary>
public static class MethodGroupRewriter
{
    /// <summary>Marks the lambdas this rewrite synthesizes: they are not part of the program (no stack frame in .NET).</summary>
    public static readonly SyntaxAnnotation Synthesized = new("csharpc.method-group");

    /// <summary>Rewritten tree -> the tree it was made from, and what changed (original full span, new length), in order.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxTree, Origin> s_origins = new();

    private sealed record Origin(SyntaxTree Tree, List<(Microsoft.CodeAnalysis.Text.TextSpan Span, int NewLength)> Replacements);

    /// <summary>
    /// Maps a span of a (possibly) rewritten tree back to the tree the user wrote: stack traces look
    /// positions up in the PDB of the original program (CodeGen/StackTraces.cs).
    /// </summary>
    public static (SyntaxTree Tree, Microsoft.CodeAnalysis.Text.TextSpan Span) Original(SyntaxTree tree, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        if (!s_origins.TryGetValue(tree, out var origin)) return (tree, span);
        var start = OriginalPosition(origin, span.Start);
        var end = Math.Max(start, OriginalPosition(origin, span.End));
        return (origin.Tree, Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(start, end));
    }

    private static int OriginalPosition(Origin origin, int position)
    {
        var delta = 0;
        foreach (var (span, newLength) in origin.Replacements)
        {
            var newStart = span.Start + delta;
            if (position < newStart) break;
            if (position < newStart + newLength) return span.Start; // inside a synthesized lambda
            delta += newLength - span.Length;
        }
        return position - delta;
    }

    public static CSharpCompilation Rewrite(CSharpCompilation compilation)
    {
        var result = compilation;
        foreach (var tree in compilation.SyntaxTrees)
        {
            // The corelib writes lambdas where it needs them; binding all of it here would cost
            // ~1-2 s per compile (GetOperation forces method-body binding).
            if (RoslynFrontend.IsCoreLibTree(tree)) continue;
            SemanticModel? model = null;
            var root = tree.GetRoot();
            var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
            foreach (var node in root.DescendantNodes().OfType<ExpressionSyntax>())
            {
                if (node is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or GenericNameSyntax)) continue;
                if (!CanBeMethodGroupConversion(node)) continue;
                model ??= compilation.GetSemanticModel(tree);
                if (model.GetOperation(node) is not IMethodReferenceOperation { Method: { IsStatic: true } method } reference) continue;
                if (reference.Parent is not IDelegateCreationOperation) continue;
                if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly)) continue;
                if (method.Parameters.Any(p => p.RefKind != RefKind.None) || method.IsVararg) continue;
                if (node.Ancestors().Any(a => replacements.ContainsKey(a))) continue;
                var parameters = string.Join(", ", method.Parameters.Select((_, i) => $"__mg{i}"));
                var lambda = SyntaxFactory.ParseExpression($"({parameters}) => {node.WithoutTrivia()}({parameters})")
                    .WithTriviaFrom(node).WithAdditionalAnnotations(Synthesized);
                replacements[node] = lambda;
            }
            if (replacements.Count == 0) continue;
            var newRoot = root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);
            var newTree = tree.WithRootAndOptions(newRoot, tree.Options);
            s_origins.AddOrUpdate(newTree, new Origin(tree,
                [.. replacements.OrderBy(r => r.Key.FullSpan.Start).Select(r => (r.Key.FullSpan, r.Value.FullSpan.Length))]));
            result = result.ReplaceSyntaxTree(tree, newTree);
        }
        return result;
    }

    /// <summary>Syntactic positions where a method group can be converted to a delegate.</summary>
    private static bool CanBeMethodGroupConversion(ExpressionSyntax node)
    {
        // `a.b` as the receiver of a member access or the target of a call is not a method group value.
        var parent = node.Parent;
        if (parent is MemberAccessExpressionSyntax access && access.Expression == node) return false;
        return parent switch
        {
            ArgumentSyntax or EqualsValueClauseSyntax or ReturnStatementSyntax or ArrowExpressionClauseSyntax
                or CastExpressionSyntax or YieldStatementSyntax => true,
            AssignmentExpressionSyntax assignment => assignment.Right == node,
            ConditionalExpressionSyntax or CollectionElementSyntax or InitializerExpressionSyntax => true,
            _ => false,
        };
    }
}
