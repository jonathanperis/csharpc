using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.CodeGen;

public enum CoroutineKind { None, Async, Iterator }

/// <summary>
/// Classification helpers for async methods and iterators, which csharpc lowers to LLVM
/// coroutines (docs/10-async.md). Roslyn's IOperation/CFG keep `await` and `yield return` as
/// operations; the state machine is built by LLVM's CoroSplit pass, not by us.
/// </summary>
public static class Coroutines
{
    public static CoroutineKind KindOf(IMethodSymbol method)
    {
        if (method.IsAsync) return CoroutineKind.Async;
        if (IsEnumerableLike(method.ReturnType) && HasYield(method)) return CoroutineKind.Iterator;
        return CoroutineKind.None;
    }

    private static bool IsEnumerableLike(ITypeSymbol type) =>
        type.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T
            or SpecialType.System_Collections_Generic_IEnumerator_T
            or SpecialType.System_Collections_IEnumerable or SpecialType.System_Collections_IEnumerator;

    private static bool HasYield(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Any(r =>
        {
            var root = r.GetSyntax();
            return root.DescendantNodes(n => n == root || n is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
                .OfType<YieldStatementSyntax>().Any();
        });

    /// <summary>IEnumerable&lt;T&gt; / IEnumerator&lt;T&gt; -> T; non-generic -> object.</summary>
    public static ITypeSymbol IteratorElementType(IMethodSymbol method, Compilation compilation) =>
        method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } generic
            ? generic.TypeArguments[0]
            : compilation.GetSpecialType(SpecialType.System_Object);

    /// <summary>Task&lt;T&gt; / ValueTask&lt;T&gt; -> T; Task / ValueTask / void -> null.</summary>
    public static ITypeSymbol? AsyncResultType(ITypeSymbol returnType) =>
        IsTaskLike(returnType) && returnType is INamedTypeSymbol { TypeArguments.Length: 1 } generic ? generic.TypeArguments[0] : null;

    public static bool IsTaskLike(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Task" or "ValueTask" } named && Namespace(named) == "System.Threading.Tasks";

    /// <summary>
    /// The task-shaped library types (tasks, awaiters, configured awaitables). All of them are
    /// represented by a pointer to the csrt task object — awaiters are just the task again.
    /// </summary>
    public static bool IsTaskHandleType(ITypeSymbol? type) => type is INamedTypeSymbol named && (Namespace(named), named.Name) switch
    {
        ("System.Threading.Tasks", "Task" or "ValueTask") => true,
        ("System.Runtime.CompilerServices", "TaskAwaiter" or "ValueTaskAwaiter" or "ConfiguredTaskAwaitable" or "ConfiguredTaskAwaiter"
            or "ConfiguredValueTaskAwaitable" or "ConfiguredValueTaskAwaiter" or "YieldAwaitable" or "YieldAwaiter") => true,
        _ => false,
    };

    public static bool IsYieldAwaitable(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "YieldAwaitable" } named && Namespace(named) == "System.Runtime.CompilerServices";

    private static string Namespace(INamedTypeSymbol type) => type.ContainingNamespace?.ToDisplayString() ?? "";
}
