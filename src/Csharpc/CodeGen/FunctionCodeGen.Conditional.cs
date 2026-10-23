using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// [Conditional("SYMBOL")]: a call to such a method is omitted entirely, arguments included, unless one
/// of its symbols is defined at the call site (C# spec §23.5.3). Roslyn's emitter drops these calls,
/// but its IOperation tree, which csharpc lowers, keeps them. An override is conditional when the
/// method it overrides is; the symbols defined at a call are the compilation's plus the #define/#undef
/// directives of the call's file (C# allows them only before the first token, so they cover the file).
/// </summary>
public sealed partial class FunctionCodeGen
{
    private static readonly Dictionary<SyntaxTree, HashSet<string>> s_fileSymbols = [];

    private static bool IsConditionallyOmitted(IInvocationOperation inv)
    {
        var method = inv.TargetMethod.OriginalDefinition;
        while (method.OverriddenMethod is { } overridden) method = overridden.OriginalDefinition;
        List<string>? conditions = null;
        foreach (var attribute in method.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute"
                && attribute.ConstructorArguments is [{ Value: string symbol }])
                (conditions ??= []).Add(symbol);
        if (conditions is null) return false;
        var defined = FileSymbols(inv.Syntax.SyntaxTree);
        return !conditions.Any(defined.Contains);
    }

    private static HashSet<string> FileSymbols(SyntaxTree tree)
    {
        lock (s_fileSymbols)
        {
            if (s_fileSymbols.TryGetValue(tree, out var cached)) return cached;
            var symbols = new HashSet<string>(tree.Options.PreprocessorSymbolNames, StringComparer.Ordinal);
            // #define/#undef may only precede the first token, so they are all in its leading trivia
            // (the end-of-file token's, for a file without code).
            foreach (var trivia in tree.GetRoot().GetFirstToken(includeZeroWidth: true).LeadingTrivia)
            {
                var directive = trivia.GetStructure();
                if (directive is DefineDirectiveTriviaSyntax define) symbols.Add(define.Name.ValueText);
                else if (directive is UndefDirectiveTriviaSyntax undef) symbols.Remove(undef.Name.ValueText);
            }
            return s_fileSymbols[tree] = symbols;
        }
    }
}
