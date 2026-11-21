// The part of System.IO.Stream (taken from the framework's IL, upstream-types/System.IO.txt) that csharpc serves itself: the
// asynchronous defaults. Upstream runs them as Stream.ReadWriteTask, a Task<int> subclass scheduled through Task's internals
// (m_taskScheduler, ScheduleAndStart, AddCompletionAction) and serialized with a SemaphoreSlim, and finds out whether a derived stream
// overrides BeginRead/BeginWrite from the method table's auxiliary data. csharpc's tasks are the runtime's, so the slicer's csharpc
// profile replaces those members with these (tools/il-first/slicer/Program.cs). Behaviour is .NET's: one default operation at a time per
// stream, in order, on the thread pool; ReadAsync/WriteAsync go through BeginRead/BeginWrite, so a stream that overrides those is used
// through them (.NET does this only when they are overridden; the result is the same, the extra hop is not observable); the End methods
// accept only their own Begin's result, once.
#nullable disable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    public static class UpstreamStreamSupport
    {
        private const string BadAsyncResult =
            "Either the IAsyncResult object did not come from the corresponding async method on this type, or the End method was called multiple times with the same IAsyncResult.";

        /// <summary>The tail of each stream's chain of default asynchronous operations (upstream: Stream._asyncActiveSemaphore).</summary>
        private static readonly ConditionalWeakTable<Stream, Gate> s_gates = new();

        private sealed class Gate
        {
            internal Task Tail;
        }

        /// <summary>Stream.BeginRead.</summary>
        public static IAsyncResult BeginRead(object self, byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            var stream = (Stream)self;
            ValidateBufferArguments(buffer, offset, count);
            if (!stream.CanRead) throw new NotSupportedException("Stream does not support reading.");
            return new StreamAsyncResult(RunSerialized(stream, () => stream.Read(buffer, offset, count)), true, callback, state);
        }

        /// <summary>Stream.BeginWrite.</summary>
        public static IAsyncResult BeginWrite(object self, byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            var stream = (Stream)self;
            ValidateBufferArguments(buffer, offset, count);
            if (!stream.CanWrite) throw new NotSupportedException("Stream does not support writing.");
            return new StreamAsyncResult(RunSerialized(stream, () => { stream.Write(buffer, offset, count); return 0; }), false, callback, state);
        }

        /// <summary>Stream.EndRead.</summary>
        public static int EndRead(object self, IAsyncResult asyncResult) => End(asyncResult, true);

        /// <summary>Stream.EndWrite.</summary>
        public static void EndWrite(object self, IAsyncResult asyncResult) => End(asyncResult, false);

        /// <summary>Stream.BeginEndReadAsync: ReadAsync(byte[], int, int, CancellationToken)'s default, through the stream's BeginRead/EndRead.</summary>
        public static Task<int> BeginEndReadAsync(object self, byte[] buffer, int offset, int count)
        {
            var stream = (Stream)self;
            var completion = new TaskCompletionSource<int>();
            stream.BeginRead(buffer, offset, count, result =>
            {
                try { completion.SetResult(stream.EndRead(result)); }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception e) { completion.SetException(e); }
            }, null);
            return completion.Task;
        }

        /// <summary>Stream.BeginEndWriteAsync: WriteAsync(byte[], int, int, CancellationToken)'s default, through BeginWrite/EndWrite.</summary>
        public static Task BeginEndWriteAsync(object self, byte[] buffer, int offset, int count)
        {
            var stream = (Stream)self;
            var completion = new TaskCompletionSource();
            stream.BeginWrite(buffer, offset, count, result =>
            {
                try { stream.EndWrite(result); completion.SetResult(); }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception e) { completion.SetException(e); }
            }, null);
            return completion.Task;
        }

        /// <summary>Stream.Null's BeginRead: a completed read of 0 bytes (CoreLib: TaskToAsyncResult.Begin(Task&lt;int&gt;.s_defaultResultTask)).</summary>
        public static IAsyncResult NullStreamBeginRead(object self, byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (!((Stream)self).CanRead) throw new NotSupportedException("Stream does not support reading.");
            return TaskToAsyncResult.Begin(Task.FromResult(0), callback, state);
        }

        private static int End(IAsyncResult asyncResult, bool read)
        {
            if (asyncResult == null) throw new ArgumentNullException("asyncResult");
            if (asyncResult is not StreamAsyncResult result || result.IsRead != read) throw new ArgumentException(BadAsyncResult);
            if (result.EndCalled) throw new InvalidOperationException(BadAsyncResult);
            result.EndCalled = true;
            return result.Task.GetAwaiter().GetResult();
        }

        /// <summary>Runs a default asynchronous operation on the thread pool once the stream's previous one has finished.</summary>
        private static Task<int> RunSerialized(Stream stream, Func<int> operation)
        {
            var gate = s_gates.GetValue(stream, _ => new Gate());
            lock (gate)
            {
                Task previous = gate.Tail;
                Task<int> task = previous == null || previous.IsCompleted
                    ? Task.Factory.StartNew(operation, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default)
                    : previous.ContinueWith(_ => operation(), CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
                gate.Tail = task;
                return task;
            }
        }

        private static void ValidateBufferArguments(byte[] buffer, int offset, int count)
        {
            // Stream.ValidateBufferArguments' exceptions and texts (ThrowHelper: ExceptionArgument.buffer, NeedNonNegNum, InvalidOffLen).
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Non-negative number required.");
            if ((uint)count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count), "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }

        private sealed class StreamAsyncResult : IAsyncResult
        {
            internal readonly Task<int> Task;
            internal readonly bool IsRead;
            internal bool EndCalled;
            private readonly object _state;

            internal StreamAsyncResult(Task<int> task, bool isRead, AsyncCallback callback, object state)
            {
                Task = task;
                IsRead = isRead;
                _state = state;
                if (callback != null)
                    task.ContinueWith(_ => callback(this), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            public object AsyncState => _state;
            public bool CompletedSynchronously => false;
            public bool IsCompleted => Task.IsCompleted;
            public WaitHandle AsyncWaitHandle => ((IAsyncResult)Task).AsyncWaitHandle;
        }
    }
}
