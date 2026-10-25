// SynchronizationContext (ExecutionContext, the flow and the thread pool: ExecutionContexts.cs, ExecutionFlow.cs).
//
// SynchronizationContext.Current is per thread, as in .NET. An `await` that captures the context (no
// ConfigureAwait(false)) of a derived type posts its continuation there: the C runtime keeps such a
// context in a thread-local of its own (csrt_tl_sync_context, runtime/async.c), and while one is set an
// await asks Capture below for the continuation to register, which Posts the coroutine's resumption.
// A plain `new SynchronizationContext()` behaves like none, as .NET's awaiters treat it.
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
            ThreadPool.QueueUserWorkItem(static s => s.d(s.state), (d, state), preferLocal: false);

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
            ThreadPool.QueueUserWorkItem(static e => e.Throw(), edi, preferLocal: false);
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
            IntPtr saved = FlowGet();
            FlowSet(_flow);
            try { csrt_coro_resume(_frame); }
            finally { FlowSet(saved); }
        }

        [DllImport("__Internal", EntryPoint = "csrt_flow_get")]
        private static extern IntPtr FlowGet();

        [DllImport("__Internal", EntryPoint = "csrt_flow_set")]
        private static extern void FlowSet(IntPtr flow);

        [DllImport("__Internal", EntryPoint = "csrt_coro_resume")]
        private static extern void csrt_coro_resume(IntPtr frame);
    }
}
