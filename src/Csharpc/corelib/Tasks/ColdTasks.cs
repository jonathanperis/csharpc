// new Task(...) / new Task<TResult>(...) (created, not started) with Start and RunSynchronously, and
// Task.CurrentId. A created task is a TaskCompletionSource's task the runtime reports as Created
// (TF_CREATED, runtime/async.c) whose work waits here until Start queues it to a scheduler (TaskSupport.Work:
// the same body as Task.Factory.StartNew) or RunSynchronously runs it on the calling thread. A token
// canceled before the start cancels the task, as in .NET.
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class ColdTasks
    {
        private sealed class Pending
        {
            internal Func<TaskSchedulerImpl, Action> Work;
            internal Action Cancel;
            internal CancellationTokenRegistration Registration;
        }

        private static readonly Dictionary<Task, Pending> s_pending = new Dictionary<Task, Pending>();

        [DllImport("__Internal", EntryPoint = "csrt_task_mark_created")]
        private static extern void MarkCreated(IntPtr task);

        [DllImport("__Internal", EntryPoint = "csrt_task_clear_created")]
        private static extern void ClearCreated(IntPtr task);

        [DllImport("__Internal", EntryPoint = "csrt_task_current_id")]
        private static extern int CurrentIdRaw();

        [DllImport("__Internal", EntryPoint = "csrt_task_swap_current")]
        internal static extern IntPtr SwapCurrent(IntPtr task);

        internal static int? CurrentId => CurrentIdRaw() is var id && id != 0 ? id : null;

        private const TaskCreationOptions ValidOptions = TaskCreationOptions.AttachedToParent | TaskCreationOptions.DenyChildAttach
            | TaskCreationOptions.HideScheduler | TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness
            | TaskCreationOptions.RunContinuationsAsynchronously;

        private static void CheckOptions(TaskCreationOptions options)
        {
            if ((options & ~ValidOptions) != 0) throw new ArgumentOutOfRangeException("creationOptions");
        }

        public static Task Create(Action action, CancellationToken token, TaskCreationOptions options)
        {
            if (action == null) throw new ArgumentNullException("action");
            return Build(action, token, options);
        }

        public static Task CreateWithState(Action<object> action, object state, CancellationToken token, TaskCreationOptions options)
        {
            if (action == null) throw new ArgumentNullException("action");
            return Build(() => action(state), token, options);
        }

        private static Task Build(Action body, CancellationToken token, TaskCreationOptions options)
        {
            CheckOptions(options);
            var tcs = new TaskCompletionSource();
            TaskSupport.AttachToParent(tcs.Task, options);
            return Register(tcs.Task, scheduler => TaskSupport.Work(body, tcs, token, options, scheduler), () => tcs.TrySetCanceled(), token);
        }

        public static Task<TResult> CreateFunc<TResult>(Func<TResult> function, CancellationToken token, TaskCreationOptions options)
        {
            if (function == null) throw new ArgumentNullException("function");
            return BuildFunc(function, token, options);
        }

        public static Task<TResult> CreateFuncWithState<TResult>(Func<object, TResult> function, object state, CancellationToken token, TaskCreationOptions options)
        {
            if (function == null) throw new ArgumentNullException("function");
            return BuildFunc(() => function(state), token, options);
        }

        private static Task<TResult> BuildFunc<TResult>(Func<TResult> body, CancellationToken token, TaskCreationOptions options)
        {
            CheckOptions(options);
            var tcs = new TaskCompletionSource<TResult>();
            TaskSupport.AttachToParent(tcs.Task, options);
            Register(tcs.Task, scheduler => TaskSupport.Work(body, tcs, token, options, scheduler), () => tcs.TrySetCanceled(), token);
            return tcs.Task;
        }

        private static Task Register(Task task, Func<TaskSchedulerImpl, Action> work, Action cancel, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                cancel();
                return task;
            }
            MarkCreated(CoreIntrinsics.ObjectToPointer(task));
            var pending = new Pending { Work = work, Cancel = cancel };
            lock (s_pending) s_pending[task] = pending;
            // Canceled before it starts: the task is canceled (and can no longer be started).
            if (token.CanBeCanceled)
                pending.Registration = token.Register(() =>
                {
                    if (Take(task) is { } p)
                    {
                        ClearCreated(CoreIntrinsics.ObjectToPointer(task));
                        p.Cancel();
                    }
                });
            return task;
        }

        private static Pending Take(Task task)
        {
            lock (s_pending) return s_pending.Remove(task, out Pending pending) ? pending : null;
        }

        private static Pending TakeForStart(Task task, string startedMessage, string promiseMessage)
        {
            if (Take(task) is { } pending)
            {
                pending.Registration.Dispose();
                return pending;
            }
            throw new InvalidOperationException(task.Status is TaskStatus.WaitingToRun or TaskStatus.Running ? startedMessage : promiseMessage);
        }

        public static void Start(Task task) => Start(task, TaskScheduler.Current);

        public static void Start(Task task, TaskScheduler scheduler)
        {
            if (task.IsCompleted) throw new InvalidOperationException("Start may not be called on a task that has completed.");
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            Pending pending = TakeForStart(task, "Start may not be called on a task that was already started.",
                "Start may not be called on a promise-style task.");
            var impl = (TaskSchedulerImpl)(object)scheduler;
            impl.Schedule(task, pending.Work(impl));
        }

        public static void RunSynchronously(Task task) => RunSynchronously(task, TaskScheduler.Current);

        public static void RunSynchronously(Task task, TaskScheduler scheduler)
        {
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            if (task.IsCompleted) throw new InvalidOperationException("RunSynchronously may not be called on a task that has already completed.");
            Pending pending = TakeForStart(task, "RunSynchronously may not be called on a task that was already started.",
                "RunSynchronously may not be called on a task not bound to a delegate, such as the task returned from an asynchronous method.");
            var impl = (TaskSchedulerImpl)(object)scheduler;
            TaskSupport.MarkQueued(task);
            // .NET runs it inline when the scheduler lets it (the default one does); its exception stays in the task.
            pending.Work(impl)();
        }
    }

    [ShadowMembers("System.Threading.Tasks.Task")]
    internal static class ColdTaskMembers
    {
        [ShadowInstance] public static void Start(Task task) => ColdTasks.Start(task);
        [ShadowInstance] public static void Start(Task task, TaskScheduler scheduler) => ColdTasks.Start(task, scheduler);
        [ShadowInstance] public static void RunSynchronously(Task task) => ColdTasks.RunSynchronously(task);
        [ShadowInstance] public static void RunSynchronously(Task task, TaskScheduler scheduler) => ColdTasks.RunSynchronously(task, scheduler);
        public static int? CurrentId => ColdTasks.CurrentId;
    }
}
