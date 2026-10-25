// CancellationToken.Register / UnsafeRegister and CancellationTokenRegistration, over the C runtime's
// CancellationTokenSource (src/runtime/async.c; a CancellationToken is its source pointer, default ==
// null). A registration is a CancellationCallbackNode the source keeps in its callback list next to
// the runtime's own entries (Task.Delay tasks, linked sources). Cancel runs them here, newest first
// as .NET does, with .NET's exception handling: Cancel() / Cancel(false) runs every callback and then
// throws an AggregateException of what they threw; Cancel(true) lets the first exception propagate
// and leaves the remaining callbacks unrun.
#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal sealed class CancellationCallbackNode
    {
        internal object Source;
        internal Action Plain;
        internal Action<object> WithState;
        internal Action<object, CancellationToken> WithToken;
        internal object State;

        internal void Invoke()
        {
            if (Plain != null) Plain();
            else if (WithState != null) WithState(State);
            else WithToken(State, CoreIntrinsics.SourceToken(Source));
        }
    }

    internal static class CancellationRuntime
    {
        /// <summary>
        /// new CancellationTokenSource(TimeSpan) / CancelAfter(TimeSpan): .NET's range check ("delay", -1 to
        /// Timer.MaxSupportedTimeout ms); delays beyond int.MaxValue ms (~24.8 days) wait int.MaxValue ms.
        /// </summary>
        internal static int DelayMilliseconds(TimeSpan delay)
        {
            long totalMilliseconds = (long)delay.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > 0xFFFFFFFE) throw new ArgumentOutOfRangeException("delay");
            return totalMilliseconds > int.MaxValue ? int.MaxValue : (int)totalMilliseconds;
        }

        internal static CancellationTokenRegistrationImpl Register(CancellationToken token, CancellationCallbackNode node)
        {
            object source = CoreIntrinsics.TokenSource(token);
            if (source == null) return default; // CancellationToken.None can't be canceled
            node.Source = source;
            switch (CoreIntrinsics.csrt_cts_register_node(source, node))
            {
                case 0: return new CancellationTokenRegistrationImpl(node);
                case 1: node.Invoke(); return default; // canceled already: runs now, on this thread
                default: return default;               // the source was disposed: .NET drops the callback
            }
        }

        /// <summary>
        /// Runs the callbacks of a source that was just canceled (the C runtime's csrt_cts_notify in
        /// programs that register callbacks; its fallback only knows the runtime's own entries).
        /// </summary>
        [RuntimeExport("csrt_cts_notify", "csrt_cts_register_node")]
        public static void Notify(object source, int throwOnFirstException)
        {
            List<Exception> exceptions = null;
            try
            {
                object entry;
                while ((entry = CoreIntrinsics.csrt_cts_next_callback(source)) != null)
                {
                    try
                    {
                        switch (CoreIntrinsics.csrt_cts_entry_kind(entry))
                        {
                            case 1: CoreIntrinsics.csrt_cts_cancel_delay(entry); break;
                            // A linked source: .NET's callback is NotifyCancellation(throwOnFirstException: false).
                            case 2: if (CoreIntrinsics.csrt_cts_transition(entry)) Notify(entry, 0); break;
                            default: ((CancellationCallbackNode)entry).Invoke(); break;
                        }
                    }
                    catch (Exception e) when (throwOnFirstException == 0)
                    {
                        if (exceptions == null) exceptions = new List<Exception>();
                        exceptions.Add(e);
                    }
                }
            }
            finally
            {
                CoreIntrinsics.csrt_cts_callbacks_done(source);
            }
            if (exceptions != null) throw new AggregateException(exceptions);
        }

        internal static Exception CallbackNull() => new ArgumentNullException("callback");
    }

    [Shadows("System.Threading.CancellationTokenRegistration")]
    public readonly struct CancellationTokenRegistrationImpl : IDisposable, IAsyncDisposable
    {
        private readonly CancellationCallbackNode _node;

        internal CancellationTokenRegistrationImpl(CancellationCallbackNode node) { _node = node; }

        public CancellationToken Token => _node != null ? CoreIntrinsics.SourceToken(_node.Source) : default(CancellationToken);

        /// <summary>Unregisters; a callback already running on another thread is waited for, as in .NET.</summary>
        public void Dispose()
        {
            CancellationCallbackNode node = _node;
            if (node != null && !CoreIntrinsics.csrt_cts_unregister(node.Source, node))
                CoreIntrinsics.csrt_cts_wait_for_callback(node.Source, node);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return new ValueTask(Task.CompletedTask);
        }

        public bool Unregister() => _node != null && CoreIntrinsics.csrt_cts_unregister(_node.Source, _node);

        public bool Equals(CancellationTokenRegistrationImpl other) => _node == other._node;
        public override bool Equals(object obj) => obj is CancellationTokenRegistrationImpl other && other._node == _node;
        public override int GetHashCode() => _node != null ? _node.GetHashCode() : 0;
        public static bool operator ==(CancellationTokenRegistrationImpl left, CancellationTokenRegistrationImpl right) => left._node == right._node;
        public static bool operator !=(CancellationTokenRegistrationImpl left, CancellationTokenRegistrationImpl right) => left._node != right._node;
    }

    [ShadowMembers("System.Threading.CancellationToken")]
    internal static class CancellationTokenMembers
    {
        // No ExecutionContext to capture: Register and UnsafeRegister agree.
        [ShadowInstance]
        public static CancellationTokenRegistrationImpl Register(CancellationToken token, Action callback)
        {
            if (callback == null) throw CancellationRuntime.CallbackNull();
            CancellationCallbackNode node = new CancellationCallbackNode();
            node.Plain = callback;
            return CancellationRuntime.Register(token, node);
        }

        // useSynchronizationContext: the callback runs through the context current now (its Send), as in .NET.
        [ShadowInstance]
        public static CancellationTokenRegistrationImpl Register(CancellationToken token, Action callback, bool useSynchronizationContext)
        {
            SynchronizationContext context = useSynchronizationContext ? SynchronizationContextImpl.CurrentCustom : null;
            if (context == null || callback == null) return Register(token, callback);
            return Register(token, () => context.Send(static s => ((Action)s)(), callback));
        }

        [ShadowInstance]
        public static CancellationTokenRegistrationImpl Register(CancellationToken token, Action<object> callback, object state)
        {
            if (callback == null) throw CancellationRuntime.CallbackNull();
            CancellationCallbackNode node = new CancellationCallbackNode();
            node.WithState = callback;
            node.State = state;
            return CancellationRuntime.Register(token, node);
        }

        [ShadowInstance]
        public static CancellationTokenRegistrationImpl Register(CancellationToken token, Action<object> callback, object state, bool useSynchronizationContext)
        {
            SynchronizationContext context = useSynchronizationContext ? SynchronizationContextImpl.CurrentCustom : null;
            if (context == null || callback == null) return Register(token, callback, state);
            return Register(token, () => context.Send(s => callback(s), state));
        }

        [ShadowInstance]
        public static CancellationTokenRegistrationImpl Register(CancellationToken token, Action<object, CancellationToken> callback, object state)
        {
            if (callback == null) throw CancellationRuntime.CallbackNull();
            CancellationCallbackNode node = new CancellationCallbackNode();
            node.WithToken = callback;
            node.State = state;
            return CancellationRuntime.Register(token, node);
        }

        [ShadowInstance]
        public static CancellationTokenRegistrationImpl UnsafeRegister(CancellationToken token, Action<object> callback, object state) =>
            Register(token, callback, state);

        [ShadowInstance]
        public static CancellationTokenRegistrationImpl UnsafeRegister(CancellationToken token, Action<object, CancellationToken> callback, object state) =>
            Register(token, callback, state);
    }

    [ShadowMembers("System.Threading.CancellationTokenSource")]
    internal static class CancellationTokenSourceMembers
    {
        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            CoreIntrinsics.csrt_cts_link(source, token);
            return source;
        }

        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken[] tokens)
        {
            if (tokens == null) throw new ArgumentNullException("tokens");
            return CreateLinkedTokenSource(new ReadOnlySpan<CancellationToken>(tokens));
        }

        public static CancellationTokenSource CreateLinkedTokenSource(ReadOnlySpan<CancellationToken> tokens)
        {
            if (tokens.Length == 0) throw new ArgumentException("No tokens were supplied.");
            CancellationTokenSource source = new CancellationTokenSource();
            for (int i = 0; i < tokens.Length; i++) CoreIntrinsics.csrt_cts_link(source, tokens[i]);
            return source;
        }

        // CancelAfter(TimeSpan) as .NET checks it (-1 ms is "never"; up to 2^32-2 ms), on the runtime's
        // millisecond timer, which takes an int.
        [ShadowInstance]
        public static void CancelAfter(CancellationTokenSource source, TimeSpan delay)
        {
            long ms = (long)delay.TotalMilliseconds;
            if (ms < -1 || ms > 0xfffffffeL) throw new ArgumentOutOfRangeException("delay");
            if (ms > int.MaxValue)
                throw new NotSupportedException("CancelAfter beyond Int32.MaxValue milliseconds is not supported by csharpc yet.");
            source.CancelAfter((int)ms);
        }

        /// <summary>.NET runs the callbacks on the thread pool; the returned task ends with them.</summary>
        [ShadowInstance]
        public static Task CancelAsync(CancellationTokenSource source)
        {
            object self = CoreIntrinsics.csrt_cts_token(source); // ObjectDisposedException once disposed
            if (!CoreIntrinsics.csrt_cts_transition(self)) return Task.CompletedTask;
            return Task.Run(() => CancellationRuntime.Notify(self, 0));
        }
    }
}
