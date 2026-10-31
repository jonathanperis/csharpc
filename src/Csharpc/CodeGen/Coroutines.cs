using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.CodeGen;

public enum CoroutineKind { None, Async, Iterator, AsyncIterator }

/// <summary>
/// Classification helpers for async methods and iterators, which csharpc lowers to LLVM
/// coroutines (docs/10-async.md). Roslyn's IOperation/CFG keep `await` and `yield return` as
/// operations; the state machine is built by LLVM's CoroSplit pass, not by us.
/// </summary>
public static class Coroutines
{
    public static CoroutineKind KindOf(IMethodSymbol method)
    {
        if (method.IsAsync)
            return method.ReturnType.OriginalDefinition is INamedTypeSymbol { Name: "IAsyncEnumerable" or "IAsyncEnumerator" }
                ? CoroutineKind.AsyncIterator
                : CoroutineKind.Async;
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
        type is INamedTypeSymbol { Name: "Task" or "ValueTask" } named && Namespace(named) == "System.Threading.Tasks"
        // ValueTask<T> after corelib mapping (corelib/Tasks/ValueTaskOfT.cs)
        || type is INamedTypeSymbol { Name: "ValueTaskImpl", Arity: 1 } shadow && Namespace(shadow) == CoreLib.Namespace;

    /// <summary>
    /// The task-shaped library types (tasks, awaiters, configured awaitables). All of them are
    /// represented by a pointer to the csrt task object — awaiters are just the task again.
    /// </summary>
    public static bool IsTaskHandleType(ITypeSymbol? type) => type is INamedTypeSymbol named && !IsValueTaskOfT(named) && (Namespace(named), named.Name) switch
    {
        ("System.Threading.Tasks", "Task" or "ValueTask") => true,
        ("System.Threading", "CancellationToken") => true, // ignored by the single-threaded scheduler; default == null
        ("System.Runtime.CompilerServices", "TaskAwaiter" or "ValueTaskAwaiter" or "ConfiguredTaskAwaitable" or "ConfiguredTaskAwaiter"
            or "ConfiguredValueTaskAwaitable" or "ConfiguredValueTaskAwaiter" or "YieldAwaitable" or "YieldAwaiter") => true,
        _ => false,
    };

    /// <summary>
    /// ValueTask&lt;T&gt;, its awaiter and its configured awaitable and awaiter: structs ported from .NET
    /// (corelib/Tasks/ValueTaskOfT.cs) — the result inline, a Task&lt;T&gt; or an IValueTaskSource&lt;T&gt; — not task pointers.
    /// </summary>
    public static bool IsValueTaskOfT(ITypeSymbol? type) => type is INamedTypeSymbol named
        && (named.Arity == 1 || named.ContainingType is { Arity: 1 })
        && (Namespace(named), named.Name) is ("System.Threading.Tasks", "ValueTask")
            or ("System.Runtime.CompilerServices", "ValueTaskAwaiter" or "ConfiguredValueTaskAwaitable" or "ConfiguredValueTaskAwaiter");

    public static bool IsYieldAwaitable(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "YieldAwaitable" } named && Namespace(named) == "System.Runtime.CompilerServices";

    private static string Namespace(INamedTypeSymbol type) => type.ContainingNamespace?.ToDisplayString() ?? "";
}
