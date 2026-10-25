// System.Threading.ExecutionContext over csharpc's execution-context flow (ExecutionFlow.cs): Capture,
// Run and Restore move the immutable flow object around, as .NET's ExecutionContext does with its
// AsyncLocal map; SuppressFlow makes the runtime's capture points (queued work, Thread.Start, awaits) carry
// no flow until RestoreFlow. System.Threading.SynchronizationContext: csharpc's awaits never post to one, so there is no
// current context and installing one is an explicit NotSupportedException; the base class's Post/Send
// work as in .NET (the thread pool, a direct call).
#nullable disable
using System;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.ExecutionContext")]
    public sealed class ExecutionContextImpl : IDisposable
    {
        internal readonly ExecutionFlow Flow;
        private static ExecutionContextImpl s_default;

        private ExecutionContextImpl(ExecutionFlow flow) { Flow = flow; }

        private static ExecutionContextImpl Default => s_default ??= new ExecutionContextImpl(null);

        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_flow_suppressed")]
        private static extern bool FlowSuppressed();

        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_flow_suppress")]
        internal static extern void SetFlowSuppressed(bool suppressed);

        public static ExecutionContextImpl Capture()
        {
            if (FlowSuppressed()) return null;
            ExecutionFlow flow = ExecutionFlow.Current;
            return flow == null ? Default : new ExecutionContextImpl(flow);
        }

        public ExecutionContextImpl CreateCopy() => this;

        public static bool IsFlowSuppressed() => FlowSuppressed();

        public static AsyncFlowControl SuppressFlow()
        {
            if (FlowSuppressed()) throw new InvalidOperationException("Context flow is already suppressed.");
            SetFlowSuppressed(true);
            return (AsyncFlowControl)(object)new AsyncFlowControlImpl(Thread.CurrentThread);
        }

        public static void RestoreFlow()
        {
            if (!FlowSuppressed()) throw new InvalidOperationException("Cannot restore context flow when it is not suppressed.");
            SetFlowSuppressed(false);
        }

        public static void Run(ExecutionContextImpl executionContext, ContextCallback callback, object state)
        {
            if (executionContext == null) throw new InvalidOperationException("Cannot call Set on a null context");
            ExecutionFlow saved = ExecutionFlow.Current;
            ExecutionFlow.Current = executionContext.Flow;
            try { callback(state); }
            finally { ExecutionFlow.Current = saved; }
        }

        public static void Restore(ExecutionContextImpl executionContext)
        {
            if (executionContext == null) throw new InvalidOperationException("Cannot call Set on a null context");
            ExecutionFlow.Current = executionContext.Flow;
        }

        public void Dispose() { }
    }

    [Shadows("System.Threading.AsyncFlowControl")]
    public struct AsyncFlowControlImpl : IDisposable, IEquatable<AsyncFlowControlImpl>
    {
        private Thread _thread;

        internal AsyncFlowControlImpl(Thread thread) { _thread = thread; }

        public void Undo()
        {
            if (_thread == null) throw new InvalidOperationException("AsyncFlowControl object can be used only once to call Undo().");
            if (Thread.CurrentThread != _thread) throw new InvalidOperationException("AsyncFlowControl object must be used on the thread where it was created.");
            if (!ExecutionContextImpl.IsFlowSuppressed()) throw new InvalidOperationException("AsyncFlowControl objects can be used to restore flow only on a Context that had its flow suppressed.");
            _thread = null;
            ExecutionContextImpl.SetFlowSuppressed(false);
        }

        public void Dispose() => Undo();

        public override bool Equals(object obj) => obj is AsyncFlowControlImpl other && Equals(other);
        public bool Equals(AsyncFlowControlImpl obj) => _thread == obj._thread;
        public override int GetHashCode() => _thread?.GetHashCode() ?? 0;
        public static bool operator ==(AsyncFlowControlImpl a, AsyncFlowControlImpl b) => a.Equals(b);
        public static bool operator !=(AsyncFlowControlImpl a, AsyncFlowControlImpl b) => !(a == b);
    }

    [Shadows("System.Threading.SynchronizationContext")]
    public class SynchronizationContextImpl
    {
        public SynchronizationContextImpl() { }

        public static SynchronizationContextImpl Current => null;

        public static void SetSynchronizationContext(SynchronizationContextImpl syncContext)
        {
            if (syncContext != null)
                throw new NotSupportedException("Installing a SynchronizationContext is not supported by csharpc yet (awaits never post to one).");
        }

        public virtual void Post(SendOrPostCallback d, object state) => ThreadPool.QueueUserWorkItem(static s => s.d(s.state), (d, state), preferLocal: false);

        public virtual void Send(SendOrPostCallback d, object state) => d(state);

        public virtual SynchronizationContextImpl CreateCopy() => new SynchronizationContextImpl();

        public virtual void OperationStarted() { }

        public virtual void OperationCompleted() { }
    }
}
