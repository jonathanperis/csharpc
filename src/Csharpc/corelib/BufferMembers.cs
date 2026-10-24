// System.Buffer.BlockCopy/ByteLength (byte offsets into arrays of primitives: System.Net.Http's and
// ASP.NET Core's WebUtilities buffers) and System.Threading.Tasks.TaskToAsyncResult (the APM wrappers
// Kestrel's and System.Net.Http's streams build BeginRead/EndRead from).
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Buffer")]
    internal static unsafe class BufferMembers
    {
        public static void BlockCopy(Array src, int srcOffset, Array dst, int dstOffset, int count)
        {
            ArgumentNullException.ThrowIfNull(src);
            ArgumentNullException.ThrowIfNull(dst);
            Span<byte> source = Bytes(src, "src");
            Span<byte> destination = src == dst ? source : Bytes(dst, "dst");
            ArgumentOutOfRangeException.ThrowIfNegative(srcOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(dstOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if ((ulong)source.Length < (ulong)srcOffset + (ulong)count || (ulong)destination.Length < (ulong)dstOffset + (ulong)count)
                throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            // Span.CopyTo handles overlapping ranges of one array, as Buffer.Memmove does.
            source.Slice(srcOffset, count).CopyTo(destination.Slice(dstOffset, count));
        }

        public static int ByteLength(Array array)
        {
            ArgumentNullException.ThrowIfNull(array);
            return Bytes(array, "array").Length;
        }

        // The array's elements as bytes (csharpc's collector never moves objects, so the span stays valid).
        private static Span<byte> Bytes(Array array, string paramName)
        {
            switch (array)
            {
                case byte[] a: return a;
                case sbyte[] a: fixed (sbyte* p = a) return new Span<byte>(p, a.Length);
                case bool[] a: fixed (bool* p = a) return new Span<byte>(p, a.Length);
                case char[] a: fixed (char* p = a) return new Span<byte>(p, a.Length * 2);
                case short[] a: fixed (short* p = a) return new Span<byte>(p, a.Length * 2);
                case ushort[] a: fixed (ushort* p = a) return new Span<byte>(p, a.Length * 2);
                case int[] a: fixed (int* p = a) return new Span<byte>(p, a.Length * 4);
                case uint[] a: fixed (uint* p = a) return new Span<byte>(p, a.Length * 4);
                case float[] a: fixed (float* p = a) return new Span<byte>(p, a.Length * 4);
                case long[] a: fixed (long* p = a) return new Span<byte>(p, a.Length * 8);
                case ulong[] a: fixed (ulong* p = a) return new Span<byte>(p, a.Length * 8);
                case double[] a: fixed (double* p = a) return new Span<byte>(p, a.Length * 8);
                case nint[] a: fixed (nint* p = a) return new Span<byte>(p, a.Length * sizeof(nint));
                case nuint[] a: fixed (nuint* p = a) return new Span<byte>(p, a.Length * sizeof(nuint));
                default: throw new ArgumentException("Object must be an array of primitives.", paramName);
            }
        }
    }

    [Shadows("System.Threading.Tasks.TaskToAsyncResult")]
    public static class TaskToAsyncResultImpl
    {
        public static IAsyncResult Begin(Task task, AsyncCallback callback, object state)
        {
            ArgumentNullException.ThrowIfNull(task);
            return new TaskAsyncResult(task, state, callback);
        }

        public static void End(IAsyncResult asyncResult) => Unwrap(asyncResult).GetAwaiter().GetResult();

        public static TResult End<TResult>(IAsyncResult asyncResult) => Unwrap<TResult>(asyncResult).GetAwaiter().GetResult();

        public static Task Unwrap(IAsyncResult asyncResult)
        {
            ArgumentNullException.ThrowIfNull(asyncResult);
            if (!(asyncResult is TaskAsyncResult result)) throw new ArgumentException(null, "asyncResult");
            return result.Task;
        }

        // csharpc can't test a task's result type at run time: a Begin(Task<T>) result is assumed here.
        public static Task<TResult> Unwrap<TResult>(IAsyncResult asyncResult) => Unsafe.As<Task<TResult>>(Unwrap(asyncResult));

        private sealed class TaskAsyncResult : IAsyncResult
        {
            internal readonly Task Task;
            private readonly object _state;

            internal TaskAsyncResult(Task task, object state, AsyncCallback callback)
            {
                Task = task;
                _state = state;
                if (task.IsCompleted)
                {
                    CompletedSynchronously = true;
                    callback?.Invoke(this);
                }
                else if (callback != null)
                    task.ContinueWith(_ => callback(this), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            public object AsyncState => _state;
            public bool CompletedSynchronously { get; }
            public bool IsCompleted => Task.IsCompleted;
            public WaitHandle AsyncWaitHandle => throw new NotSupportedException("IAsyncResult.AsyncWaitHandle of TaskToAsyncResult is not supported by csharpc yet.");
        }
    }
}
