// TaskFactory.ContinueWhenAll / ContinueWhenAny and TaskFactory.FromAsync (TaskFactory.cs,
// TaskFactory.Continuations.cs in dotnet/runtime): the work behind the thin public overloads in
// TaskFactoryMembers.cs. A multi-task continuation is a gate task (completed when all, or the first, of a
// private copy of the antecedents has completed, whatever their outcome) that TaskSupport.ContinueWith
// hooks like any continuation, so scheduler, token, LazyCancellation and the status transitions are the
// ones of Task.ContinueWith. Argument checks keep .NET's exceptions, messages and (different for All and Any)
// order, probed against the runtime (samples 1200-1203).
#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class TaskFactoryOps
    {
        // ---- ContinueWhenAll / ContinueWhenAny ---------------------------------------------------------

        // ContinueWhenAll checks: continuation, options, tasks, scheduler, then the tasks' contents;
        // ContinueWhenAny: continuation, options, tasks, no tasks, scheduler, then null elements.
        private static T[] CheckAndCopy<T>(object continuation, string continuationName, TaskContinuationOptions options, T[] tasks, TaskSchedulerImpl scheduler, bool any)
            where T : Task
        {
            if (continuation == null) throw new ArgumentNullException(continuationName);
            TaskFactoryChecks.CheckMultiTaskContinuationOptions(options);
            if (tasks == null) throw new ArgumentNullException("tasks");
            if (any && tasks.Length == 0) throw new ArgumentException("The tasks argument contains no tasks.", "tasks");
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            if (!any && tasks.Length == 0) throw new ArgumentException("The tasks argument contains no tasks.", "tasks");
            var copy = new T[tasks.Length];
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i] == null) throw new ArgumentException("The tasks argument included a null value.", "tasks");
                copy[i] = tasks[i];
            }
            return copy;
        }

        // Completes (never faults) once every task has, in whatever state.
        private static Task AllGate<T>(T[] copy) where T : Task
        {
            var gate = new TaskCompletionSource();
            int remaining = copy.Length;
            foreach (T item in copy)
            {
                Task task = item;
                task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
                {
                    if (Interlocked.Decrement(ref remaining) == 0) gate.TrySetResult();
                });
            }
            return gate.Task;
        }

        // Completes with the first task to complete; among tasks that already have, the first in order.
        private static Task<Task> AnyGate<T>(T[] copy) where T : Task
        {
            var gate = new TaskCompletionSource<Task>();
            foreach (T item in copy)
            {
                Task task = item;
                if (task.IsCompleted) { gate.TrySetResult(task); break; }
                task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() => gate.TrySetResult(task));
            }
            return gate.Task;
        }

        public static Task WhenAllAction(Task[] tasks, Action<Task[]> action, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task[] copy = CheckAndCopy(action, "continuationAction", options, tasks, scheduler, false);
            return TaskSupport.ContinueWith(AllGate(copy), () => action(copy), action, token, options, scheduler, "continuationAction");
        }

        public static Task<TResult> WhenAllFunc<TResult>(Task[] tasks, Func<Task[], TResult> function, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task[] copy = CheckAndCopy(function, "continuationFunction", options, tasks, scheduler, false);
            return TaskSupport.ContinueWith<TResult>(AllGate(copy), () => function(copy), function, token, options, scheduler);
        }

        public static Task WhenAllActionOf<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> action, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task<TAntecedentResult>[] copy = CheckAndCopy(action, "continuationAction", options, tasks, scheduler, false);
            return TaskSupport.ContinueWith(AllGate(copy), () => action(copy), action, token, options, scheduler, "continuationAction");
        }

        public static Task<TResult> WhenAllFuncOf<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> function, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task<TAntecedentResult>[] copy = CheckAndCopy(function, "continuationFunction", options, tasks, scheduler, false);
            return TaskSupport.ContinueWith<TResult>(AllGate(copy), () => function(copy), function, token, options, scheduler);
        }

        public static Task WhenAnyAction(Task[] tasks, Action<Task> action, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task[] copy = CheckAndCopy(action, "continuationAction", options, tasks, scheduler, true);
            Task<Task> gate = AnyGate(copy);
            return TaskSupport.ContinueWith(gate, () => action(gate.Result), action, token, options, scheduler, "continuationAction");
        }

        public static Task<TResult> WhenAnyFunc<TResult>(Task[] tasks, Func<Task, TResult> function, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task[] copy = CheckAndCopy(function, "continuationFunction", options, tasks, scheduler, true);
            Task<Task> gate = AnyGate(copy);
            return TaskSupport.ContinueWith<TResult>(gate, () => function(gate.Result), function, token, options, scheduler);
        }

        public static Task WhenAnyActionOf<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> action, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task<TAntecedentResult>[] copy = CheckAndCopy(action, "continuationAction", options, tasks, scheduler, true);
            Task<Task> gate = AnyGate(copy);
            return TaskSupport.ContinueWith(gate, () => action((Task<TAntecedentResult>)gate.Result), action, token, options, scheduler, "continuationAction");
        }

        public static Task<TResult> WhenAnyFuncOf<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> function, CancellationToken token, TaskContinuationOptions options, TaskSchedulerImpl scheduler)
        {
            Task<TAntecedentResult>[] copy = CheckAndCopy(function, "continuationFunction", options, tasks, scheduler, true);
            Task<Task> gate = AnyGate(copy);
            return TaskSupport.ContinueWith<TResult>(gate, () => function((Task<TAntecedentResult>)gate.Result), function, token, options, scheduler);
        }

        // ---- FromAsync ---------------------------------------------------------------------------------

        // TaskFactory.CheckFromAsyncOptions: Begin/End pairs name the two options a promise can't honor;
        // the IAsyncResult overloads only get the promise task's own "AttachedToParent only" check.
        private static void CheckOptions(TaskCreationOptions options, bool hasBeginMethod)
        {
            if (hasBeginMethod)
            {
                if ((options & TaskCreationOptions.LongRunning) != 0)
                    throw new ArgumentOutOfRangeException("creationOptions", "It is invalid to specify TaskCreationOptions.LongRunning in calls to FromAsync.");
                if ((options & TaskCreationOptions.PreferFairness) != 0)
                    throw new ArgumentOutOfRangeException("creationOptions", "It is invalid to specify TaskCreationOptions.PreferFairness in calls to FromAsync.");
            }
            if ((options & ~TaskCreationOptions.AttachedToParent) != 0) throw new ArgumentOutOfRangeException("creationOptions");
        }

        // The IAsyncResult overloads: the end method is the body of a task that runs inline when the
        // operation is complete (the scheduler is asked through TryExecuteTaskInline, else QueueTask), or once
        // its wait handle is signaled. An OperationCanceledException from it cancels the task.
        public static Task FromAsyncResult(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions options, TaskSchedulerImpl scheduler)
        {
            if (asyncResult == null) throw new ArgumentNullException("asyncResult");
            if (endMethod == null) throw new ArgumentNullException("endMethod");
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            CheckOptions(options, false);
            var tcs = new TaskCompletionSource();
            TaskSupport.AttachToParent(tcs.Task, options);
            TaskState.Set(tcs.Task, asyncResult.AsyncState, options);
            Action work = TaskSupport.Work(() =>
            {
                try { endMethod(asyncResult); }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
            }, tcs, default(CancellationToken), options, scheduler);
            RunWhenComplete(asyncResult, tcs.Task, work, scheduler, e => tcs.TrySetException(e));
            return tcs.Task;
        }

        public static Task<TResult> FromAsyncResult<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions options, TaskSchedulerImpl scheduler)
        {
            if (asyncResult == null) throw new ArgumentNullException("asyncResult");
            if (endMethod == null) throw new ArgumentNullException("endMethod");
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            CheckOptions(options, false);
            var tcs = new TaskCompletionSource<TResult>();
            TaskSupport.AttachToParent(tcs.Task, options);
            TaskState.Set(tcs.Task, asyncResult.AsyncState, options);
            Action work = TaskSupport.Work<TResult>(() =>
            {
                try { return endMethod(asyncResult); }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); return default(TResult); }
            }, tcs, default(CancellationToken), options, scheduler);
            RunWhenComplete(asyncResult, tcs.Task, work, scheduler, e => tcs.TrySetException(e));
            return tcs.Task;
        }

        private static void RunWhenComplete(IAsyncResult asyncResult, Task task, Action work, TaskSchedulerImpl scheduler, Action<Exception> fail)
        {
            if (asyncResult.IsCompleted)
            {
                try { scheduler.RunInline(task, work); }
                catch (Exception e) { fail(e); }
                return;
            }
            ThreadPool.RegisterWaitForSingleObject(asyncResult.AsyncWaitHandle, (state, timedOut) =>
            {
                try { scheduler.RunInline(task, work); }
                catch (Exception e) { fail(e); }
            }, null, -1, true);
        }

        // Begin methods with arguments, bound to them (the null check comes before the end method's, as in .NET).
        public static Func<AsyncCallback, object, IAsyncResult> Bind<TArg1>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, TArg1 arg1)
        {
            if (beginMethod == null) throw new ArgumentNullException("beginMethod");
            return (callback, state) => beginMethod(arg1, callback, state);
        }

        public static Func<AsyncCallback, object, IAsyncResult> Bind<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod, TArg1 arg1, TArg2 arg2)
        {
            if (beginMethod == null) throw new ArgumentNullException("beginMethod");
            return (callback, state) => beginMethod(arg1, arg2, callback, state);
        }

        public static Func<AsyncCallback, object, IAsyncResult> Bind<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3)
        {
            if (beginMethod == null) throw new ArgumentNullException("beginMethod");
            return (callback, state) => beginMethod(arg1, arg2, arg3, callback, state);
        }

        // The Begin/End overloads: Begin runs now (its exception propagates), and the promise completes from
        // the callback, or right after Begin returns when the operation completed synchronously (a callback
        // that reports CompletedSynchronously is ignored, so the end method runs once). The end method runs
        // wherever that happens, not on a scheduler.
        public static Task FromAsyncBeginEnd(Func<AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object state, TaskCreationOptions options)
        {
            if (beginMethod == null) throw new ArgumentNullException("beginMethod");
            if (endMethod == null) throw new ArgumentNullException("endMethod");
            CheckOptions(options, true);
            var tcs = new TaskCompletionSource();
            TaskSupport.AttachToParent(tcs.Task, options);
            TaskState.Set(tcs.Task, state, options);
            int completed = 0;
            Action<IAsyncResult> complete = iar =>
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                try { endMethod(iar); tcs.TrySetResult(); }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception e) { tcs.TrySetException(e); }
            };
            IAsyncResult asyncResult = beginMethod(iar => { if (!iar.CompletedSynchronously) complete(iar); }, state);
            if (asyncResult.CompletedSynchronously) complete(asyncResult);
            return tcs.Task;
        }

        public static Task<TResult> FromAsyncBeginEnd<TResult>(Func<AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object state, TaskCreationOptions options)
        {
            if (beginMethod == null) throw new ArgumentNullException("beginMethod");
            if (endMethod == null) throw new ArgumentNullException("endMethod");
            CheckOptions(options, true);
            var tcs = new TaskCompletionSource<TResult>();
            TaskSupport.AttachToParent(tcs.Task, options);
            TaskState.Set(tcs.Task, state, options);
            int completed = 0;
            Action<IAsyncResult> complete = iar =>
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                try { tcs.TrySetResult(endMethod(iar)); }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception e) { tcs.TrySetException(e); }
            };
            IAsyncResult asyncResult = beginMethod(iar => { if (!iar.CompletedSynchronously) complete(iar); }, state);
            if (asyncResult.CompletedSynchronously) complete(asyncResult);
            return tcs.Task;
        }
    }
}
