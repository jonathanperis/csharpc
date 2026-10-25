// AsyncLocal<T>, and the execution-context flow it lives in.
//
// csharpc's runtime keeps one immutable flow object per logical flow (csrt_tl_flow, runtime/threads.c):
// captured when a thread starts, when work is queued and at each await, restored when an async method
// returns to its caller (FunctionCodeGen.Async.cs) — what .NET does with the ExecutionContext. The flow
// holds the current cultures (which .NET keeps in AsyncLocals too) and the AsyncLocal values; setting
// one makes a new flow, so the flows captured before don't see the change, as in .NET.
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CsharpcCoreLib
{
    internal sealed class ExecutionFlow
    {
        internal readonly object Culture, UICulture;   // CultureInfo (Globalization/CultureInfo.cs)
        private readonly object[] _keys;               // AsyncLocal instances
        private readonly object[] _values;

        private ExecutionFlow(object culture, object uiCulture, object[] keys, object[] values)
        {
            Culture = culture;
            UICulture = uiCulture;
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

        private static ExecutionFlow Make(object culture, object uiCulture, object[] keys, object[] values) =>
            culture == null && uiCulture == null && keys.Length == 0 ? null : new ExecutionFlow(culture, uiCulture, keys, values);

        internal static void SetCultures(object culture, object uiCulture)
        {
            ExecutionFlow flow = Current;
            Current = Make(culture, uiCulture, flow?._keys ?? [], flow?._values ?? []);
        }

        internal static object GetLocal(object key)
        {
            ExecutionFlow flow = Current;
            if (flow == null) return null;
            for (int i = 0; i < flow._keys.Length; i++)
                if (ReferenceEquals(flow._keys[i], key)) return flow._values[i];
            return null;
        }

        /// <summary>A new flow with `key` set to `value`; null (a default value) removes it, as .NET's AsyncLocal map does.</summary>
        internal static void SetLocal(object key, object value)
        {
            ExecutionFlow flow = Current;
            object[] keys = flow?._keys ?? [], values = flow?._values ?? [];
            int at = Array.IndexOf(keys, key);
            if (at < 0 && value == null) return;
            if (at >= 0 && ReferenceEquals(values[at], value)) return;
            object[] newKeys, newValues;
            if (at >= 0 && value == null)
            {
                newKeys = new object[keys.Length - 1];
                newValues = new object[keys.Length - 1];
                Array.Copy(keys, 0, newKeys, 0, at);
                Array.Copy(values, 0, newValues, 0, at);
                Array.Copy(keys, at + 1, newKeys, at, keys.Length - at - 1);
                Array.Copy(values, at + 1, newValues, at, keys.Length - at - 1);
            }
            else if (at >= 0)
            {
                newKeys = keys;
                newValues = (object[])values.Clone();
                newValues[at] = value;
            }
            else
            {
                newKeys = new object[keys.Length + 1];
                newValues = new object[keys.Length + 1];
                Array.Copy(keys, newKeys, keys.Length);
                Array.Copy(values, newValues, keys.Length);
                newKeys[keys.Length] = key;
                newValues[keys.Length] = value;
            }
            Current = Make(flow?.Culture, flow?.UICulture, newKeys, newValues);
        }
    }

    [Shadows("System.Threading.AsyncLocal`1")]
    public sealed class AsyncLocalImpl<T>
    {
        public AsyncLocalImpl() { }

        // .NET calls the handler whenever the value seen by the current thread changes, also when the
        // execution context switches (an await resuming elsewhere): csharpc doesn't track those switches.
        public AsyncLocalImpl(Action<AsyncLocalValueChangedArgs<T>> valueChangedHandler)
        {
            if (valueChangedHandler != null) throw new NotSupportedException("not supported by csharpc yet: AsyncLocal<T> value-changed handlers");
        }

        public T Value
        {
            get => ExecutionFlow.GetLocal(this) is object boxed ? (T)boxed : default;
            set => ExecutionFlow.SetLocal(this, value);
        }
    }
}
