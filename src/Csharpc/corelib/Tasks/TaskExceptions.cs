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
