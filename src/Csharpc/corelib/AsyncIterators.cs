// Async iterators compiled from IL and IValueTaskSource-backed ValueTasks (docs/10-async.md).
//
// The C# compiler turns `async IAsyncEnumerable<T>` methods into a class that is its own enumerator
// and its own IValueTaskSource<bool>: MoveNextAsync() resets a ManualResetValueTaskSourceCore<bool>,
// drives the state machine through AsyncIteratorMethodBuilder.MoveNext, and returns either a completed
// ValueTask<bool> or `new ValueTask<bool>(this, version)`. csrt's ValueTask is a task pointer, so a
// ValueTask over an IValueTaskSource is bridged to a task: completed at once when the source already
// is, else when the source invokes the continuation registered through OnCompleted. The bridge calls
// GetResult exactly once, after completion, as an awaiter of the ValueTask would.
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.CompilerServices.AsyncIteratorMethodBuilder")]
    public struct AsyncIteratorMethodBuilderImpl
    {
        private object _box;

        public static AsyncIteratorMethodBuilderImpl Create() { return default(AsyncIteratorMethodBuilderImpl); }

        public void MoveNext<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.Start(ref stateMachine);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.AwaitOnCompleted(ref _box, ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine =>
            AsyncBuilderCore.AwaitUnsafeOnCompleted(ref _box, ref awaiter, ref stateMachine);

        public void Complete() { _box = null; }
    }

    internal static class ValueTaskSourceSentinel
    {
        private static Action<object> s_completed;

        /// <summary>Stored as the continuation once the operation completed (published once, compared by reference).</summary>
        public static Action<object> Completed
        {
            get
            {
                var s = s_completed;
                if (s != null) return s;
                Action<object> created = Nothing;
                return Interlocked.CompareExchange(ref s_completed, created, null) ?? created;
            }
        }

        private static void Nothing(object state) => throw new InvalidOperationException("Operation is not valid due to the current state of the object.");

        public static void Invalid() => throw new InvalidOperationException("Operation is not valid due to the current state of the object.");
    }

    // Port of .NET's ManualResetValueTaskSourceCore<TResult> (no ExecutionContext/SynchronizationContext:
    // continuations run inline on the completing thread, or on the pool with RunContinuationsAsynchronously
    // or when registered after completion).
    [Shadows("System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1")]
    public struct ManualResetValueTaskSourceCoreImpl<TResult>
    {
        private Action<object> _continuation;
        private object _continuationState;
        private Exception _error;
        private TResult _result;
        private short _version;
        private bool _completed;
        private bool _runContinuationsAsynchronously;

        public bool RunContinuationsAsynchronously
        {
            get => _runContinuationsAsynchronously;
            set => _runContinuationsAsynchronously = value;
        }

        public void Reset()
        {
            _version++;
            _continuation = null;
            _continuationState = null;
            _result = default(TResult);
            _error = null;
            _completed = false;
        }

        public void SetResult(TResult result)
        {
            _result = result;
            SignalCompletion();
        }

        public void SetException(Exception error)
        {
            _error = error;
            SignalCompletion();
        }

        public short Version => _version;

        public ValueTaskSourceStatus GetStatus(short token)
        {
            ValidateToken(token);
            return _continuation == null || !_completed ? ValueTaskSourceStatus.Pending
                : _error == null ? ValueTaskSourceStatus.Succeeded
                : _error is OperationCanceledException ? ValueTaskSourceStatus.Canceled
                : ValueTaskSourceStatus.Faulted;
        }

        public TResult GetResult(short token)
        {
            if (token != _version || !_completed || _error != null) ThrowForFailedGetResult(token);
            return _result;
        }

        private void ThrowForFailedGetResult(short token)
        {
            if (token != _version || !_completed) ValueTaskSourceSentinel.Invalid();
            throw _error;
        }

        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if (continuation == null) throw new ArgumentNullException("continuation");
            ValidateToken(token);
            _continuationState = state;
            var sentinel = ValueTaskSourceSentinel.Completed;
            var old = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (old != null)
            {
                if (!ReferenceEquals(old, sentinel)) ValueTaskSourceSentinel.Invalid();
                _continuationState = null;
                QueueContinuation(continuation, state); // already completed: .NET queues to the pool
            }
        }

        private void ValidateToken(short token)
        {
            if (token != _version) ValueTaskSourceSentinel.Invalid();
        }

        private void SignalCompletion()
        {
            if (_completed) ValueTaskSourceSentinel.Invalid();
            _completed = true;
            var continuation = _continuation ?? Interlocked.CompareExchange(ref _continuation, ValueTaskSourceSentinel.Completed, null);
            if (continuation == null) return;
            var state = _continuationState;
            if (_runContinuationsAsynchronously) QueueContinuation(continuation, state);
            else continuation(state);
        }

        private static void QueueContinuation(Action<object> continuation, object state)
        {
            Task.Run(() => continuation(state));
        }
    }

    /// <summary>new ValueTask&lt;T&gt;(IValueTaskSource&lt;T&gt;, short) as a csrt task.</summary>
    internal sealed class ValueTaskSourceBridge<TResult>
    {
        private IValueTaskSource<TResult> _source;
        private short _token;
        private TaskCompletionSource<TResult> _promise;

        public static Task<TResult> Create(IValueTaskSource<TResult> source, short token)
        {
            if (source == null) throw new ArgumentNullException("source");
            var bridge = new ValueTaskSourceBridge<TResult>();
            bridge._source = source;
            bridge._token = token;
            bridge._promise = new TaskCompletionSource<TResult>();
            if (source.GetStatus(token) != ValueTaskSourceStatus.Pending) bridge.Complete(null);
            else source.OnCompleted(bridge.Complete, null, token, ValueTaskSourceOnCompletedFlags.None);
            return bridge._promise.Task;
        }

        private void Complete(object state)
        {
            TResult result;
            try { result = _source.GetResult(_token); }
            catch (Exception e) { CoreIntrinsics.TryFaultAsyncTask(_promise.Task, e); return; }
            _promise.SetResult(result);
        }
    }

    /// <summary>new ValueTask(IValueTaskSource, short) as a csrt task.</summary>
    internal sealed class ValueTaskSourceBridge
    {
        private IValueTaskSource _source;
        private short _token;
        private TaskCompletionSource _promise;

        public static Task Create(IValueTaskSource source, short token)
        {
            if (source == null) throw new ArgumentNullException("source");
            var bridge = new ValueTaskSourceBridge();
            bridge._source = source;
            bridge._token = token;
            bridge._promise = new TaskCompletionSource();
            if (source.GetStatus(token) != ValueTaskSourceStatus.Pending) bridge.Complete(null);
            else source.OnCompleted(bridge.Complete, null, token, ValueTaskSourceOnCompletedFlags.None);
            return bridge._promise.Task;
        }

        private void Complete(object state)
        {
            try { _source.GetResult(_token); }
            catch (Exception e) { CoreIntrinsics.TryFaultAsyncTask(_promise.Task, e); return; }
            _promise.SetResult();
        }
    }

    // `await foreach (var x in source.WithCancellation(token))` / `.ConfigureAwait(false)`.
    [Shadows("System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1")]
    public struct ConfiguredCancelableAsyncEnumerableImpl<T>
    {
        private readonly IAsyncEnumerable<T> _enumerable;
        private readonly CancellationToken _cancellationToken;
        private readonly bool _continueOnCapturedContext;

        internal ConfiguredCancelableAsyncEnumerableImpl(IAsyncEnumerable<T> enumerable, bool continueOnCapturedContext, CancellationToken cancellationToken)
        {
            _enumerable = enumerable;
            _continueOnCapturedContext = continueOnCapturedContext;
            _cancellationToken = cancellationToken;
        }

        public ConfiguredCancelableAsyncEnumerableImpl<T> ConfigureAwait(bool continueOnCapturedContext) =>
            new ConfiguredCancelableAsyncEnumerableImpl<T>(_enumerable, continueOnCapturedContext, _cancellationToken);

        public ConfiguredCancelableAsyncEnumerableImpl<T> WithCancellation(CancellationToken cancellationToken) =>
            new ConfiguredCancelableAsyncEnumerableImpl<T>(_enumerable, _continueOnCapturedContext, cancellationToken);

        public Enumerator GetAsyncEnumerator()
        {
            // csharpc's own async iterators (LLVM coroutines) can't pass a token to their
            // [EnumeratorCancellation] parameter after the call: say so rather than ignore it.
            if (_cancellationToken.CanBeCanceled && CoreIntrinsics.IsCoroutineIterator(_enumerable))
                throw new NotSupportedException("csharpc: WithCancellation on an async iterator compiled from C# source is not supported yet (the token would not reach its [EnumeratorCancellation] parameter).");
            return new Enumerator(_enumerable.GetAsyncEnumerator(_cancellationToken), _continueOnCapturedContext);
        }

        [Shadows("System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1+Enumerator")]
        public struct Enumerator
        {
            private readonly IAsyncEnumerator<T> _enumerator;
            private readonly bool _continueOnCapturedContext;

            internal Enumerator(IAsyncEnumerator<T> enumerator, bool continueOnCapturedContext)
            {
                _enumerator = enumerator;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public ConfiguredValueTaskAwaitable<bool> MoveNextAsync() => _enumerator.MoveNextAsync().ConfigureAwait(_continueOnCapturedContext);

            public T Current => _enumerator.Current;

            public ConfiguredValueTaskAwaitable DisposeAsync() => _enumerator.DisposeAsync().ConfigureAwait(_continueOnCapturedContext);
        }
    }

    [ShadowMembers("System.Threading.Tasks.TaskAsyncEnumerableExtensions")]
    internal static class TaskAsyncEnumerableExtensionsImpl
    {
        public static ConfiguredCancelableAsyncEnumerableImpl<T> WithCancellation<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
        {
            if (source == null) throw new ArgumentNullException("source");
            return new ConfiguredCancelableAsyncEnumerableImpl<T>(source, true, cancellationToken);
        }

        public static ConfiguredCancelableAsyncEnumerableImpl<T> ConfigureAwait<T>(IAsyncEnumerable<T> source, bool continueOnCapturedContext)
        {
            if (source == null) throw new ArgumentNullException("source");
            return new ConfiguredCancelableAsyncEnumerableImpl<T>(source, continueOnCapturedContext, default(CancellationToken));
        }
    }
}
