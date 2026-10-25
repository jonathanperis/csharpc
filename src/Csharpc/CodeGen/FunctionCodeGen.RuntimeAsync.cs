using System.Reflection;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Runtime-async methods from IL (.NET 11's MethodImplOptions.Async, 0x2000): libraries such as ASP.NET
/// Core and System.Net.Http are compiled so that an async method's IL is its plain body — it returns the
/// unwrapped result (`ret` with a T, or nothing, for Task&lt;T&gt; / Task) and awaits by calling
/// System.Runtime.CompilerServices.AsyncHelpers.Await(task) — and the runtime turns it into a resumable
/// method. csharpc lowers such a method exactly like a C# async method from source (docs/10-async.md): an
/// LLVM coroutine whose ramp returns the task, `ret` completing it and an escaping exception faulting it;
/// AsyncHelpers.Await(x) is the source path's `await x`.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private const MethodImplAttributes RuntimeAsyncImpl = (MethodImplAttributes)0x2000;

    /// <summary>Whether a method compiled from IL is a runtime-async one (its IL awaits through AsyncHelpers).</summary>
    internal static bool IsRuntimeAsync(Frontend.Il.IlAssembly assembly, IMethodSymbol method) =>
        method.OriginalDefinition.MetadataToken != 0
        && (assembly.MethodDefinitionOf(method).ImplAttributes & RuntimeAsyncImpl) != 0
        && Coroutines.IsTaskLike(method.ReturnType);

    /// <summary>AsyncHelpers.Await / AwaitAwaiter / UnsafeAwaitAwaiter in a runtime-async method.</summary>
    private IlEntry? IlRuntimeAsyncCall(IMethodSymbol method, IlEntry[] args)
    {
        if (method.Name is not ("Await" or "AwaitAwaiter" or "UnsafeAwaitAwaiter") || args.Length != 1) return null;
        if (_coro is not { Kind: CoroutineKind.Async })
            throw new Frontend.Il.IlException($"AsyncHelpers.{method.Name} outside a runtime-async method");
        var parameterType = method.Parameters[0].Type;
        var operandType = S(parameterType);
        var handled = new IlEntry(IlKind.Token, default, null);
        if (method.Name == "Await")
        {
            // Await(Task / ValueTask / their configured awaitables) and the generic Await<T> overloads: all of
            // them are a csrt task pointer (TypeMapper); a configured awaitable doesn't post to a SynchronizationContext.
            if (!Coroutines.IsTaskHandleType(operandType) || Coroutines.IsYieldAwaitable(operandType))
                throw new Frontend.Il.IlException($"AsyncHelpers.Await({operandType})");
            var task = IlTyped(args[0], parameterType);
            AwaitCompletion(task, AwaitContextRuntime(null, operandType));
            CallRuntime("csrt_task_throw_if_faulted", L.Void, [L.Ptr], task); // rethrows the original exception
            if (method.ReturnsVoid) return handled;
            return IlPush(TaskResult(task, S(method.ReturnType)), method.ReturnType);
        }
        // `await x` on an awaiter type the compiler doesn't special-case: the IL tests IsCompleted, calls
        // (Unsafe)AwaitAwaiter(awaiter) to suspend until it completes, then GetResult() (which throws).
        if (Coroutines.IsYieldAwaitable(operandType))
        {
            AwaitYield();
            return handled;
        }
        if (Coroutines.IsTaskHandleType(operandType))
        {
            AwaitCompletion(IlTyped(args[0], parameterType), operandType.Name.StartsWith("Configured", StringComparison.Ordinal)
                ? "csrt_task_on_completed_no_context" : "csrt_task_on_completed");
            return handled;
        }
        return IlAwaitCustomAwaiter(method, args[0], parameterType, operandType);
    }

    /// <summary>
    /// A custom awaiter: hand it an Action that resumes this coroutine (awaiter.UnsafeOnCompleted for
    /// UnsafeAwaitAwaiter, OnCompleted for AwaitAwaiter), then suspend. The frame is published by the call,
    /// so llvm.coro.save comes first.
    /// </summary>
    private IlEntry IlAwaitCustomAwaiter(IMethodSymbol method, IlEntry awaiter, ITypeSymbol parameterType, ITypeSymbol awaiterType)
    {
        var compilation = _m.Compilation;
        var notify = compilation.GetTypeByMetadataName(method.Name == "UnsafeAwaitAwaiter"
            ? "System.Runtime.CompilerServices.ICriticalNotifyCompletion" : "System.Runtime.CompilerServices.INotifyCompletion")!;
        var interfaceMethod = notify.GetMembers(method.Name == "UnsafeAwaitAwaiter" ? "UnsafeOnCompleted" : "OnCompleted").OfType<IMethodSymbol>().First();
        var onCompleted = awaiterType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol
            ?? throw new Frontend.Il.IlException($"awaiter '{awaiterType}' doesn't implement {notify.Name}");
        IlEntry receiver;
        if (awaiterType.IsValueType)
        {
            var temp = NewTemp(T.Storage(awaiterType), "awaiter");
            StoreValue(ToStorage(IlTyped(awaiter, parameterType), awaiterType), temp);
            receiver = new IlEntry(IlKind.ByRef, temp, parameterType);
        }
        else receiver = awaiter;
        var actionType = compilation.GetTypeByMetadataName("System.Action")!;
        var resume = _m.Runtime("csrt_coro_resume", L.Void, L.Ptr);
        var action = NewDelegate(_m.DelegateMethodTable(actionType), _coro!.Handle, resume.Fn); // Action bound to the frame
        var save = CoroSave();
        IlInvoke(onCompleted, receiver, [new IlEntry(IlKind.Ref, action, actionType) { NonNull = true }], isVirtual: !awaiterType.IsValueType, constrained: null);
        Suspend(_coro.Cleanup, save);
        return new IlEntry(IlKind.Token, default, null);
    }

    /// <summary>`ret` of a runtime-async method: store the result (if any) and complete the task.</summary>
    private void IlCoroutineReturn(Stack<IlEntry> stack)
    {
        var coro = _coro!;
        if (coro.ValueType is { } resultType)
        {
            var value = stack.Count > 0 ? stack.Pop() : throw new Frontend.Il.IlException("evaluation stack underflow");
            StoreValue(ToStorage(IlTyped(value, Coroutines.AsyncResultType(_method!.OriginalDefinition.ReturnType)!), resultType), PromisePayload);
        }
        else if (stack.Count > 0) throw new Frontend.Il.IlException("stack not empty at ret");
        _b.Br(coro.Complete);
    }

    /// <summary>An exception leaving every handler of a runtime-async method faults its task.</summary>
    private void IlCoroutineEscape(LLVMValueRef exception)
    {
        _b.Store(exception, _coro!.ExceptionSlot);
        _b.Br(_coro.Fault);
    }
}
