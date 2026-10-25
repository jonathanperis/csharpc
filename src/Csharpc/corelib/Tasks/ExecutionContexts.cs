// System.Threading.ExecutionContext over csharpc's execution-context flow (ExecutionFlow.cs): Capture,
// Run and Restore move the immutable flow object around, as .NET's ExecutionContext does with its
// AsyncLocal map. Suppressing the flow isn't supported yet (the runtime captures the flow whenever work is
// queued). System.Threading.SynchronizationContext: csharpc's awaits never post to one, so there is no
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

        public static ExecutionContextImpl Capture()
        {
            ExecutionFlow flow = ExecutionFlow.Current;
            return flow == null ? Default : new ExecutionContextImpl(flow);
        }

        public ExecutionContextImpl CreateCopy() => this;

        public static bool IsFlowSuppressed() => false;

        public static AsyncFlowControl SuppressFlow() =>
            throw new NotSupportedException("ExecutionContext.SuppressFlow is not supported by csharpc yet.");

        public static void RestoreFlow() => throw new InvalidOperationException("AsyncFlowControl object must be used on the thread where it was created.");

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
