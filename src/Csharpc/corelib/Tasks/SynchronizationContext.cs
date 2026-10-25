// SynchronizationContext, ExecutionContext.Capture/Run and ThreadPool.QueueUserWorkItem.
//
// SynchronizationContext.Current is per thread, as in .NET. An `await` that captures the context (no
// ConfigureAwait(false)) of a derived type posts its continuation there: the C runtime keeps such a
// context in a thread-local of its own (csrt_tl_sync_context, runtime/async.c), and while one is set an
// await asks Capture below for the continuation to register, which Posts the coroutine's resumption.
// A plain `new SynchronizationContext()` behaves like none, as .NET's awaiters treat it.
//
// ExecutionContext is csharpc's execution-context flow (csrt_tl_flow, runtime/threads.c: the cultures
// that flow like AsyncLocals): Capture takes the current one and Run runs a callback with it.
#nullable disable
using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.SynchronizationContext")]
    public class SynchronizationContextImpl
    {
        [ThreadStatic] private static SynchronizationContext t_current;
        private static bool s_hookSet;

        public SynchronizationContextImpl() { }

        public static SynchronizationContext Current => t_current;

        public static unsafe void SetSynchronizationContext(SynchronizationContext syncContext)
        {
            t_current = syncContext;
            bool custom = syncContext != null && syncContext.GetType() != typeof(SynchronizationContext);
            if (custom && !s_hookSet)
            {
                csrt_sync_context_hook((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&Capture);
                s_hookSet = true;
            }
            csrt_sync_context_set(custom ? CoreIntrinsics.ObjectToPointer(syncContext) : IntPtr.Zero);
        }

        /// <summary>The context awaits post to on this thread (a derived type), or null.</summary>
        internal static SynchronizationContext CurrentCustom
        {
            get
            {
                SynchronizationContext current = t_current;
                return current != null && current.GetType() != typeof(SynchronizationContext) ? current : null;
            }
        }

        public virtual void Send(SendOrPostCallback d, object state) => d(state);

        public virtual void Post(SendOrPostCallback d, object state) =>
            ThreadPoolMembers.Queue(() => d(state), flow: true);

        public virtual SynchronizationContext CreateCopy() => new SynchronizationContext();
        public virtual void OperationStarted() { }
        public virtual void OperationCompleted() { }

        private bool _requireWaitNotification;
        protected void SetWaitNotificationRequired() { _requireWaitNotification = true; }
        public bool IsWaitNotificationRequired() => _requireWaitNotification;

        /// <summary>
        /// The runtime's hook (await with a captured context): the Action continuation that posts the
        /// resumption of coroutine `frame` (with execution-context flow `flow`), or of `action` (an IL
        /// awaiter's OnCompleted continuation), to `context`.
        /// </summary>
        [UnmanagedCallersOnly]
        private static IntPtr Capture(IntPtr context, IntPtr frame, IntPtr action, IntPtr flow)
        {
            var continuation = new SyncContextContinuation(
                (SynchronizationContext)CoreIntrinsics.PointerToObject(context), frame,
                action == IntPtr.Zero ? null : (Action)CoreIntrinsics.PointerToObject(action), flow);
            return CoreIntrinsics.ObjectToPointer(new Action(continuation.Post));
        }

        [DllImport("__Internal", EntryPoint = "csrt_sync_context_set")]
        private static extern void csrt_sync_context_set(IntPtr context);

        [DllImport("__Internal", EntryPoint = "csrt_sync_context_hook")]
        private static extern void csrt_sync_context_hook(IntPtr hook);

        // ---- async void (FunctionCodeGen.Async.cs; AsyncVoidMethodBuilder in .NET) ----

        /// <summary>An async void method starts: the context it reports to (OperationStarted), or null.</summary>
        internal static object AsyncVoidStarted()
        {
            SynchronizationContext context = CurrentCustom;
            context?.OperationStarted();
            return context;
        }

        internal static void AsyncVoidCompleted(object context)
        {
            if (context == null) return;
            try { ((SynchronizationContext)context).OperationCompleted(); }
            catch (Exception e) { ThrowAsync(e, null); }
        }

        /// <summary>An exception escaped an async void method: rethrown on its context (or the thread pool).</summary>
        internal static void AsyncVoidFailed(object context, Exception exception)
        {
            if (context == null) CoreIntrinsics.UnhandledException(exception);
            try { ThrowAsync(exception, (SynchronizationContext)context); }
            finally { AsyncVoidCompleted(context); }
        }

        // Task.ThrowAsync
        private static void ThrowAsync(Exception exception, SynchronizationContext targetContext)
        {
            var edi = ExceptionDispatchInfo.Capture(exception);
            if (targetContext != null)
            {
                try
                {
                    targetContext.Post(static state => ((ExceptionDispatchInfo)state).Throw(), edi);
                    return;
                }
                catch (Exception postException)
                {
                    edi = ExceptionDispatchInfo.Capture(new AggregateException(exception, postException));
                }
            }
            ThreadPoolMembers.Queue(() => edi.Throw(), flow: true);
        }
    }

    internal sealed class SyncContextContinuation
    {
        private static readonly SendOrPostCallback s_resume = static state => ((SyncContextContinuation)state).Resume();

        private readonly SynchronizationContext _context;
        private readonly IntPtr _frame;
        private readonly Action _action;
        private readonly IntPtr _flow;

        internal SyncContextContinuation(SynchronizationContext context, IntPtr frame, Action action, IntPtr flow)
        {
            _context = context;
            _frame = frame;
            _action = action;
            _flow = flow;
        }

        internal void Post() => _context.Post(s_resume, this);

        private void Resume()
        {
            if (_action != null)
            {
                _action();
                return;
            }
            IntPtr saved = ExecutionContextImpl.FlowGet();
            ExecutionContextImpl.FlowSet(_flow);
            try { csrt_coro_resume(_frame); }
            finally { ExecutionContextImpl.FlowSet(saved); }
        }

        [DllImport("__Internal", EntryPoint = "csrt_coro_resume")]
        private static extern void csrt_coro_resume(IntPtr frame);
    }

    [Shadows("System.Threading.ExecutionContext")]
    public sealed class ExecutionContextImpl : IDisposable
    {
        private static readonly ExecutionContextImpl s_default = new ExecutionContextImpl(IntPtr.Zero);
        private readonly IntPtr _flow;

        private ExecutionContextImpl(IntPtr flow) { _flow = flow; }

        public static ExecutionContext Capture()
        {
            IntPtr flow = FlowGet();
            return (ExecutionContext)(object)(flow == IntPtr.Zero ? s_default : new ExecutionContextImpl(flow));
        }

        public static void Run(ExecutionContext executionContext, ContextCallback callback, object state)
        {
            if (executionContext == null) throw new InvalidOperationException("Cannot call Set on a null context");
            // .NET's RunInternal also puts back the thread's SynchronizationContext.
            IntPtr saved = FlowGet();
            SynchronizationContext savedContext = SynchronizationContext.Current;
            FlowSet(((ExecutionContextImpl)(object)executionContext)._flow);
            try { callback(state); }
            finally
            {
                FlowSet(saved);
                if (SynchronizationContext.Current != savedContext) SynchronizationContext.SetSynchronizationContext(savedContext);
            }
        }

        public static void Restore(ExecutionContext executionContext)
        {
            if (executionContext == null) throw new InvalidOperationException("Cannot call Set on a null context");
            FlowSet(((ExecutionContextImpl)(object)executionContext)._flow);
        }

        public static bool IsFlowSuppressed() => false;

        public ExecutionContext CreateCopy() => (ExecutionContext)(object)this;
        public void Dispose() { }

        [DllImport("__Internal", EntryPoint = "csrt_flow_get")]
        internal static extern IntPtr FlowGet();

        [DllImport("__Internal", EntryPoint = "csrt_flow_set")]
        internal static extern void FlowSet(IntPtr flow);
    }

    [ShadowMembers("System.Threading.ThreadPool")]
    internal static class ThreadPoolMembers
    {
        [DllImport("__Internal", EntryPoint = "csrt_queue_action")]
        private static extern void csrt_queue_action(IntPtr action);

        /// <summary>Runs `action` on the pool (an exception escaping it is unhandled); `flow`: with the current execution context.</summary>
        internal static void Queue(Action action, bool flow)
        {
            if (flow)
            {
                csrt_queue_action(CoreIntrinsics.ObjectToPointer(action));
                return;
            }
            IntPtr saved = ExecutionContextImpl.FlowGet();
            ExecutionContextImpl.FlowSet(IntPtr.Zero);
            try { csrt_queue_action(CoreIntrinsics.ObjectToPointer(action)); }
            finally { ExecutionContextImpl.FlowSet(saved); }
        }

        public static bool QueueUserWorkItem(WaitCallback callBack)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            Queue(() => callBack(null), flow: true);
            return true;
        }

        public static bool QueueUserWorkItem(WaitCallback callBack, object state)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            Queue(() => callBack(state), flow: true);
            return true;
        }

        public static bool QueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            Queue(() => callBack(state), flow: true);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem(WaitCallback callBack, object state)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            Queue(() => callBack(state), flow: false);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            Queue(() => callBack(state), flow: false);
            return true;
        }
    }
}
