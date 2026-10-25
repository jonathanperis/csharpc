// System.Threading.ExecutionContext over csharpc's execution-context flow (ExecutionFlow.cs): Capture,
// Run and Restore move the immutable flow object around, as .NET's ExecutionContext does with its
// AsyncLocal map; SuppressFlow makes the runtime's capture points (queued work, Thread.Start, awaits) carry
// no flow until RestoreFlow. (SynchronizationContext: SynchronizationContext.cs.)
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
        private static extern int FlowSuppressedRaw();

        private static bool FlowSuppressed() => FlowSuppressedRaw() != 0;

        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_flow_suppress")]
        private static extern void SetFlowSuppressedRaw(int suppressed);

        internal static void SetFlowSuppressed(bool suppressed) => SetFlowSuppressedRaw(suppressed ? 1 : 0);

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
            // .NET's RunInternal also puts back the thread's SynchronizationContext.
            ExecutionFlow saved = ExecutionFlow.Current;
            SynchronizationContext savedContext = SynchronizationContext.Current;
            ExecutionFlow.Current = executionContext.Flow;
            try { callback(state); }
            finally
            {
                ExecutionFlow.Current = saved;
                if (SynchronizationContext.Current != savedContext) SynchronizationContext.SetSynchronizationContext(savedContext);
            }
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
}
