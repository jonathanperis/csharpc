// The execution context: csharpc's runtime keeps one GC object per logical flow (csrt_tl_flow,
// src/runtime/threads.c), captured when a thread starts, when work is queued and at await, and restored
// when an async method returns to its caller (as AsyncMethodBuilder.Start does). The object is an
// immutable ExecutionFlow: AsyncLocal<T> values and the current cultures, copied on every change, so a
// captured flow never sees later changes (.NET's AsyncLocalValueMap is immutable the same way).
// ThreadPool queues work items with the flow (QueueUserWorkItem) or without it (UnsafeQueueUserWorkItem).
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CsharpcCoreLib
{
    internal sealed class ExecutionFlow
    {
        private readonly object[] _keys;
        private readonly object[] _values;

        private ExecutionFlow(object[] keys, object[] values)
        {
            _keys = keys;
            _values = values;
        }

        [DllImport("__Internal", EntryPoint = "csrt_flow_get")]
        private static extern IntPtr FlowGet();

        [DllImport("__Internal", EntryPoint = "csrt_flow_set")]
        private static extern void FlowSet(IntPtr flow);

        internal static ExecutionFlow Current
        {
            get
            {
                IntPtr flow = FlowGet();
                return flow == IntPtr.Zero ? null : (ExecutionFlow)CoreIntrinsics.PointerToObject(flow);
            }
            set => FlowSet(value == null ? IntPtr.Zero : CoreIntrinsics.ObjectToPointer(value));
        }

        internal static object Get(object key)
        {
            ExecutionFlow flow = Current;
            if (flow == null) return null;
            object[] keys = flow._keys;
            for (int i = 0; i < keys.Length; i++)
                if (ReferenceEquals(keys[i], key)) return flow._values[i];
            return null;
        }

        /// <summary>A new current flow with key → value (null removes it).</summary>
        internal static void Set(object key, object value)
        {
            ExecutionFlow flow = Current;
            object[] keys = flow?._keys ?? Array.Empty<object>();
            object[] values = flow?._values ?? Array.Empty<object>();
            int index = -1;
            for (int i = 0; i < keys.Length; i++)
                if (ReferenceEquals(keys[i], key)) { index = i; break; }
            if (index >= 0 && ReferenceEquals(values[index], value)) return;
            if (value == null)
            {
                if (index < 0) return;
                if (keys.Length == 1) { Current = null; return; }
                var k = new object[keys.Length - 1];
                var v = new object[keys.Length - 1];
                for (int i = 0, j = 0; i < keys.Length; i++)
                {
                    if (i == index) continue;
                    k[j] = keys[i];
                    v[j++] = values[i];
                }
                Current = new ExecutionFlow(k, v);
                return;
            }
            if (index >= 0)
            {
                var v = (object[])values.Clone();
                v[index] = value;
                Current = new ExecutionFlow(keys, v);
                return;
            }
            var nk = new object[keys.Length + 1];
            var nv = new object[keys.Length + 1];
            Array.Copy(keys, nk, keys.Length);
            Array.Copy(values, nv, keys.Length);
            nk[keys.Length] = key;
            nv[keys.Length] = value;
            Current = new ExecutionFlow(nk, nv);
        }
    }

    [Shadows("System.Threading.AsyncLocal`1")]
    public sealed class AsyncLocalImpl<T>
    {
        public AsyncLocalImpl() { }

        public AsyncLocalImpl(Action<AsyncLocalValueChangedArgs<T>> valueChangedHandler)
        {
            // .NET calls the handler on every execution-context switch; csharpc's flows don't track that.
            if (valueChangedHandler != null)
                throw new NotSupportedException("An AsyncLocal<T> value-changed handler is not supported by csharpc yet.");
        }

        public T Value
        {
            get
            {
                object value = ExecutionFlow.Get(this);
                return value == null ? default : (T)value;
            }
            set => ExecutionFlow.Set(this, value);
        }
    }

    [Shadows("System.Threading.ThreadPool")]
    public static unsafe class ThreadPoolImpl
    {
        [DllImport("__Internal", EntryPoint = "csrt_pool_queue_action")]
        private static extern void QueueAction(IntPtr action, bool flowContext);

        // A one-argument delegate and its (reference) argument, queued as they are: Kestrel and
        // System.IO.Pipelines queue an IThreadPoolWorkItem or an Action<object> with its state for
        // every I/O completion, and wrapping them in a closure allocated two objects each time.
        [DllImport("__Internal", EntryPoint = "csrt_pool_queue_state")]
        private static extern void QueueState(IntPtr action, IntPtr state, bool flowContext);

        private static Action<IThreadPoolWorkItem> s_execute;

        private static void Queue(Action action, bool flowContext) => QueueAction(CoreIntrinsics.ObjectToPointer(action), flowContext);

        private static void Queue<TState>(Action<TState> callBack, TState state, bool flowContext)
        {
            if (CoreIntrinsics.IsValueType<TState>()) Queue(() => callBack(state), flowContext);
            else QueueState(CoreIntrinsics.ObjectToPointer(callBack), CoreIntrinsics.ObjectToPointer(state), flowContext);
        }

        public static bool QueueUserWorkItem(WaitCallback callBack) => QueueUserWorkItem(callBack, null);

        public static bool QueueUserWorkItem(WaitCallback callBack, object state)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            QueueState(CoreIntrinsics.ObjectToPointer(callBack), CoreIntrinsics.ObjectToPointer(state), true);
            return true;
        }

        public static bool QueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            Queue(callBack, state, true);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem(WaitCallback callBack, object state)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            QueueState(CoreIntrinsics.ObjectToPointer(callBack), CoreIntrinsics.ObjectToPointer(state), false);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            Queue(callBack, state, false);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem(IThreadPoolWorkItem callBack, bool preferLocal)
        {
            if (callBack == null) throw new ArgumentNullException("callBack");
            if (callBack is System.Threading.Tasks.Task)
                throw new ArgumentOutOfRangeException("callBack");
            Action<IThreadPoolWorkItem> execute = s_execute ??= static item => item.Execute();
            QueueState(CoreIntrinsics.ObjectToPointer(execute), CoreIntrinsics.ObjectToPointer(callBack), false);
            return true;
        }
    }
}
