// System.Threading.Tasks.ValueTask<TResult> and its awaiters, ported from .NET (dotnet/runtime release/11.0
// ValueTask.cs, ValueTaskAwaiter.cs, ConfiguredValueTaskAwaitable.cs): a struct holding the result inline,
// a Task<TResult> or an IValueTaskSource<TResult> with its token. A synchronously completed ValueTask<T>
// allocates nothing, and a source-backed one calls the source exactly as .NET does (awaiting it twice is
// the source's error, not a second result). The non-generic ValueTask is still a csrt task pointer.
//
// `await` compiles to the awaiter calls below, with one shortcut (FunctionCodeGen.Async.cs): a pending
// Task-backed ValueTask resumes the coroutine straight from the task, like `await task` does.
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace CsharpcCoreLib
{
    internal static class ValueTaskSupport
    {
        [DllImport("__Internal", EntryPoint = "csrt_object_is_task")]
        private static extern int IsTaskObject(IntPtr obj);

        /// <summary>Whether a ValueTask's object is a (csrt) task rather than an IValueTaskSource.</summary>
        internal static bool IsTask(object obj) => IsTaskObject(CoreIntrinsics.ObjectToPointer(obj)) != 0;

        /// <summary>.NET's ValueTaskAwaiter.s_invokeActionDelegate: the continuation a source runs, its state the Action.</summary>
        internal static readonly Action<object> s_invokeAction = static state =>
        {
            if (state is not Action action) throw new ArgumentOutOfRangeException("state");
            action();
        };

        [DllImport("__Internal", EntryPoint = "csrt_task_on_completed")]
        internal static extern void OnTask(IntPtr task, IntPtr frame);

        [DllImport("__Internal", EntryPoint = "csrt_task_on_completed_no_context")]
        internal static extern void OnTaskNoContext(IntPtr task, IntPtr frame);

        [DllImport("__Internal", EntryPoint = "csrt_coro_resume_state")]
        private static extern void ResumeState(IntPtr state);

        [DllImport("__Internal", EntryPoint = "csrt_coro_resume_token")]
        internal static extern IntPtr ResumeToken(IntPtr frame);

        /// <summary>`await` of a pending source-backed ValueTask: the source resumes the coroutine (csrt_coro_resume_token).</summary>
        internal static readonly Action<object> s_resume = static state => ResumeState(CoreIntrinsics.ObjectToPointer(state));
    }

    /// <summary>
    /// What compiled code calls (FunctionCodeGen.Async.cs, ModuleCodeGen.Async.cs): `await` of a ValueTask&lt;T&gt; or
    /// its configured awaitable, and the ValueTask&lt;T&gt; an async method or async iterator returns.
    /// </summary>
    internal static class ValueTaskAwait<TResult>
    {
        internal static ValueTaskImpl<TResult> Unconfigured(ConfiguredValueTaskAwaitableImpl<TResult> configured) => configured._value;

        internal static bool IsCompleted(ValueTaskImpl<TResult> value) => value.IsCompleted;

        internal static TResult GetResult(ValueTaskImpl<TResult> value) => value.Result;

        /// <summary>
        /// Resumes the suspended coroutine `frame` when `value` completes: straight from the task (as `await task`),
        /// or through the source's OnCompleted, posting to the captured context unless ConfigureAwait(false).
        /// </summary>
        internal static void OnCompleted(ValueTaskImpl<TResult> value, IntPtr frame)
        {
            object obj = value._obj;
            if (ValueTaskSupport.IsTask(obj))
            {
                if (value._continueOnCapturedContext) ValueTaskSupport.OnTask(CoreIntrinsics.ObjectToPointer(obj), frame);
                else ValueTaskSupport.OnTaskNoContext(CoreIntrinsics.ObjectToPointer(obj), frame);
                return;
            }
            ((IValueTaskSource<TResult>)obj).OnCompleted(ValueTaskSupport.s_resume, CoreIntrinsics.PointerToObject(ValueTaskSupport.ResumeToken(frame)),
                value._token, value._continueOnCapturedContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None);
        }

        /// <summary>An async method's (or async iterator's MoveNextAsync's) csrt task as the ValueTask&lt;T&gt; it returns.</summary>
        internal static ValueTaskImpl<TResult> OfTask(IntPtr task) => new ValueTaskImpl<TResult>(CoreIntrinsics.PointerToObject(task), default, 0, true);
    }

    [Shadows("System.Threading.Tasks.ValueTask`1")]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ValueTaskImpl<TResult>
    {
        // null: the result is _result; else a Task<TResult> or an IValueTaskSource<TResult>.
        internal readonly object _obj;
        internal readonly TResult _result;
        internal readonly short _token;
        internal readonly bool _continueOnCapturedContext;

        public ValueTaskImpl(TResult result)
        {
            _result = result;
            _obj = null;
            _continueOnCapturedContext = true;
            _token = 0;
        }

        public ValueTaskImpl(Task<TResult> task)
        {
            if (task == null) throw new ArgumentNullException("task");
            _obj = task;
            _result = default;
            _continueOnCapturedContext = true;
            _token = 0;
        }

        public ValueTaskImpl(IValueTaskSource<TResult> source, short token)
        {
            if (source == null) throw new ArgumentNullException("source");
            _obj = source;
            _token = token;
            _result = default;
            _continueOnCapturedContext = true;
        }

        internal ValueTaskImpl(object obj, TResult result, short token, bool continueOnCapturedContext)
        {
            _obj = obj;
            _result = result;
            _token = token;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        private Task<TResult> TaskObject => CoreIntrinsics.ReinterpretAs<Task<TResult>>(_obj);
        private IValueTaskSource<TResult> Source => (IValueTaskSource<TResult>)_obj;

        public override int GetHashCode() =>
            _obj != null ? _obj.GetHashCode() :
            _result != null ? _result.GetHashCode() :
            0;

        public override bool Equals(object obj) => obj is ValueTask<TResult> && Equals((ValueTaskImpl<TResult>)obj);

        // (Parameters of the shadow type match the real ValueTask<TResult>: docs/12-corelib.md, "Matching".)
        public bool Equals(ValueTaskImpl<TResult> other) =>
            _obj != null || other._obj != null
                ? ReferenceEquals(_obj, other._obj) && _token == other._token
                : EqualityComparer<TResult>.Default.Equals(_result, other._result);

        public static bool operator ==(ValueTaskImpl<TResult> left, ValueTaskImpl<TResult> right) => left.Equals(right);

        public static bool operator !=(ValueTaskImpl<TResult> left, ValueTaskImpl<TResult> right) => !(left == right);

        public Task<TResult> AsTask()
        {
            object obj = _obj;
            if (obj == null) return Task.FromResult(_result);
            if (ValueTaskSupport.IsTask(obj)) return TaskObject;
            return GetTaskForValueTaskSource(Source);
        }

        public ValueTaskImpl<TResult> Preserve() => _obj == null ? this : new ValueTaskImpl<TResult>(AsTask());

        private Task<TResult> GetTaskForValueTaskSource(IValueTaskSource<TResult> t)
        {
            ValueTaskSourceStatus status = t.GetStatus(_token);
            if (status != ValueTaskSourceStatus.Pending)
            {
                try
                {
                    return Task.FromResult(t.GetResult(_token));
                }
                catch (Exception exc)
                {
                    if (status == ValueTaskSourceStatus.Canceled)
                    {
                        if (exc is OperationCanceledException oce)
                        {
                            var canceled = new TaskCompletionSource<TResult>();
                            CoreIntrinsics.TryFaultAsyncTask(canceled.Task, oce); // canceled, keeping the exception (as TrySetCanceled(token, oce))
                            return canceled.Task;
                        }
                        return Task.FromCanceled<TResult>(new CancellationToken(true));
                    }
                    return Task.FromException<TResult>(exc);
                }
            }
            return ValueTaskSourceBridge<TResult>.TaskFor(t, _token);
        }

        public bool IsCompleted
        {
            get
            {
                object obj = _obj;
                if (obj == null) return true;
                if (ValueTaskSupport.IsTask(obj)) return TaskObject.IsCompleted;
                return Source.GetStatus(_token) != ValueTaskSourceStatus.Pending;
            }
        }

        public bool IsCompletedSuccessfully
        {
            get
            {
                object obj = _obj;
                if (obj == null) return true;
                if (ValueTaskSupport.IsTask(obj)) return TaskObject.IsCompletedSuccessfully;
                return Source.GetStatus(_token) == ValueTaskSourceStatus.Succeeded;
            }
        }

        public bool IsFaulted
        {
            get
            {
                object obj = _obj;
                if (obj == null) return false;
                if (ValueTaskSupport.IsTask(obj)) return TaskObject.IsFaulted;
                return Source.GetStatus(_token) == ValueTaskSourceStatus.Faulted;
            }
        }

        public bool IsCanceled
        {
            get
            {
                object obj = _obj;
                if (obj == null) return false;
                if (ValueTaskSupport.IsTask(obj)) return TaskObject.IsCanceled;
                return Source.GetStatus(_token) == ValueTaskSourceStatus.Canceled;
            }
        }

        /// <summary>The result; a failure throws the original exception (TaskAwaiter.ValidateEnd), not an AggregateException.</summary>
        public TResult Result
        {
            get
            {
                object obj = _obj;
                if (obj == null) return _result;
                if (ValueTaskSupport.IsTask(obj)) return TaskObject.GetAwaiter().GetResult();
                return Source.GetResult(_token);
            }
        }

        // (Return types aren't matched: the shadow awaiter is ValueTaskAwaiter<TResult> after mapping.)
        public ValueTaskAwaiterImpl<TResult> GetAwaiter() => new ValueTaskAwaiterImpl<TResult>(this);

        public ConfiguredValueTaskAwaitableImpl<TResult> ConfigureAwait(bool continueOnCapturedContext) =>
            new ConfiguredValueTaskAwaitableImpl<TResult>(new ValueTaskImpl<TResult>(_obj, _result, _token, continueOnCapturedContext));

        public override string ToString()
        {
            if (IsCompletedSuccessfully)
            {
                TResult result = Result;
                if (result != null) return result.ToString();
            }
            return string.Empty;
        }
    }

    [Shadows("System.Runtime.CompilerServices.ValueTaskAwaiter`1")]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ValueTaskAwaiterImpl<TResult> : ICriticalNotifyCompletion
    {
        internal readonly ValueTaskImpl<TResult> _value;

        internal ValueTaskAwaiterImpl(in ValueTaskImpl<TResult> value) => _value = value;

        public bool IsCompleted => _value.IsCompleted;

        public TResult GetResult() => _value.Result;

        public void OnCompleted(Action continuation)
        {
            object obj = _value._obj;
            if (obj != null && ValueTaskSupport.IsTask(obj)) CoreIntrinsics.ReinterpretAs<Task<TResult>>(obj).GetAwaiter().OnCompleted(continuation);
            else if (obj != null)
                ((IValueTaskSource<TResult>)obj).OnCompleted(ValueTaskSupport.s_invokeAction, continuation, _value._token,
                    ValueTaskSourceOnCompletedFlags.UseSchedulingContext | ValueTaskSourceOnCompletedFlags.FlowExecutionContext);
            else Task.CompletedTask.GetAwaiter().OnCompleted(continuation);
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            object obj = _value._obj;
            if (obj != null && ValueTaskSupport.IsTask(obj)) CoreIntrinsics.ReinterpretAs<Task<TResult>>(obj).GetAwaiter().UnsafeOnCompleted(continuation);
            else if (obj != null)
                ((IValueTaskSource<TResult>)obj).OnCompleted(ValueTaskSupport.s_invokeAction, continuation, _value._token,
                    ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
            else Task.CompletedTask.GetAwaiter().UnsafeOnCompleted(continuation);
        }
    }

    [Shadows("System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1")]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ConfiguredValueTaskAwaitableImpl<TResult>
    {
        internal readonly ValueTaskImpl<TResult> _value;

        internal ConfiguredValueTaskAwaitableImpl(in ValueTaskImpl<TResult> value) => _value = value;

        public ConfiguredValueTaskAwaiter GetAwaiter() => new ConfiguredValueTaskAwaiter(_value);

        [Shadows("System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1+ConfiguredValueTaskAwaiter")]
        [StructLayout(LayoutKind.Auto)]
        public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion
        {
            internal readonly ValueTaskImpl<TResult> _value;

            internal ConfiguredValueTaskAwaiter(in ValueTaskImpl<TResult> value) => _value = value;

            public bool IsCompleted => _value.IsCompleted;

            public TResult GetResult() => _value.Result;

            public void OnCompleted(Action continuation)
            {
                object obj = _value._obj;
                if (obj != null && ValueTaskSupport.IsTask(obj))
                    CoreIntrinsics.ReinterpretAs<Task<TResult>>(obj).ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
                else if (obj != null)
                    ((IValueTaskSource<TResult>)obj).OnCompleted(ValueTaskSupport.s_invokeAction, continuation, _value._token,
                        (_value._continueOnCapturedContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None)
                        | ValueTaskSourceOnCompletedFlags.FlowExecutionContext);
                else Task.CompletedTask.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                object obj = _value._obj;
                if (obj != null && ValueTaskSupport.IsTask(obj))
                    CoreIntrinsics.ReinterpretAs<Task<TResult>>(obj).ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
                else if (obj != null)
                    ((IValueTaskSource<TResult>)obj).OnCompleted(ValueTaskSupport.s_invokeAction, continuation, _value._token,
                        _value._continueOnCapturedContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None);
                else Task.CompletedTask.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
            }
        }
    }
}
