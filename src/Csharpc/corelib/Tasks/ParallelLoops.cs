// Parallel.For with ParallelOptions, ParallelOptions and ParallelLoopResult. The overloads without
// options run on the runtime's parallel loop (FunctionCodeGen.Threading.cs); these take a token and a
// degree of parallelism, which Roslyn's RoslynParallel.For passes. As in .NET: a token canceled before the
// loop starts throws at once, body exceptions stop the loop from starting new iterations and surface in
// one AggregateException, and a cancellation seen while running ends in OperationCanceledException.
#nullable disable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.Tasks.ParallelOptions")]
    public class ParallelOptionsImpl
    {
        private TaskScheduler _scheduler;
        private int _maxDegreeOfParallelism = -1;
        private CancellationToken _cancellationToken = CancellationToken.None;

        public ParallelOptionsImpl() { _scheduler = TaskScheduler.Default; }

        public TaskScheduler TaskScheduler { get => _scheduler; set => _scheduler = value; }

        public int MaxDegreeOfParallelism
        {
            get => _maxDegreeOfParallelism;
            set
            {
                if (value == 0 || value < -1) throw new ArgumentOutOfRangeException("MaxDegreeOfParallelism");
                _maxDegreeOfParallelism = value;
            }
        }

        public CancellationToken CancellationToken { get => _cancellationToken; set => _cancellationToken = value; }
    }

    [Shadows("System.Threading.Tasks.ParallelLoopResult")]
    public struct ParallelLoopResultImpl
    {
        internal bool _completed;
        internal long? _lowestBreakIteration;

        public bool IsCompleted => _completed;
        public long? LowestBreakIteration => _lowestBreakIteration;
    }

    internal static class ParallelForSupport
    {
        /// <summary>The result of a loop that ran every iteration (the runtime loop's overloads).</summary>
        public static ParallelLoopResultImpl Completed()
        {
            ParallelLoopResultImpl result = default;
            result._completed = true;
            return result;
        }

        public static ParallelLoopResultImpl For(int fromInclusive, int toExclusive, ParallelOptions parallelOptions, Action<int> body)
        {
            if (body == null) throw new ArgumentNullException("body");
            if (parallelOptions == null) throw new ArgumentNullException("parallelOptions");
            return Run(fromInclusive, toExclusive, parallelOptions, i => body((int)i));
        }

        public static ParallelLoopResultImpl For64(long fromInclusive, long toExclusive, ParallelOptions parallelOptions, Action<long> body)
        {
            if (body == null) throw new ArgumentNullException("body");
            if (parallelOptions == null) throw new ArgumentNullException("parallelOptions");
            return Run(fromInclusive, toExclusive, parallelOptions, body);
        }

        private static ParallelLoopResultImpl Run(long fromInclusive, long toExclusive, ParallelOptions options, Action<long> body)
        {
            CancellationToken token = options.CancellationToken;
            token.ThrowIfCancellationRequested();
            if (fromInclusive >= toExclusive) return Completed();

            long next = fromInclusive;
            int stop = 0;
            var exceptions = new ConcurrentQueue<Exception>();
            long count = toExclusive - fromInclusive;
            int degree = options.MaxDegreeOfParallelism == -1 ? Math.Max(1, Environment.ProcessorCount) : options.MaxDegreeOfParallelism;
            if (count < degree) degree = (int)count;

            Action worker = () =>
            {
                while (Volatile.Read(ref stop) == 0 && !token.IsCancellationRequested)
                {
                    long i = Interlocked.Increment(ref next) - 1;
                    if (i >= toExclusive) break;
                    try { body(i); }
                    catch (Exception e)
                    {
                        exceptions.Enqueue(e);
                        Volatile.Write(ref stop, 1);
                    }
                }
            };
            // The calling thread takes part, as .NET's loop runs its first task inline.
            var helpers = new Task[degree - 1];
            for (int w = 0; w < helpers.Length; w++) helpers[w] = Task.Run(worker);
            worker();
            if (helpers.Length > 0) Task.WaitAll(helpers);

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);
            token.ThrowIfCancellationRequested();
            return Completed();
        }
    }
}
