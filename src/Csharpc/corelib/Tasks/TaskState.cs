// What a runtime task (src/runtime/async.c) doesn't have a field for: Task.AsyncState and
// Task.CreationOptions (only for the tasks created with a state or non-default options, in a
// ConditionalWeakTable: the common task pays nothing), the IAsyncResult view of a task (Task implements it:
// AsyncWaitHandle is a lazily created ManualResetEvent set when the task completes), and Task.Dispose.
// Called from Intrinsics.cs (the properties, the TaskCompletionSource constructors) and, for IAsyncResult,
// through the slot table csharpc registers for Task (ModuleCodeGen.TaskAsyncResultSlots).
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class TaskState
    {
        private sealed class Info
        {
            internal object State;
            internal TaskCreationOptions Options;
            internal ManualResetEvent WaitHandle;
        }

        private static readonly ConditionalWeakTable<Task, Info> s_info = new ConditionalWeakTable<Task, Info>();

        [DllImport("__Internal", EntryPoint = "csrt_task_denies_children")]
        private static extern bool DeniesChildren(IntPtr task);

        /// <summary>Records the state and creation options of a task just created (nothing for the defaults).</summary>
        internal static void Set(Task task, object state, TaskCreationOptions options)
        {
            if (state == null && options == TaskCreationOptions.None) return;
            Info info = s_info.GetValue(task, static _ => new Info());
            info.State = state;
            info.Options = options;
        }

        // TaskCompletionSource(object state, TaskCreationOptions): only AttachedToParent and
        // RunContinuationsAsynchronously are legal for a promise-style task (Task's constructor).
        internal static void Promise(Task task, object state, TaskCreationOptions options)
        {
            if ((options & ~(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously)) != 0)
                throw new ArgumentOutOfRangeException("creationOptions");
            TaskSupport.AttachToParent(task, options);
            Set(task, state, options);
        }

        public static object AsyncState(Task task) => s_info.TryGetValue(task, out Info info) ? info.State : null;

        public static TaskCreationOptions CreationOptions(Task task) =>
            s_info.TryGetValue(task, out Info info) ? info.Options
            : DeniesChildren(CoreIntrinsics.ObjectToPointer(task)) ? TaskCreationOptions.DenyChildAttach : TaskCreationOptions.None;

        public static void Dispose(Task task)
        {
            if (!task.IsCompleted)
                throw new InvalidOperationException("A task may only be disposed if it is in a completion state (RanToCompletion, Faulted or Canceled).");
        }

        // ---- the IAsyncResult slots of a task: (object self, ...) like BoxedConvertible's -------------------

        private static ManualResetEvent WaitHandleOf(Task task)
        {
            Info info = s_info.GetValue(task, static _ => new Info());
            lock (info)
            {
                if (info.WaitHandle != null) return info.WaitHandle;
                // One read: a task that completes between two reads would leave the handle unset with no
                // continuation to set it (a WaitOne that only times out).
                bool completed = task.IsCompleted;
                var handle = new ManualResetEvent(completed);
                info.WaitHandle = handle;
                if (!completed) task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() => handle.Set());
                return handle;
            }
        }

        public static bool get_IsCompleted(object self) => ((Task)self).IsCompleted;
        public static object get_AsyncState(object self) => AsyncState((Task)self);
        public static WaitHandle get_AsyncWaitHandle(object self) => WaitHandleOf((Task)self);

        // IAsyncResult.CompletedSynchronously of a Task is false for every task in .NET.
        public static bool get_CompletedSynchronously(object self) => false;
    }
}
