// TaskCompletionSource(<TResult>).SetException / TrySetException(IEnumerable<Exception>): .NET's checks,
// then the runtime faults the task with all of them (runtime/async.c, csrt_task_try_fault_many). The
// source object is its task (Intrinsics.cs).
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    internal static class TaskExceptions
    {
        public static bool TrySetMany(object task, IEnumerable<Exception> exceptions)
        {
            if (exceptions == null) throw new ArgumentNullException("exceptions");
            var list = new List<Exception>();
            foreach (Exception exception in exceptions)
            {
                if (exception == null) throw new ArgumentException("The exceptions collection included at least one null element.", "exceptions");
                list.Add(exception);
            }
            if (list.Count == 0) throw new ArgumentException("The exceptions collection was empty.", "exceptions");
            return CoreIntrinsics.csrt_task_try_fault_many(task, list.ToArray());
        }

        public static void SetMany(object task, IEnumerable<Exception> exceptions)
        {
            if (!TrySetMany(task, exceptions))
                throw new InvalidOperationException("An attempt was made to transition a task to a final state when it had already completed.");
        }
    }
}

namespace CsharpcCoreLib
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// TaskCompletionSource(&lt;TResult&gt;).TrySetFromTask (#96): .NET's checks, then the completed task's outcome through the source's
    /// own TrySet* members (Intrinsics.cs): its result, all its exceptions, or its cancellation with the token it was canceled with.
    /// </summary>
    internal static class TaskCompletionFrom
    {
        private static void Check(Task completedTask)
        {
            if (completedTask == null) throw new ArgumentNullException("completedTask");
            if (!completedTask.IsCompleted) throw new ArgumentException("The provided task must have already completed.", "completedTask");
        }

        private static CancellationToken TokenOf(Task canceled)
        {
            try { canceled.GetAwaiter().GetResult(); }
            catch (OperationCanceledException e) { return e.CancellationToken; }
            return default;
        }

        public static bool TrySetFromPlainTask(TaskCompletionSource source, Task completedTask)
        {
            Check(completedTask);
            return completedTask.Status switch
            {
                TaskStatus.RanToCompletion => source.TrySetResult(),
                TaskStatus.Canceled => source.TrySetCanceled(TokenOf(completedTask)),
                TaskStatus.Faulted => source.TrySetException(completedTask.Exception.InnerExceptions),
                _ => false,
            };
        }

        public static bool TrySetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
        {
            Check(completedTask);
            return completedTask.Status switch
            {
                TaskStatus.RanToCompletion => source.TrySetResult(completedTask.Result),
                TaskStatus.Canceled => source.TrySetCanceled(TokenOf(completedTask)),
                TaskStatus.Faulted => source.TrySetException(completedTask.Exception.InnerExceptions),
                _ => false,
            };
        }
    }
}
