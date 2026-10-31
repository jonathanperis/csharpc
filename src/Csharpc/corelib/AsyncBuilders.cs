// Async method builders for async methods compiled from IL (docs/adr/0010-il-frontend.md, docs/10-async.md).
//
// Source async methods are LLVM coroutines and never reach these types. A C# compiler that produced IL
// instead turned each async method into an IAsyncStateMachine (a struct in Release builds, a class in
// Debug) whose MoveNext() drives one of these builders. The builders sit on csrt's task objects:
// Task/Task<T>/ValueTask/awaiters are all the same task pointer, TaskCompletionSource is that task too.
//
// .NET semantics kept here:
//  * Start runs MoveNext synchronously on the caller's thread.
//  * The first await that does not complete synchronously copies the state machine into a heap box
//    *after* the builder's task exists, so the ramp's stack copy and the boxed copy share the task;
//    later awaits reuse the box (one MoveNext delegate per invocation, as AsyncStateMachineBox).
//  * Methods that complete synchronously never allocate a pending task (Task.FromResult-style result).
//  * Exceptions escaping MoveNext fault the task (the awaiter rethrows the original exception), an
//    OperationCanceledException cancels it (and `await` rethrows that same exception);
//    async void exceptions crash the process like an unhandled exception on the thread pool.
//  * Awaiter continuations run where csrt runs them: inline on the completing thread, queued to the
//    pool if the awaited task had already completed (TaskAwaiter.UnsafeOnCompleted semantics).
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    /// <summary>The heap copy of a suspended state machine (.NET's AsyncStateMachineBox, minus the task).</summary>
    internal sealed class AsyncStateMachineBox<TStateMachine> where TStateMachine : IAsyncStateMachine
    {
        public TStateMachine StateMachine;
        public Action MoveNextAction;
        /// <summary>The execution-context flow captured by the last await (ExecutionContext.Capture() in .NET).</summary>
        public ExecutionFlow Flow;
        public bool FlowSuppressed;

        // .NET's AsyncStateMachineBox.MoveNext: the continuation runs in the context its await captured
        // (AsyncLocal values, cultures), and the resuming thread gets its own back afterwards.
        public void MoveNext()
        {
            if (FlowSuppressed) { StateMachine.MoveNext(); return; }
            ExecutionFlow saved = ExecutionFlow.Current;
            ExecutionFlow.Current = Flow;
            try { StateMachine.MoveNext(); }
            finally { ExecutionFlow.Current = saved; }
        }
    }

    internal static class AsyncBuilderCore
    {
        internal const string AlreadyCompleted = "An attempt was made to transition a task to a final state when it had already completed.";

        /// <summary>
        /// The MoveNext delegate of the boxed state machine. `box` is the builder's own field, i.e. a
        /// field of `stateMachine`: it is set before the copy, so the box's copy knows it is boxed.
        /// </summary>
        public static Action GetMoveNext<TStateMachine>(ref object box, ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            if (box is AsyncStateMachineBox<TStateMachine> existing)
            {
                CaptureFlow(existing);
                return existing.MoveNextAction;
            }
            var created = new AsyncStateMachineBox<TStateMachine>();
            box = created;
            CaptureFlow(created);
            created.StateMachine = stateMachine;
            created.MoveNextAction = created.MoveNext;
            return created.MoveNextAction;
        }

        private static void CaptureFlow<TStateMachine>(AsyncStateMachineBox<TStateMachine> box) where TStateMachine : IAsyncStateMachine
        {
            box.FlowSuppressed = ExecutionContextImpl.IsFlowSuppressed();
            box.Flow = box.FlowSuppressed ? null : ExecutionFlow.Current;
        }

        public static void AwaitOnCompleted<TAwaiter, TStateMachine>(ref object box, ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.OnCompleted(GetMoveNext(ref box, ref stateMachine));
        }

        public static void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref object box, ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.UnsafeOnCompleted(GetMoveNext(ref box, ref stateMachine));
        }

        // .NET's AsyncMethodBuilderCore.Start: context changes made before the first await (an AsyncLocal
        // set, a culture changed) don't flow back to the caller, and neither does a SynchronizationContext change.
        public static void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            if (stateMachine == null) throw new ArgumentNullException("stateMachine");
            ExecutionFlow savedFlow = ExecutionFlow.Current;
            System.Threading.SynchronizationContext savedContext = System.Threading.SynchronizationContext.Current;
            try { stateMachine.MoveNext(); }
            finally
            {
                ExecutionFlow.Current = savedFlow;
                if (System.Threading.SynchronizationContext.Current != savedContext) System.Threading.SynchronizationContext.SetSynchronizationContext(savedContext);
            }
        }

        public static void CheckStateMachine(IAsyncStateMachine stateMachine)
        {
            if (stateMachine == null) throw new ArgumentNullException("stateMachine");
        }
    }

    [Shadows("System.Runtime.CompilerServices.AsyncTaskMethodBuilder")]
    public struct AsyncTaskMethodBuilderImpl
    {
        private TaskCompletionSource _source; // the pending task, once the method suspended or Task was read
        private Task _task;
        private object _box;

        public static AsyncTaskMethodBuilderImpl Create() { return default(AsyncTaskMethodBuilderImpl); }

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);

        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);

        public Task Task
        {
            get
            {
                if (_task == null) { _source = new TaskCompletionSource(); _task = _source.Task; }
                return _task;
            }
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            _ = Task;
            AsyncBuilderCore.AwaitOnCompleted(ref _box, ref awaiter, ref stateMachine);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            _ = Task;
            AsyncBuilderCore.AwaitUnsafeOnCompleted(ref _box, ref awaiter, ref stateMachine);
        }

        public void SetResult()
        {
            if (_task == null) { _task = System.Threading.Tasks.Task.CompletedTask; return; }
            if (_source == null || !_source.TrySetResult()) throw new InvalidOperationException(AsyncBuilderCore.AlreadyCompleted);
        }

        public void SetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException("exception");
            if (!CoreIntrinsics.TryFaultAsyncTask(Task, exception)) throw new InvalidOperationException(AsyncBuilderCore.AlreadyCompleted);
        }
    }

    [Shadows("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1")]
    public struct AsyncTaskMethodBuilderImpl<TResult>
    {
        private TaskCompletionSource<TResult> _source;
        private Task<TResult> _task;
        private object _box;

        public static AsyncTaskMethodBuilderImpl<TResult> Create() { return default(AsyncTaskMethodBuilderImpl<TResult>); }

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);

        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);

        public Task<TResult> Task
        {
            get
            {
                if (_task == null) { _source = new TaskCompletionSource<TResult>(); _task = _source.Task; }
                return _task;
            }
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            _ = Task;
            AsyncBuilderCore.AwaitOnCompleted(ref _box, ref awaiter, ref stateMachine);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            _ = Task;
            AsyncBuilderCore.AwaitUnsafeOnCompleted(ref _box, ref awaiter, ref stateMachine);
        }

        public void SetResult(TResult result)
        {
            if (_task == null) { _task = System.Threading.Tasks.Task.FromResult(result); return; }
            if (_source == null || !_source.TrySetResult(result)) throw new InvalidOperationException(AsyncBuilderCore.AlreadyCompleted);
        }

        public void SetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException("exception");
            if (!CoreIntrinsics.TryFaultAsyncTask(Task, exception)) throw new InvalidOperationException(AsyncBuilderCore.AlreadyCompleted);
        }
    }

    // ValueTask and ValueTask<T> are csrt task pointers as well (a completed ValueTask is a completed
    // task), so the ValueTask builders are the Task builders with a different Task property type.
    [Shadows("System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder")]
    public struct AsyncValueTaskMethodBuilderImpl
    {
        private AsyncTaskMethodBuilderImpl _inner;

        public static AsyncValueTaskMethodBuilderImpl Create() { return default(AsyncValueTaskMethodBuilderImpl); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);
        public ValueTask Task => new ValueTask(_inner.Task);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        public void SetResult() => _inner.SetResult();
        public void SetException(Exception exception) => _inner.SetException(exception);
    }

    [Shadows("System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder`1")]
    public struct AsyncValueTaskMethodBuilderImpl<TResult>
    {
        private AsyncTaskMethodBuilderImpl<TResult> _inner;

        public static AsyncValueTaskMethodBuilderImpl<TResult> Create() { return default(AsyncValueTaskMethodBuilderImpl<TResult>); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);
        public ValueTask<TResult> Task => new ValueTask<TResult>(_inner.Task);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        public void SetResult(TResult result) => _inner.SetResult(result);
        public void SetException(Exception exception) => _inner.SetException(exception);
    }

    [Shadows("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder")]
    public struct PoolingAsyncValueTaskMethodBuilderImpl
    {
        private AsyncTaskMethodBuilderImpl _inner;

        public static PoolingAsyncValueTaskMethodBuilderImpl Create() { return default(PoolingAsyncValueTaskMethodBuilderImpl); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);
        public ValueTask Task => new ValueTask(_inner.Task);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        public void SetResult() => _inner.SetResult();
        public void SetException(Exception exception) => _inner.SetException(exception);
    }

    [Shadows("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1")]
    public struct PoolingAsyncValueTaskMethodBuilderImpl<TResult>
    {
        private AsyncTaskMethodBuilderImpl<TResult> _inner;

        public static PoolingAsyncValueTaskMethodBuilderImpl<TResult> Create() { return default(PoolingAsyncValueTaskMethodBuilderImpl<TResult>); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);
        public ValueTask<TResult> Task => new ValueTask<TResult>(_inner.Task);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        public void SetResult(TResult result) => _inner.SetResult(result);
        public void SetException(Exception exception) => _inner.SetException(exception);
    }

    [Shadows("System.Runtime.CompilerServices.AsyncVoidMethodBuilder")]
    public struct AsyncVoidMethodBuilderImpl
    {
        private object _box;
        private object _context; // the SynchronizationContext it reports to (SynchronizationContext.cs)

        public static AsyncVoidMethodBuilderImpl Create()
        {
            AsyncVoidMethodBuilderImpl builder = default(AsyncVoidMethodBuilderImpl);
            builder._context = SynchronizationContextImpl.AsyncVoidStarted();
            return builder;
        }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.AwaitOnCompleted(ref _box, ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.AwaitUnsafeOnCompleted(ref _box, ref awaiter, ref stateMachine);
        public void SetResult() => SynchronizationContextImpl.AsyncVoidCompleted(_context);

        /// <summary>Rethrown on the SynchronizationContext; without one .NET rethrows on the thread pool, which ends the process.</summary>
        public void SetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException("exception");
            SynchronizationContextImpl.AsyncVoidFailed(_context, exception);
        }
    }

    /// <summary>
    /// What csc's synthesized sync entry point (`&lt;Main&gt;` of an async Main) calls since .NET 11: wait for
    /// the task and rethrow its original exception, as GetAwaiter().GetResult() does.
    /// </summary>
    [Shadows("System.Runtime.CompilerServices.AsyncHelpers")]
    public static class AsyncHelpersImpl
    {
        public static void HandleAsyncEntryPoint(Task task) { task.GetAwaiter().GetResult(); }

        public static int HandleAsyncEntryPoint(Task<int> task) => task.GetAwaiter().GetResult();
    }
}
