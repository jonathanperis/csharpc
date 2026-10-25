// TaskScheduler, TaskFactory / TaskFactory<TResult> (Task.Factory.StartNew) and Task.ContinueWith over
// the C runtime's tasks (runtime/async.c). A started task is a TaskCompletionSource's task completed by
// a work item: on the thread pool for TaskScheduler.Default, or handed to a custom scheduler's
// QueueTask, whose TryExecuteTask runs it (TaskScheduler.Current is that scheduler meanwhile). The
// results follow dotnet/runtime: argument validation and messages, canceled vs faulted (a token
// canceled before the work runs, or an OperationCanceledException once it is canceled), continuation
// options (NotOn*/OnlyOn*, early cancellation unless LazyCancellation). A child task attached to its
// parent (AttachedToParent where a parent would accept it) throws NotSupportedException instead of
// silently not being waited for.
#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.Tasks.TaskScheduler")]
    public abstract class TaskSchedulerImpl
    {
        private static TaskSchedulerImpl s_default;
        private static int s_taskSchedulerIdCounter;
        private volatile int m_taskSchedulerId;

        // Tasks queued to this (custom) scheduler, with their work item until it runs.
        private readonly List<KeyValuePair<Task, Action>> _pending = new List<KeyValuePair<Task, Action>>();

        protected TaskSchedulerImpl() { }

        public static TaskSchedulerImpl Default
        {
            get
            {
                if (s_default == null) Interlocked.CompareExchange(ref s_default, new ThreadPoolTaskSchedulerImpl(), null);
                return s_default;
            }
        }

        public static TaskSchedulerImpl Current => TaskSupport.t_currentScheduler ?? Default;

        public virtual int MaximumConcurrencyLevel => int.MaxValue;

        public int Id
        {
            get
            {
                int id = m_taskSchedulerId;
                if (id == 0)
                {
                    int newId;
                    do newId = Interlocked.Increment(ref s_taskSchedulerIdCounter);
                    while (newId == 0);
                    Interlocked.CompareExchange(ref m_taskSchedulerId, newId, 0);
                    id = m_taskSchedulerId;
                }
                return id;
            }
        }

        protected internal abstract void QueueTask(Task task);
        protected abstract bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued);
        protected abstract IEnumerable<Task> GetScheduledTasks();
        protected internal virtual bool TryDequeue(Task task) => false;

        protected bool TryExecuteTask(Task task)
        {
            Action work = null;
            bool known = false;
            lock (_pending)
            {
                for (int i = 0; i < _pending.Count; i++)
                    if (ReferenceEquals(_pending[i].Key, task))
                    {
                        known = true;
                        work = _pending[i].Value;
                        if (work != null) _pending[i] = new KeyValuePair<Task, Action>(task, null);
                        break;
                    }
            }
            if (!known) throw new InvalidOperationException("ExecuteTask may not be called for a task which was previously queued to a different TaskScheduler.");
            if (work == null) return false; // ran already (the entry stays: the task was queued here)
            work();
            return true;
        }

        internal void Schedule(Task task, Action work)
        {
            TaskSupport.MarkQueued(task); // Task.Status: WaitingToRun from here, Running once the work starts
            if (this is ThreadPoolTaskSchedulerImpl)
            {
                Task.Run(work);
                return;
            }
            lock (_pending) _pending.Add(new KeyValuePair<Task, Action>(task, work));
            QueueTask(task);
        }

        public static TaskSchedulerImpl FromCurrentSynchronizationContext() =>
            throw new InvalidOperationException("The current SynchronizationContext may not be used as a TaskScheduler.");

        // Raised when an unobserved faulted task is finalized: csharpc never finalizes tasks.
        public static event EventHandler<UnobservedTaskExceptionEventArgs> UnobservedTaskException
        {
            add { }
            remove { }
        }
    }

    // TaskScheduler.UnobservedTaskException's arguments (UnobservedTaskExceptionEventArgs.cs): the event never
    // fires in csharpc (tasks aren't finalized), but handlers that read them compile.
    [Shadows("System.Threading.Tasks.UnobservedTaskExceptionEventArgs")]
    public class UnobservedTaskExceptionEventArgsImpl : EventArgs
    {
        private readonly AggregateException _exception;
        internal bool _observed;

        public UnobservedTaskExceptionEventArgsImpl(AggregateException exception) => _exception = exception;

        public void SetObserved() => _observed = true;

        public bool Observed => _observed;

        public AggregateException Exception => _exception;
    }

    internal sealed class ThreadPoolTaskSchedulerImpl : TaskSchedulerImpl
    {
        protected internal override void QueueTask(Task task) { }
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => null;
    }

    internal static class TaskSupport
    {
        [ThreadStatic] internal static TaskSchedulerImpl t_currentScheduler;

        private const TaskCreationOptions ValidCreationOptions = TaskCreationOptions.AttachedToParent | TaskCreationOptions.DenyChildAttach
            | TaskCreationOptions.HideScheduler | TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness
            | TaskCreationOptions.RunContinuationsAsynchronously;

        // A delegate task started here is a TaskCompletionSource's task: the runtime is told it is queued and
        // then running, so Task.Status reports WaitingToRun / Running like .NET's (async.c, TF_* flags).
        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_task_mark_queued")]
        private static extern void MarkQueued(IntPtr task);
        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_task_mark_running")]
        private static extern void MarkRunning(IntPtr task);
        internal static void MarkQueued(Task task) => MarkQueued(CoreIntrinsics.ObjectToPointer(task));
        internal static void MarkRunning(Task task) => MarkRunning(CoreIntrinsics.ObjectToPointer(task));

        internal static void CheckCreationOptions(TaskCreationOptions creationOptions)
        {
            if ((creationOptions & ~ValidCreationOptions) != 0) throw new ArgumentOutOfRangeException("creationOptions");
        }

        // ---- attached child tasks ------------------------------------------------------------------
        // A delegate task without DenyChildAttach is a parent while its body runs: tasks created then with
        // AttachedToParent join it, and it completes only when its body and all of them have, faulted with
        // their exceptions as in .NET (a child's AggregateException, after the body's own exception).

        internal sealed class ParentScope
        {
            private readonly object _lock = new object();
            private int _pending = 1;                    // the body, then one per attached child
            private List<Exception> _exceptions;
            private bool _canceled;
            private readonly Action<List<Exception>, bool> _finish;

            internal ParentScope(Action<List<Exception>, bool> finish) { _finish = finish; }

            internal void AddChild(Task child)
            {
                lock (_lock) _pending++;
                child.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
                {
                    if (child.IsFaulted) lock (_lock) (_exceptions ??= new List<Exception>()).Add(child.Exception);
                    Release();
                });
            }

            internal void BodyDone(Exception exception, bool canceled)
            {
                lock (_lock)
                {
                    if (exception != null) (_exceptions ??= new List<Exception>()).Insert(0, exception);
                    _canceled = canceled;
                }
                Release();
            }

            private void Release()
            {
                bool last;
                lock (_lock) last = --_pending == 0;
                if (last) _finish(_exceptions, _canceled);
            }
        }

        [ThreadStatic] private static ParentScope t_parent;

        /// <summary>A task just created with `options`: attached to the running parent task, if it asks to be and there is one.</summary>
        internal static void AttachToParent(Task child, TaskCreationOptions options)
        {
            if ((options & TaskCreationOptions.AttachedToParent) != 0 && t_parent is { } parent) parent.AddChild(child);
        }

        /// <summary>Runs `body` as the task's work: scheduler context, cancellation, children and outcome as in .NET.</summary>
        internal static Action Work(Action body, TaskCompletionSource tcs, CancellationToken token, TaskCreationOptions options, TaskSchedulerImpl scheduler) =>
            Work(() => { body(); return 0; }, (exceptions, canceled, _) =>
            {
                if (exceptions != null) tcs.TrySetException(exceptions);
                else if (canceled) tcs.TrySetCanceled();
                else tcs.TrySetResult();
            }, tcs.Task, token, options, scheduler);

        internal static Action Work<TResult>(Func<TResult> body, TaskCompletionSource<TResult> tcs, CancellationToken token, TaskCreationOptions options, TaskSchedulerImpl scheduler) =>
            Work(body, (exceptions, canceled, result) =>
            {
                if (exceptions != null) tcs.TrySetException(exceptions);
                else if (canceled) tcs.TrySetCanceled();
                else tcs.TrySetResult(result);
            }, tcs.Task, token, options, scheduler);

        private static Action Work<TResult>(Func<TResult> body, Action<List<Exception>, bool, TResult> finish, Task task,
            CancellationToken token, TaskCreationOptions options, TaskSchedulerImpl scheduler)
        {
            return () =>
            {
                TaskSchedulerImpl savedScheduler = t_currentScheduler;
                ParentScope savedParent = t_parent;
                t_currentScheduler = (options & TaskCreationOptions.HideScheduler) != 0 || scheduler is ThreadPoolTaskSchedulerImpl ? null : scheduler;
                TResult result = default;
                var scope = new ParentScope((exceptions, canceled) => finish(exceptions, canceled, result));
                t_parent = (options & TaskCreationOptions.DenyChildAttach) == 0 ? scope : null;
                IntPtr savedTask = ColdTasks.SwapCurrent(CoreIntrinsics.ObjectToPointer(task)); // Task.CurrentId
                Exception error = null;
                bool canceled = false;
                try
                {
                    if (token.IsCancellationRequested) canceled = true;
                    else
                    {
                        MarkRunning(task);
                        result = body();
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { canceled = true; }
                catch (Exception e) { error = e; }
                finally
                {
                    t_currentScheduler = savedScheduler;
                    t_parent = savedParent;
                    ColdTasks.SwapCurrent(savedTask);
                }
                scope.BodyDone(error, canceled);
            };
        }

        internal static Task StartNew(object callback, Action body, CancellationToken token, TaskCreationOptions options, TaskSchedulerImpl scheduler)
        {
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            if (callback == null) throw new ArgumentNullException("action");
            CheckCreationOptions(options);
            TaskCompletionSource tcs = new TaskCompletionSource();
            AttachToParent(tcs.Task, options);
            scheduler.Schedule(tcs.Task, Work(body, tcs, token, options, scheduler)); // canceled when it would run
            return tcs.Task;
        }

        internal static Task<TResult> StartNew<TResult>(object callback, Func<TResult> body, CancellationToken token, TaskCreationOptions options, TaskSchedulerImpl scheduler)
        {
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            if (callback == null) throw new ArgumentNullException("function");
            CheckCreationOptions(options);
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
            AttachToParent(tcs.Task, options);
            scheduler.Schedule(tcs.Task, Work(body, tcs, token, options, scheduler)); // canceled when it would run
            return tcs.Task;
        }

        // ---- ContinueWith -------------------------------------------------------------------------

        private const TaskContinuationOptions NotOnAnything = TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.NotOnFaulted
            | TaskContinuationOptions.NotOnRanToCompletion;
        private const TaskContinuationOptions CreationOptionsMask = TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning
            | TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.HideScheduler | TaskContinuationOptions.AttachedToParent
            | TaskContinuationOptions.RunContinuationsAsynchronously;

        internal static TaskCreationOptions CreationOptionsFromContinuationOptions(TaskContinuationOptions continuationOptions)
        {
            const TaskContinuationOptions IllegalMask = TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.LongRunning;
            if ((continuationOptions & IllegalMask) == IllegalMask)
                throw new ArgumentOutOfRangeException("continuationOptions", "The specified TaskContinuationOptions combined LongRunning and ExecuteSynchronously.  Synchronous continuations should not be long running.");
            if ((continuationOptions & ~(CreationOptionsMask | NotOnAnything | TaskContinuationOptions.LazyCancellation | TaskContinuationOptions.ExecuteSynchronously)) != 0)
                throw new ArgumentOutOfRangeException("continuationOptions");
            if ((continuationOptions & NotOnAnything) == NotOnAnything)
                throw new ArgumentOutOfRangeException("continuationOptions", "The specified TaskContinuationOptions excluded all continuation kinds.");
            return (TaskCreationOptions)(continuationOptions & CreationOptionsMask);
        }

        private static bool Excluded(Task antecedent, TaskContinuationOptions options) =>
            antecedent.IsCanceled ? (options & TaskContinuationOptions.NotOnCanceled) != 0
            : antecedent.IsFaulted ? (options & TaskContinuationOptions.NotOnFaulted) != 0
            : (options & TaskContinuationOptions.NotOnRanToCompletion) != 0;

        internal static Task ContinueWith(Task antecedent, Action body, object callback, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler, string callbackName)
        {
            if (callback == null) throw new ArgumentNullException(callbackName);
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            TaskCreationOptions creation = CreationOptionsFromContinuationOptions(options);
            TaskCompletionSource tcs = new TaskCompletionSource();
            AttachToParent(tcs.Task, creation);
            Action work = Work(body, tcs, token, creation, scheduler);
            Hook(antecedent, () => tcs.TrySetCanceled(), tcs.Task, work, token, options, scheduler);
            return tcs.Task;
        }

        internal static Task<TResult> ContinueWith<TResult>(Task antecedent, Func<TResult> body, object callback, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            if (callback == null) throw new ArgumentNullException("continuationFunction");
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            TaskCreationOptions creation = CreationOptionsFromContinuationOptions(options);
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
            AttachToParent(tcs.Task, creation);
            Action work = Work(body, tcs, token, creation, scheduler);
            Hook(antecedent, () => tcs.TrySetCanceled(), tcs.Task, work, token, options, scheduler);
            return tcs.Task;
        }

        private static void Hook(Task antecedent, Action cancel, Task continuation, Action work, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            bool lazy = (options & TaskContinuationOptions.LazyCancellation) != 0;
            if (!lazy && token.IsCancellationRequested) { cancel(); return; }
            // Canceling the token cancels the continuation at once, before its antecedent completes.
            CancellationTokenRegistration registration = lazy ? default(CancellationTokenRegistration) : token.Register(cancel);
            antecedent.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
            {
                registration.Dispose();
                if (continuation.IsCompleted) return; // canceled early
                if (Excluded(antecedent, options) || (lazy && token.IsCancellationRequested)) { cancel(); return; }
                scheduler.Schedule(continuation, work);
            });
        }
    }

    [Shadows("System.Threading.Tasks.TaskFactory")]
    public class TaskFactoryImpl
    {
        private readonly CancellationToken m_defaultCancellationToken;
        private readonly TaskSchedulerImpl m_defaultScheduler;
        private readonly TaskCreationOptions m_defaultCreationOptions;
        private readonly TaskContinuationOptions m_defaultContinuationOptions;

        public TaskFactoryImpl() { }
        public TaskFactoryImpl(CancellationToken cancellationToken) { m_defaultCancellationToken = cancellationToken; }
        public TaskFactoryImpl(TaskScheduler scheduler) { m_defaultScheduler = (TaskSchedulerImpl)(object)scheduler; }

        public TaskFactoryImpl(TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions)
        {
            TaskFactoryChecks.CheckMultiTaskContinuationOptions(continuationOptions);
            TaskSupport.CheckCreationOptions(creationOptions);
            m_defaultCreationOptions = creationOptions;
            m_defaultContinuationOptions = continuationOptions;
        }

        public TaskFactoryImpl(CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
            : this(creationOptions, continuationOptions)
        {
            m_defaultCancellationToken = cancellationToken;
            m_defaultScheduler = (TaskSchedulerImpl)(object)scheduler;
        }

        public CancellationToken CancellationToken => m_defaultCancellationToken;
        public TaskSchedulerImpl Scheduler => m_defaultScheduler;
        public TaskCreationOptions CreationOptions => m_defaultCreationOptions;
        public TaskContinuationOptions ContinuationOptions => m_defaultContinuationOptions;

        private TaskSchedulerImpl DefaultScheduler => m_defaultScheduler ?? TaskSchedulerImpl.Current;

        public Task StartNew(Action action) => TaskSupport.StartNew(action, action, m_defaultCancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task StartNew(Action action, CancellationToken cancellationToken) => TaskSupport.StartNew(action, action, cancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task StartNew(Action action, TaskCreationOptions creationOptions) => TaskSupport.StartNew(action, action, m_defaultCancellationToken, creationOptions, DefaultScheduler);
        public Task StartNew(Action action, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            TaskSupport.StartNew(action, action, cancellationToken, creationOptions, (TaskSchedulerImpl)(object)scheduler);

        public Task StartNew(Action<object> action, object state) => TaskSupport.StartNew(action, () => action(state), m_defaultCancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task StartNew(Action<object> action, object state, CancellationToken cancellationToken) => TaskSupport.StartNew(action, () => action(state), cancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task StartNew(Action<object> action, object state, TaskCreationOptions creationOptions) => TaskSupport.StartNew(action, () => action(state), m_defaultCancellationToken, creationOptions, DefaultScheduler);
        public Task StartNew(Action<object> action, object state, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return TaskSupport.StartNew(action, () => action(state), cancellationToken, creationOptions, (TaskSchedulerImpl)(object)scheduler);
        }

        public Task<TResult> StartNew<TResult>(Func<TResult> function) => TaskSupport.StartNew(function, function, m_defaultCancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken) => TaskSupport.StartNew(function, function, cancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew<TResult>(Func<TResult> function, TaskCreationOptions creationOptions) => TaskSupport.StartNew(function, function, m_defaultCancellationToken, creationOptions, DefaultScheduler);
        public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            TaskSupport.StartNew(function, function, cancellationToken, creationOptions, (TaskSchedulerImpl)(object)scheduler);

        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state) => TaskSupport.StartNew(function, () => function(state), m_defaultCancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state, CancellationToken cancellationToken) => TaskSupport.StartNew(function, () => function(state), cancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state, TaskCreationOptions creationOptions) => TaskSupport.StartNew(function, () => function(state), m_defaultCancellationToken, creationOptions, DefaultScheduler);
        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return TaskSupport.StartNew(function, () => function(state), cancellationToken, creationOptions, (TaskSchedulerImpl)(object)scheduler);
        }
    }

    [Shadows("System.Threading.Tasks.TaskFactory`1")]
    public class TaskFactoryImpl<TResult>
    {
        private readonly CancellationToken m_defaultCancellationToken;
        private readonly TaskSchedulerImpl m_defaultScheduler;
        private readonly TaskCreationOptions m_defaultCreationOptions;
        private readonly TaskContinuationOptions m_defaultContinuationOptions;

        public TaskFactoryImpl() { }
        public TaskFactoryImpl(CancellationToken cancellationToken) { m_defaultCancellationToken = cancellationToken; }
        public TaskFactoryImpl(TaskScheduler scheduler) { m_defaultScheduler = (TaskSchedulerImpl)(object)scheduler; }

        public TaskFactoryImpl(TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions)
        {
            TaskFactoryChecks.CheckMultiTaskContinuationOptions(continuationOptions);
            TaskSupport.CheckCreationOptions(creationOptions);
            m_defaultCreationOptions = creationOptions;
            m_defaultContinuationOptions = continuationOptions;
        }

        public TaskFactoryImpl(CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
            : this(creationOptions, continuationOptions)
        {
            m_defaultCancellationToken = cancellationToken;
            m_defaultScheduler = (TaskSchedulerImpl)(object)scheduler;
        }

        /// <summary>Task&lt;TResult&gt;.Factory (Intrinsics.cs): a new default factory, as .NET's is.</summary>
        internal static TaskFactoryImpl<TResult> CreateDefault() => new TaskFactoryImpl<TResult>();

        public CancellationToken CancellationToken => m_defaultCancellationToken;
        public TaskSchedulerImpl Scheduler => m_defaultScheduler;
        public TaskCreationOptions CreationOptions => m_defaultCreationOptions;
        public TaskContinuationOptions ContinuationOptions => m_defaultContinuationOptions;

        private TaskSchedulerImpl DefaultScheduler => m_defaultScheduler ?? TaskSchedulerImpl.Current;

        public Task<TResult> StartNew(Func<TResult> function) => TaskSupport.StartNew(function, function, m_defaultCancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew(Func<TResult> function, CancellationToken cancellationToken) => TaskSupport.StartNew(function, function, cancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew(Func<TResult> function, TaskCreationOptions creationOptions) => TaskSupport.StartNew(function, function, m_defaultCancellationToken, creationOptions, DefaultScheduler);
        public Task<TResult> StartNew(Func<TResult> function, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            TaskSupport.StartNew(function, function, cancellationToken, creationOptions, (TaskSchedulerImpl)(object)scheduler);

        public Task<TResult> StartNew(Func<object, TResult> function, object state) => TaskSupport.StartNew(function, () => function(state), m_defaultCancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew(Func<object, TResult> function, object state, CancellationToken cancellationToken) => TaskSupport.StartNew(function, () => function(state), cancellationToken, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> StartNew(Func<object, TResult> function, object state, TaskCreationOptions creationOptions) => TaskSupport.StartNew(function, () => function(state), m_defaultCancellationToken, creationOptions, DefaultScheduler);
        public Task<TResult> StartNew(Func<object, TResult> function, object state, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return TaskSupport.StartNew(function, () => function(state), cancellationToken, creationOptions, (TaskSchedulerImpl)(object)scheduler);
        }
    }

    internal static class TaskFactoryChecks
    {
        internal static void CheckMultiTaskContinuationOptions(TaskContinuationOptions continuationOptions)
        {
            const TaskContinuationOptions NotOnAny = TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.NotOnFaulted | TaskContinuationOptions.NotOnRanToCompletion;
            const TaskContinuationOptions IllegalMask = TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.LongRunning;
            if ((continuationOptions & IllegalMask) == IllegalMask)
                throw new ArgumentOutOfRangeException("continuationOptions", "The specified TaskContinuationOptions combined LongRunning and ExecuteSynchronously.  Synchronous continuations should not be long running.");
            if ((continuationOptions & ~(TaskContinuationOptions.LongRunning | TaskContinuationOptions.PreferFairness | TaskContinuationOptions.AttachedToParent
                    | TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.HideScheduler | TaskContinuationOptions.LazyCancellation
                    | NotOnAny | TaskContinuationOptions.ExecuteSynchronously)) != 0)
                throw new ArgumentOutOfRangeException("continuationOptions");
            if ((continuationOptions & NotOnAny) != 0)
                throw new ArgumentOutOfRangeException("continuationOptions", "It is invalid to exclude specific continuation kinds for continuations off of multiple tasks.");
        }
    }

    /// <summary>
    /// TaskExtensions.Unwrap (TaskExtensions.cs, Task.CreateUnwrapPromise): an outer task that already ran to
    /// completion gives its inner task itself; otherwise a proxy that is faulted or canceled like the outer
    /// task, canceled for a null inner task, and else completes like the inner one. (csharpc's tasks keep one
    /// exception, the first, where .NET's proxy would carry every inner exception of an aggregate.)
    /// </summary>
    [ShadowMembers("System.Threading.Tasks.TaskExtensions")]
    internal static class TaskExtensionsImpl
    {
        public static Task Unwrap(Task<Task> task)
        {
            if (task == null) throw new ArgumentNullException("task");
            if (task.IsCompletedSuccessfully) return task.Result ?? Canceled<object>();
            var proxy = new TaskCompletionSource<object>();
            task.ContinueWith(outer =>
            {
                if (Forward(outer, proxy)) return;
                Task inner = outer.Result;
                if (inner == null) { proxy.TrySetCanceled(); return; }
                inner.ContinueWith(i => { if (!Forward(i, proxy)) proxy.TrySetResult(null); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return proxy.Task;
        }

        public static Task<TResult> Unwrap<TResult>(Task<Task<TResult>> task)
        {
            if (task == null) throw new ArgumentNullException("task");
            if (task.IsCompletedSuccessfully) return task.Result ?? Canceled<TResult>();
            var proxy = new TaskCompletionSource<TResult>();
            task.ContinueWith(outer =>
            {
                if (Forward(outer, proxy)) return;
                Task<TResult> inner = outer.Result;
                if (inner == null) { proxy.TrySetCanceled(); return; }
                inner.ContinueWith(i => { if (!Forward(i, proxy)) proxy.TrySetResult(i.Result); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return proxy.Task;
        }

        // A faulted or canceled task passes its outcome to the proxy; false when it ran to completion.
        private static bool Forward<T>(Task task, TaskCompletionSource<T> proxy)
        {
            if (task.IsFaulted) { proxy.TrySetException(task.Exception.InnerException); return true; }
            if (task.IsCanceled) { proxy.TrySetCanceled(); return true; }
            return false;
        }

        private static Task<T> Canceled<T>()
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetCanceled();
            return tcs.Task;
        }
    }

    /// <summary>ValueTask factories beyond the intrinsics (a ValueTask is its task: Intrinsics.cs).</summary>
    [ShadowMembers("System.Threading.Tasks.ValueTask")]
    internal static class ValueTaskMembers
    {
        public static ValueTask FromCanceled(CancellationToken cancellationToken) => new ValueTask(Task.FromCanceled(cancellationToken));
        public static ValueTask<TResult> FromCanceled<TResult>(CancellationToken cancellationToken) => new ValueTask<TResult>(Task.FromCanceled<TResult>(cancellationToken));
    }

    /// <summary>Task.Factory and Task.ContinueWith (Task&lt;TResult&gt;'s are Intrinsics.cs forwarders to TaskResultContinuations).</summary>
    [ShadowMembers("System.Threading.Tasks.Task")]
    internal static class TaskMembers
    {
        private static TaskFactoryImpl s_factory;
        public static TaskFactoryImpl Factory => s_factory ??= new TaskFactoryImpl();

        // Task.Run with a token: .NET's Task.InternalStartNew(DenyChildAttach, TaskScheduler.Default); a
        // token that is already canceled still queues the task (WaitingToRun), which cancels when it would run.
        public static Task Run(Action action, CancellationToken cancellationToken)
        {
            if (action == null) throw new ArgumentNullException("action");
            return TaskSupport.StartNew(action, action, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskSchedulerImpl.Default);
        }

        public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)
        {
            if (function == null) throw new ArgumentNullException("function");
            return TaskSupport.StartNew(function, function, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskSchedulerImpl.Default);
        }

        public static Task Run(Func<Task> function, CancellationToken cancellationToken)
        {
            if (function == null) throw new ArgumentNullException("function");
            return Unwrap(TaskSupport.StartNew(function, function, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskSchedulerImpl.Default));
        }

        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken)
        {
            if (function == null) throw new ArgumentNullException("function");
            return Unwrap(TaskSupport.StartNew(function, function, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskSchedulerImpl.Default));
        }

        /// <summary>
        /// The proxy of Task.Run(Func&lt;Task&gt;): canceled or faulted like the outer task, then like the inner
        /// one (a null inner task is a cancellation, as in .NET's UnwrapPromise).
        /// </summary>
        private static async Task Unwrap(Task<Task> outer)
        {
            Task inner = await outer.ConfigureAwait(false);
            if (inner == null) throw new TaskCanceledException();
            await inner.ConfigureAwait(false);
        }

        private static async Task<TResult> Unwrap<TResult>(Task<Task<TResult>> outer)
        {
            Task<TResult> inner = await outer.ConfigureAwait(false);
            if (inner == null) throw new TaskCanceledException();
            return await inner.ConfigureAwait(false);
        }

        public static Task FromCanceled(CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested) throw new ArgumentOutOfRangeException("cancellationToken");
            TaskCompletionSource tcs = new TaskCompletionSource();
            tcs.SetCanceled();
            return tcs.Task;
        }

        public static Task<TResult> FromCanceled<TResult>(CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested) throw new ArgumentOutOfRangeException("cancellationToken");
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
            tcs.SetCanceled();
            return tcs.Task;
        }

        // Task.WaitAll / WaitAny (Task.cs): validate, wait (for all tasks, or the first), then WaitAll throws one
        // AggregateException of every faulted task's exceptions and a TaskCanceledException per canceled task,
        // in task order.
        public static void WaitAll(params Task[] tasks) => WaitAllCore(tasks, -1, default);
        public static void WaitAll(ReadOnlySpan<Task> tasks) => WaitAllCore(tasks.ToArray(), -1, default);
        public static void WaitAll(IEnumerable<Task> tasks, CancellationToken cancellationToken = default)
        {
            if (tasks == null) throw new ArgumentNullException("tasks");
            WaitAllCore(new List<Task>(tasks).ToArray(), -1, cancellationToken);
        }
        public static bool WaitAll(Task[] tasks, int millisecondsTimeout) => WaitAllCore(tasks, millisecondsTimeout, default);
        public static bool WaitAll(Task[] tasks, TimeSpan timeout) => WaitAllCore(tasks, TimeoutMilliseconds(timeout), default);
        public static void WaitAll(Task[] tasks, CancellationToken cancellationToken) => WaitAllCore(tasks, -1, cancellationToken);
        public static bool WaitAll(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken) => WaitAllCore(tasks, millisecondsTimeout, cancellationToken);

        private static int TimeoutMilliseconds(TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException("timeout");
            return (int)totalMilliseconds;
        }

        private static void CheckTasks(Task[] tasks)
        {
            if (tasks == null) throw new ArgumentNullException("tasks");
            foreach (Task task in tasks)
                if (task == null) throw new ArgumentException("The tasks array included at least one null element.", "tasks");
        }

        private static bool WaitAllCore(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckTasks(tasks);
            if (millisecondsTimeout < -1) throw new ArgumentOutOfRangeException("millisecondsTimeout");
            // A gate that completes (never faults) once every task has: the wait itself doesn't throw.
            Task gate = Task.WhenAll(tasks).ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (!Wait(gate, millisecondsTimeout, cancellationToken)) return false;
            List<Exception> exceptions = null;
            foreach (Task task in tasks)
            {
                if (task.IsFaulted) (exceptions ??= new List<Exception>()).AddRange(task.Exception.InnerExceptions);
                else if (task.IsCanceled) (exceptions ??= new List<Exception>()).Add(new TaskCanceledException(task));
            }
            if (exceptions != null) throw new AggregateException(exceptions);
            return true;
        }

        public static int WaitAny(params Task[] tasks) => WaitAnyCore(tasks, -1, default);
        public static int WaitAny(Task[] tasks, int millisecondsTimeout) => WaitAnyCore(tasks, millisecondsTimeout, default);
        public static int WaitAny(Task[] tasks, TimeSpan timeout) => WaitAnyCore(tasks, TimeoutMilliseconds(timeout), default);
        public static int WaitAny(Task[] tasks, CancellationToken cancellationToken) => WaitAnyCore(tasks, -1, cancellationToken);
        public static int WaitAny(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken) => WaitAnyCore(tasks, millisecondsTimeout, cancellationToken);

        private static int WaitAnyCore(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckTasks(tasks);
            if (millisecondsTimeout < -1) throw new ArgumentOutOfRangeException("millisecondsTimeout");
            for (int i = 0; i < tasks.Length; i++) if (tasks[i].IsCompleted) return i; // the first already done, in order
            // No tasks: nothing can complete, so the wait runs into the timeout (or the token).
            if (tasks.Length == 0) { Wait(new TaskCompletionSource().Task, millisecondsTimeout, cancellationToken); return -1; }
            Task<Task> any = Task.WhenAny(tasks);
            if (!Wait(any, millisecondsTimeout, cancellationToken)) return -1;
            return Array.IndexOf(tasks, any.Result);
        }

        // WaitAsync (Task.cs): the task itself when it is done or nothing could interrupt the wait, otherwise a
        // task completed by whichever comes first of the task, the timeout (TimeoutException) and the token.
        [ShadowInstance] public static Task WaitAsync(Task task, CancellationToken cancellationToken) => WaitAsync(task, new TimeSpan(-TimeSpan.TicksPerMillisecond), cancellationToken);
        [ShadowInstance] public static Task WaitAsync(Task task, TimeSpan timeout) => WaitAsync(task, timeout, default);

        [ShadowInstance]
        public static Task WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            uint ms = ValidateTimeout(timeout);
            if (task.IsCompleted || (!cancellationToken.CanBeCanceled && ms == uint.MaxValue)) return task;
            TaskCompletionSource tcs = new TaskCompletionSource();
            if (cancellationToken.IsCancellationRequested) { tcs.SetCanceled(); return tcs.Task; }
            if (ms == 0) { tcs.SetException(new TimeoutException()); return tcs.Task; }
            Action complete = () =>
            {
                if (task.IsCompleted)
                {
                    if (task.IsFaulted) tcs.TrySetException(task.Exception.InnerException);
                    else if (task.IsCanceled) tcs.TrySetCanceled();
                    else tcs.TrySetResult();
                }
                else if (cancellationToken.IsCancellationRequested) tcs.TrySetCanceled();
                else tcs.TrySetException(new TimeoutException());
            };
            OnFirst(task, ms, cancellationToken, complete);
            return tcs.Task;
        }

        /// <summary>Runs `complete` once, when `task` finishes or the wait times out or is canceled.</summary>
        internal static void OnFirst(Task task, uint ms, CancellationToken cancellationToken, Action complete)
        {
            Task delay = Task.Delay(ms == uint.MaxValue ? -1 : (int)Math.Min(ms, (uint)int.MaxValue), cancellationToken);
            ((Task)Task.WhenAny(task, delay)).ContinueWith(_ => complete(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        internal static uint ValidateTimeout(TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > 0xFFFFFFFE)
                throw new ArgumentOutOfRangeException("timeout", "The value needs to translate in milliseconds to -1 (signifying an infinite timeout), 0, or a positive integer less than or equal to the maximum allowed timer duration.");
            return (uint)totalMilliseconds;
        }

        // Wait with a timeout or a token (Task.cs): a bounded wait is a wait for whichever of the task and a
        // delay finishes first; once the task is done it reports like Wait() (AggregateException).
        [ShadowInstance] public static bool Wait(Task task, int millisecondsTimeout) => Wait(task, millisecondsTimeout, default);
        [ShadowInstance] public static void Wait(Task task, CancellationToken cancellationToken) => Wait(task, -1, cancellationToken);
        [ShadowInstance] public static bool Wait(Task task, TimeSpan timeout) => Wait(task, timeout, default);

        [ShadowInstance]
        public static bool Wait(Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException("timeout");
            cancellationToken.ThrowIfCancellationRequested();
            return Wait(task, (int)totalMilliseconds, cancellationToken);
        }

        [ShadowInstance]
        public static bool Wait(Task task, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (millisecondsTimeout < -1) throw new ArgumentOutOfRangeException("millisecondsTimeout");
            if (task.IsCompletedSuccessfully) return true;
            if (!task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (millisecondsTimeout == 0) return false;
                if (millisecondsTimeout != -1 || cancellationToken.CanBeCanceled)
                {
                    Task.WhenAny(task, Task.Delay(millisecondsTimeout, cancellationToken)).Wait();
                    if (!task.IsCompleted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }
                }
            }
            if (task.IsCanceled) cancellationToken.ThrowIfCancellationRequested();
            task.Wait();
            return true;
        }

        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task> continuationAction) =>
            ContinueWith(task, continuationAction, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task> continuationAction, CancellationToken cancellationToken) =>
            ContinueWith(task, continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task> continuationAction, TaskScheduler scheduler) =>
            ContinueWith(task, continuationAction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task> continuationAction, TaskContinuationOptions continuationOptions) =>
            ContinueWith(task, continuationAction, CancellationToken.None, continuationOptions, TaskScheduler.Current);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationAction(task), continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler, "continuationAction");

        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task, object> continuationAction, object state) =>
            ContinueWith(task, continuationAction, state, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task, object> continuationAction, object state, CancellationToken cancellationToken) =>
            ContinueWith(task, continuationAction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task, object> continuationAction, object state, TaskScheduler scheduler) =>
            ContinueWith(task, continuationAction, state, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task, object> continuationAction, object state, TaskContinuationOptions continuationOptions) =>
            ContinueWith(task, continuationAction, state, CancellationToken.None, continuationOptions, TaskScheduler.Current);
        [ShadowInstance] public static Task ContinueWith(Task task, Action<Task, object> continuationAction, object state, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationAction(task, state), continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler, "continuationAction");

        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, TResult> continuationFunction) =>
            ContinueWith(task, continuationFunction, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken) =>
            ContinueWith(task, continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, TResult> continuationFunction, TaskScheduler scheduler) =>
            ContinueWith(task, continuationFunction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            ContinueWith(task, continuationFunction, CancellationToken.None, continuationOptions, TaskScheduler.Current);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationFunction(task), continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);

        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, object, TResult> continuationFunction, object state) =>
            ContinueWith(task, continuationFunction, state, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, object, TResult> continuationFunction, object state, CancellationToken cancellationToken) =>
            ContinueWith(task, continuationFunction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, object, TResult> continuationFunction, object state, TaskScheduler scheduler) =>
            ContinueWith(task, continuationFunction, state, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, object, TResult> continuationFunction, object state, TaskContinuationOptions continuationOptions) =>
            ContinueWith(task, continuationFunction, state, CancellationToken.None, continuationOptions, TaskScheduler.Current);
        [ShadowInstance] public static Task<TResult> ContinueWith<TResult>(Task task, Func<Task, object, TResult> continuationFunction, object state, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationFunction(task, state), continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
    }

    /// <summary>Task&lt;TResult&gt;.ContinueWith: Intrinsics.cs forwards each overload here with all five arguments.</summary>
    internal static class TaskResultContinuations
    {
        /// <summary>Task&lt;TResult&gt;.WaitAsync (Intrinsics.cs forwards every overload; an infinite timeout is -1 ms).</summary>
        public static Task<TResult> WaitAsync<TResult>(Task<TResult> task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            uint ms = TaskMembers.ValidateTimeout(timeout);
            if (task.IsCompleted || (!cancellationToken.CanBeCanceled && ms == uint.MaxValue)) return task;
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
            if (cancellationToken.IsCancellationRequested) { tcs.SetCanceled(); return tcs.Task; }
            if (ms == 0) { tcs.SetException(new TimeoutException()); return tcs.Task; }
            Action complete = () =>
            {
                if (task.IsCompleted)
                {
                    if (task.IsFaulted) tcs.TrySetException(task.Exception.InnerException);
                    else if (task.IsCanceled) tcs.TrySetCanceled();
                    else tcs.TrySetResult(task.Result);
                }
                else if (cancellationToken.IsCancellationRequested) tcs.TrySetCanceled();
                else tcs.TrySetException(new TimeoutException());
            };
            TaskMembers.OnFirst(task, ms, cancellationToken, complete);
            return tcs.Task;
        }

        public static Task ContinueWithAction<TResult>(Task<TResult> task, Action<Task<TResult>> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationAction(task), continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler, "continuationAction");

        public static Task ContinueWithStateAction<TResult>(Task<TResult> task, Action<Task<TResult>, object> continuationAction, object state, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationAction(task, state), continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler, "continuationAction");

        public static Task<TNewResult> ContinueWithFunction<TResult, TNewResult>(Task<TResult> task, Func<Task<TResult>, TNewResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationFunction(task), continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);

        public static Task<TNewResult> ContinueWithStateFunction<TResult, TNewResult>(Task<TResult> task, Func<Task<TResult>, object, TNewResult> continuationFunction, object state, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskSupport.ContinueWith(task, () => continuationFunction(task, state), continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
    }
}
