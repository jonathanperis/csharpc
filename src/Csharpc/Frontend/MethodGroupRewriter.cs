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
                    .WithTriviaFrom(node);
                replacements[node] = lambda;
            }
            if (replacements.Count == 0) continue;
            var newRoot = root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);
            result = result.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(newRoot, tree.Options));
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
