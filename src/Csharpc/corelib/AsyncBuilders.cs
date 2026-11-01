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
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

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

        /// <summary>Nothing asked for the task yet: the method hasn't suspended (the ValueTask builders' synchronous path).</summary>
        internal bool HasTask => _task != null;

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
        // A method that returns without suspending keeps its result here, as .NET's builder does: the ValueTask
        // then holds it inline (no task for a default result, see `new ValueTask<T>(result)`).
        private TResult _result;
        private bool _completed;

        public static AsyncValueTaskMethodBuilderImpl<TResult> Create() { return default(AsyncValueTaskMethodBuilderImpl<TResult>); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);
        public ValueTask<TResult> Task => _completed ? new ValueTask<TResult>(_result) : new ValueTask<TResult>(_inner.Task);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        public void SetResult(TResult result)
        {
            if (_completed) throw new InvalidOperationException(AsyncBuilderCore.AlreadyCompleted);
            if (_inner.HasTask) { _inner.SetResult(result); return; }
            _result = result;
            _completed = true;
        }
        public void SetException(Exception exception) => _inner.SetException(exception);
    }

    // Not pooled yet: csharpc's non-generic ValueTask is a csrt task pointer with no token, so a reused box
    // could not make a second await of a stale ValueTask throw as .NET's does (it would see the box's next
    // use). Pooling it needs the non-generic ValueTask to become a struct like ValueTask<TResult>.
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

    // .NET's PoolingAsyncValueTaskMethodBuilder<TResult> (release/11.0 PoolingAsyncValueTaskMethodBuilderT.cs):
    // a method that suspends rents a box per state-machine type — the heap copy of the state machine, its
    // MoveNext delegate and a ManualResetValueTaskSourceCore — and returns `new ValueTask<TResult>(box, version)`.
    // GetResult resets the box (a new version: a second await or Result throws InvalidOperationException, as
    // .NET's) and returns it to a one-box-per-thread cache, else to a small shared cache, else drops it.
    // Steady state, a suspending call allocates nothing (the plain builders allocate a task, a box and a delegate).
    // .NET 11 reads no setting for the cache size (DOTNET_SYSTEM_THREADING_POOLASYNCVALUETASKSLIMIT went with .NET 5's
    // global pooling switch), so neither does this.
    [Shadows("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1")]
    public struct PoolingAsyncValueTaskMethodBuilderImpl<TResult>
    {
        // null: nothing yet; PoolingBox<TResult>.SyncSuccess: completed without suspending (_result); else the box.
        private PoolingBox<TResult> _box;
        private TResult _result;

        public static PoolingAsyncValueTaskMethodBuilderImpl<TResult> Create() { return default(PoolingAsyncValueTaskMethodBuilderImpl<TResult>); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => AsyncBuilderCore.CheckStateMachine(stateMachine);

        public ValueTask<TResult> Task
        {
            get
            {
                if (ReferenceEquals(_box, PoolingBox<TResult>.SyncSuccess)) return new ValueTask<TResult>(_result);
                PoolingBox<TResult> box = _box ??= new PooledStateMachine<TResult, IAsyncStateMachine>().Box; // weakly typed, as .NET's
                return new ValueTask<TResult>(box, box.Version);
            }
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine =>
            awaiter.OnCompleted(PoolingBox<TResult>.GetMoveNext(ref _box, ref stateMachine));

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine =>
            awaiter.UnsafeOnCompleted(PoolingBox<TResult>.GetMoveNext(ref _box, ref stateMachine));

        public void SetResult(TResult result)
        {
            if (_box == null) { _result = result; _box = PoolingBox<TResult>.SyncSuccess; return; }
            _box.SetResult(result);
        }

        public void SetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException("exception");
            (_box ??= new PooledStateMachine<TResult, IAsyncStateMachine>().Box).SetException(exception);
        }
    }

    /// <summary>
    /// .NET's PoolingAsyncValueTaskMethodBuilder&lt;TResult&gt;.StateMachineBox, split in two: this sealed source,
    /// which ValueTask&lt;TResult&gt; recognizes by its exact type (ValueTaskSources), and its owner, which holds the
    /// state machine (or a source method's task) and returns both to the owner type's caches.
    /// </summary>
    internal sealed class PoolingBox<TResult> : IValueTaskSource<TResult>
    {
        /// <summary>.NET's s_syncSuccessSentinel: the builder's marker for "returned without suspending".</summary>
        internal static readonly PoolingBox<TResult> SyncSuccess = new PoolingBox<TResult>(null);

        internal ManualResetValueTaskSourceCoreImpl<TResult> _valueTaskSource;
        internal readonly PoolingBoxOwner<TResult> Owner;

        internal PoolingBox(PoolingBoxOwner<TResult> owner) { Owner = owner; }

        internal short Version => _valueTaskSource.Version;
        internal void SetResult(TResult result) => _valueTaskSource.SetResult(result);
        internal void SetException(Exception error) => _valueTaskSource.SetException(error);

        public ValueTaskSourceStatus GetStatus(short token) => _valueTaskSource.GetStatus(token);

        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _valueTaskSource.OnCompleted(continuation, state, token, flags);

        public TResult GetResult(short token)
        {
            try { return _valueTaskSource.GetResult(token); }
            finally { Owner.ReturnOrDropBox(); }
        }

        /// <summary>
        /// .NET's GetStateMachineBox: the box the await resumes (the builder's, or one rented and stored in the
        /// builder before the state machine is copied into it, so the copy knows its box), with the context the
        /// await captured. `boxField` is the builder's field, i.e. a field of `stateMachine`.
        /// </summary>
        internal static Action GetMoveNext<TStateMachine>(ref PoolingBox<TResult> boxField, ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            bool suppressed = ExecutionContextImpl.IsFlowSuppressed();
            ExecutionFlow flow = suppressed ? null : ExecutionFlow.Current;
            PoolingBoxOwner<TResult> owner = boxField?.Owner;
            if (owner is PooledStateMachine<TResult, TStateMachine> strong)
            {
                strong.Flow = flow;
                strong.FlowSuppressed = suppressed;
                return strong.MoveNextAction;
            }
            if (owner is PooledStateMachine<TResult, IAsyncStateMachine> weak)
            {
                // Task was read (or the method faulted) before its first await: a box of the interface type.
                if (weak.StateMachine == null) weak.StateMachine = stateMachine;
                weak.Flow = flow;
                weak.FlowSuppressed = suppressed;
                return weak.MoveNextAction;
            }
            PooledStateMachine<TResult, TStateMachine> rented = PooledStateMachine<TResult, TStateMachine>.RentFromCache();
            boxField = rented.Box; // before the copy below
            rented.StateMachine = stateMachine;
            rented.Flow = flow;
            rented.FlowSuppressed = suppressed;
            return rented.MoveNextAction;
        }
    }

    internal abstract class PoolingBoxOwner<TResult>
    {
        internal readonly PoolingBox<TResult> Box;
        protected PoolingBoxOwner() { Box = new PoolingBox<TResult>(this); }
        internal abstract void ReturnOrDropBox();
    }

    internal sealed class PooledStateMachine<TResult, TStateMachine> : PoolingBoxOwner<TResult> where TStateMachine : IAsyncStateMachine
    {
        // .NET's caches: one box per thread, then one per core (here: per slot of the current thread's id;
        // csharpc has no GetCurrentProcessorId, and which slot holds a box is not observable).
        [ThreadStatic] private static PooledStateMachine<TResult, TStateMachine> t_tlsCache;
        private static readonly PooledStateMachine<TResult, TStateMachine>[] s_sharedCache =
            new PooledStateMachine<TResult, TStateMachine>[Math.Min(Environment.ProcessorCount, 64)];

        public TStateMachine StateMachine;
        internal ExecutionFlow Flow;
        internal bool FlowSuppressed;
        private Action _moveNextAction;

        internal Action MoveNextAction => _moveNextAction ??= MoveNext;

        internal static PooledStateMachine<TResult, TStateMachine> RentFromCache()
        {
            PooledStateMachine<TResult, TStateMachine> owner = t_tlsCache;
            if (owner != null) { t_tlsCache = null; return owner; }
            PooledStateMachine<TResult, TStateMachine>[] shared = s_sharedCache;
            int slot = (int)((uint)Environment.CurrentManagedThreadId % (uint)shared.Length);
            if (Volatile.Read(ref shared[slot]) != null && (owner = Interlocked.Exchange(ref shared[slot], null)) != null) return owner;
            return new PooledStateMachine<TResult, TStateMachine>();
        }

        internal override void ReturnOrDropBox()
        {
            // Nothing the state machine's lifted locals reference stays alive through the cache.
            StateMachine = default;
            Flow = null;
            FlowSuppressed = false;
            Box._valueTaskSource.Reset();
            if (t_tlsCache == null) { t_tlsCache = this; return; }
            PooledStateMachine<TResult, TStateMachine>[] shared = s_sharedCache;
            int slot = (int)((uint)Environment.CurrentManagedThreadId % (uint)shared.Length);
            if (Volatile.Read(ref shared[slot]) == null) Volatile.Write(ref shared[slot], this);
        }

        // As AsyncStateMachineBox.MoveNext: the continuation runs in the context its await captured.
        private void MoveNext()
        {
            if (FlowSuppressed) { StateMachine.MoveNext(); return; }
            ExecutionFlow saved = ExecutionFlow.Current;
            ExecutionFlow.Current = Flow;
            try { StateMachine.MoveNext(); }
            finally { ExecutionFlow.Current = saved; }
        }
    }

    /// <summary>
    /// A pooled box over a coroutine's task (ValueTaskAwait&lt;T&gt;.OfPooledTask): methods compiled from C# source are
    /// coroutines, not builder state machines, but a pooling method's ValueTask must still be good for one await.
    /// </summary>
    internal sealed class PooledTask<TResult> : PoolingBoxOwner<TResult>
    {
        [ThreadStatic] private static PooledTask<TResult> t_tlsCache;
        private Task<TResult> _task;
        private readonly Action _complete;

        private PooledTask() { _complete = Complete; }

        internal static PoolingBox<TResult> For(Task<TResult> task)
        {
            PooledTask<TResult> owner = t_tlsCache;
            if (owner != null) t_tlsCache = null;
            else owner = new PooledTask<TResult>();
            owner._task = task;
            // A task that already faulted completes the box now (IsFaulted is true at once, as with .NET's box).
            if (task.IsCompleted) owner.Complete();
            else task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(owner._complete);
            return owner.Box;
        }

        private void Complete()
        {
            Task<TResult> task = _task;
            TResult result;
            try { result = task.GetAwaiter().GetResult(); }
            catch (Exception e) { Box.SetException(e); return; }
            Box.SetResult(result);
        }

        internal override void ReturnOrDropBox()
        {
            _task = null;
            Box._valueTaskSource.Reset();
            if (t_tlsCache == null) t_tlsCache = this;
        }
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
