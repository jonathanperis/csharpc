// ParallelLoopState and the Parallel loops that take it: For / ForEach with a loop state (Stop, Break,
// LowestBreakIteration, IsStopped, IsExceptional, ShouldExitCurrentIteration), an iteration index, thread-local
// state (localInit / localFinally) and ParallelOptions; ForEach over partitioners (OrderablePartitioner for the
// index); Invoke with options; ForEachAsync / ForAsync. After dotnet/runtime's Parallel.cs and
// ParallelLoopState.cs (release/11.0): the loop flags are one atomic word (exceptional, broken, stopped,
// canceled) plus the lowest broken iteration; an iteration above a Break's index is not started, one below it
// still runs; Stop starts no new iteration at all; a body exception stops the loop and every exception
// surfaces in one AggregateException; a canceled token ends in OperationCanceledException. The plain
// overloads (no state) run on the runtime's loop (FunctionCodeGen.Threading.cs, ParallelLoops.cs,
// ParallelForEach.cs). csharpc: workers are pool tasks plus the calling thread, claiming indices one by one
// rather than in .NET's growing ranges, so which iterations a Break lets through above its index differs.
#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal sealed class ParallelLoopFlags
    {
        internal const int None = 0, Exceptional = 1, Broken = 2, Stopped = 4, Canceled = 8;

        internal int Value;
        internal long LowestBreak = long.MaxValue;

        /// <summary>Sets `flag` unless one of `illegal` is set (then false, with the flags seen).</summary>
        internal bool TryUpdate(int flag, int illegal, out int seen)
        {
            while (true)
            {
                seen = Volatile.Read(ref Value);
                if ((seen & illegal) != 0) return false;
                if (Interlocked.CompareExchange(ref Value, seen | flag, seen) == seen) return true;
            }
        }

        internal void Set(int flag) { TryUpdate(flag, 0, out _); }

        /// <summary>No new iteration may start: the loop failed, was stopped or was canceled.</summary>
        internal bool Halted => (Volatile.Read(ref Value) & (Exceptional | Stopped | Canceled)) != 0;

        internal bool ShouldExit(long iteration)
        {
            int flags = Volatile.Read(ref Value);
            return flags != None && ((flags & (Exceptional | Stopped | Canceled)) != 0
                || ((flags & Broken) != 0 && iteration > Volatile.Read(ref LowestBreak)));
        }

        internal ParallelLoopResultImpl Result()
        {
            ParallelLoopResultImpl result = default;
            int flags = Volatile.Read(ref Value);
            result._completed = flags == None;
            if ((flags & Broken) != 0) result._lowestBreakIteration = Volatile.Read(ref LowestBreak);
            return result;
        }
    }

    [Shadows("System.Threading.Tasks.ParallelLoopState")]
    public class ParallelLoopStateImpl
    {
        internal readonly ParallelLoopFlags _flags;
        internal long _currentIteration;

        internal ParallelLoopStateImpl(ParallelLoopFlags flags) { _flags = flags; }

        public bool IsStopped => (Volatile.Read(ref _flags.Value) & ParallelLoopFlags.Stopped) != 0;

        public bool IsExceptional => (Volatile.Read(ref _flags.Value) & ParallelLoopFlags.Exceptional) != 0;

        public bool ShouldExitCurrentIteration => _flags.ShouldExit(_currentIteration);

        public long? LowestBreakIteration
        {
            get
            {
                long lowest = Volatile.Read(ref _flags.LowestBreak);
                return lowest == long.MaxValue ? null : lowest;
            }
        }

        public void Stop()
        {
            if (!_flags.TryUpdate(ParallelLoopFlags.Stopped, ParallelLoopFlags.Broken, out _))
                throw new InvalidOperationException("Stop was called after Break was called.");
        }

        public void Break()
        {
            if (!_flags.TryUpdate(ParallelLoopFlags.Broken, ParallelLoopFlags.Stopped | ParallelLoopFlags.Exceptional | ParallelLoopFlags.Canceled, out int seen))
            {
                if ((seen & ParallelLoopFlags.Stopped) != 0) throw new InvalidOperationException("Break was called after Stop was called.");
                return; // the loop failed or was canceled anyway
            }
            long mine = _currentIteration;
            long lowest;
            while (mine < (lowest = Volatile.Read(ref _flags.LowestBreak)))
                if (Interlocked.CompareExchange(ref _flags.LowestBreak, mine, lowest) == lowest) break;
        }
    }

    internal static class ParallelStateLoops
    {
        private static int Degree(ParallelOptions options) =>
            options.MaxDegreeOfParallelism == -1 ? Math.Max(1, Environment.ProcessorCount) : options.MaxDegreeOfParallelism;

        private static readonly ParallelOptions s_defaults = new ParallelOptions();

        /// <summary>Runs `worker` on `degree` threads (the calling one included) and rethrows what the loop collected.</summary>
        private static void RunWorkers(int degree, Action worker)
        {
            var helpers = new Task[degree - 1];
            for (int w = 0; w < helpers.Length; w++) helpers[w] = Task.Run(worker);
            worker();
            if (helpers.Length > 0) Task.WaitAll(helpers);
        }

        private static void Finish(ParallelLoopFlags flags, ConcurrentQueue<Exception> exceptions, CancellationToken token)
        {
            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
        }

        private static void Fail(ParallelLoopFlags flags, ConcurrentQueue<Exception> exceptions, Exception e)
        {
            exceptions.Enqueue(e);
            flags.Set(ParallelLoopFlags.Exceptional);
        }

        // ---- For ---------------------------------------------------------------------------------------------

        public static ParallelLoopResultImpl For<TLocal>(long from, long to, ParallelOptions options,
            Func<TLocal> localInit, Func<long, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            CancellationToken token = options.CancellationToken;
            token.ThrowIfCancellationRequested();
            var flags = new ParallelLoopFlags();
            if (from >= to) return flags.Result();
            var exceptions = new ConcurrentQueue<Exception>();
            long next = from;
            long count = to - from;
            int degree = Degree(options);
            if (count < degree) degree = (int)count;
            CancellationTokenRegistration registration = token.CanBeCanceled ? token.Register(() => flags.Set(ParallelLoopFlags.Canceled)) : default;
            Action worker = () =>
            {
                var state = new ParallelLoopStateImpl(flags);
                ParallelLoopState exposed = (ParallelLoopState)(object)state;
                TLocal local = default;
                bool started = false;
                try
                {
                    while (!flags.Halted)
                    {
                        long i = Interlocked.Increment(ref next) - 1;
                        if (i >= to || i < from) break; // (overflow past long.MaxValue ends the loop too)
                        if (flags.ShouldExit(i)) break;
                        if (!started) { started = true; if (localInit != null) local = localInit(); }
                        state._currentIteration = i;
                        local = body(i, exposed, local);
                    }
                }
                catch (Exception e) { Fail(flags, exceptions, e); }
                finally
                {
                    if (started && localFinally != null)
                        try { localFinally(local); } catch (Exception e) { Fail(flags, exceptions, e); }
                }
            };
            try { RunWorkers(degree, worker); }
            finally { registration.Dispose(); }
            Finish(flags, exceptions, token);
            return flags.Result();
        }

        public static ParallelLoopResultImpl For(long from, long to, ParallelOptions options, Action<long, ParallelLoopState> body) =>
            For<object>(from, to, options, null, (i, s, l) => { body(i, s); return l; }, null);

        // ---- ForEach over a list or array (the index is the position) ------------------------------------------

        public static ParallelLoopResultImpl ForEachList<TSource, TLocal>(IList<TSource> source, ParallelOptions options,
            Func<TLocal> localInit, Func<TSource, ParallelLoopState, long, TLocal, TLocal> body, Action<TLocal> localFinally) =>
            For<TLocal>(0, source.Count, options, localInit, (i, s, l) => body(source[(int)i], s, i, l), localFinally);

        // ---- ForEach over an enumerable or a partitioner ---------------------------------------------------------

        /// <summary>
        /// The workers drain a partitioner's dynamic partitions (an orderable one: with its keys as iteration
        /// indices, which Break and the index overloads need; a plain one: every iteration is index 0).
        /// </summary>
        public static ParallelLoopResultImpl ForEachPartitioned<TSource, TLocal>(Partitioner<TSource> source, ParallelOptions options, bool needsKeys,
            Func<TLocal> localInit, Func<TSource, ParallelLoopState, long, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            var ordered = source as OrderablePartitioner<TSource>;
            if (needsKeys && ordered != null && !ordered.KeysNormalized)
                throw new InvalidOperationException("This method requires the use of an OrderedPartitioner with the KeysNormalized property set to true.");
            CancellationToken token = options.CancellationToken;
            token.ThrowIfCancellationRequested();
            if (!source.SupportsDynamicPartitions) throw new InvalidOperationException("The Partitioner must support dynamic partitioning.");
            IEnumerable<KeyValuePair<long, TSource>> keyed = null;
            IEnumerable<TSource> plain = null;
            if (ordered != null)
            {
                keyed = ordered.GetOrderableDynamicPartitions();
                if (keyed == null) throw new InvalidOperationException("The Partitioner used here returned a null partitioner source.");
            }
            else
            {
                plain = source.GetDynamicPartitions();
                if (plain == null) throw new InvalidOperationException("The Partitioner used here returned a null partitioner source.");
            }
            var flags = new ParallelLoopFlags();
            var exceptions = new ConcurrentQueue<Exception>();
            int degree = Degree(options);
            CancellationTokenRegistration registration = token.CanBeCanceled ? token.Register(() => flags.Set(ParallelLoopFlags.Canceled)) : default;
            Action worker = () =>
            {
                var state = new ParallelLoopStateImpl(flags);
                ParallelLoopState exposed = (ParallelLoopState)(object)state;
                TLocal local = default;
                bool started = false;
                try
                {
                    if (keyed != null)
                    {
                        using (IEnumerator<KeyValuePair<long, TSource>> partition = keyed.GetEnumerator())
                            while (!flags.Halted && partition.MoveNext())
                            {
                                KeyValuePair<long, TSource> item = partition.Current;
                                if (!started) { started = true; if (localInit != null) local = localInit(); }
                                state._currentIteration = item.Key;
                                local = body(item.Value, exposed, item.Key, local);
                                // .NET looks at a Break after the body, so the first item of a chunk past it still runs.
                                if (flags.ShouldExit(item.Key)) break;
                            }
                    }
                    else
                    {
                        using (IEnumerator<TSource> partition = plain.GetEnumerator())
                            while (!flags.Halted && partition.MoveNext())
                            {
                                if (!started) { started = true; if (localInit != null) local = localInit(); }
                                local = body(partition.Current, exposed, 0, local);
                            }
                    }
                }
                catch (Exception e) { Fail(flags, exceptions, e); }
                finally
                {
                    if (started && localFinally != null)
                        try { localFinally(local); } catch (Exception e) { Fail(flags, exceptions, e); }
                }
            };
            try { RunWorkers(degree, worker); }
            finally { registration.Dispose(); }
            Finish(flags, exceptions, token);
            return flags.Result();
        }

        /// <summary>ForEach(IEnumerable): arrays and lists loop over their positions, anything else over its own partitions.</summary>
        public static ParallelLoopResultImpl ForEachSource<TSource, TLocal>(IEnumerable<TSource> source, ParallelOptions options,
            Func<TLocal> localInit, Func<TSource, ParallelLoopState, long, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            if (source is IList<TSource> list) return ForEachList(list, options, localInit, body, localFinally);
            return ForEachPartitioned<TSource, TLocal>(Partitioner.Create(source), options, true, localInit, body, localFinally);
        }

        // ---- Invoke ------------------------------------------------------------------------------------------------

        public static void Invoke(ParallelOptions options, Action[] actions)
        {
            if (options == null) throw new ArgumentNullException("parallelOptions");
            if (actions == null) throw new ArgumentNullException("actions");
            foreach (Action action in actions)
                if (action == null) throw new ArgumentException("One of the actions was null.");
            options.CancellationToken.ThrowIfCancellationRequested();
            if (actions.Length == 0) return;
            var exceptions = new ConcurrentQueue<Exception>();
            long next = -1;
            int degree = Math.Min(Degree(options), actions.Length);
            Action worker = () =>
            {
                long i;
                while ((i = Interlocked.Increment(ref next)) < actions.Length && !options.CancellationToken.IsCancellationRequested)
                {
                    try { actions[i](); }
                    catch (Exception e) { exceptions.Enqueue(e); }
                }
            };
            RunWorkers(degree, worker);
            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);
            options.CancellationToken.ThrowIfCancellationRequested();
        }

        // ---- ForEachAsync / ForAsync --------------------------------------------------------------------------------

        public static Task ForEachAsync<T>(IEnumerable<T> source, ParallelOptions options, Func<T, CancellationToken, ValueTask> body)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (options == null) throw new ArgumentNullException("parallelOptions");
            if (body == null) throw new ArgumentNullException("body");
            if (options.CancellationToken.IsCancellationRequested) return Task.FromCanceled(options.CancellationToken);
            IEnumerator<T> enumerator = source.GetEnumerator();
            return Dispatch(options, () =>
            {
                lock (enumerator)
                {
                    if (!enumerator.MoveNext()) return default((bool, T));
                    return (true, enumerator.Current);
                }
            }, body, enumerator);
        }

        public static Task ForAsync<T>(T fromInclusive, T toExclusive, ParallelOptions options, Func<T, CancellationToken, ValueTask> body)
            where T : notnull, IBinaryInteger<T>
        {
            if (fromInclusive == null) throw new ArgumentNullException("fromInclusive");
            if (toExclusive == null) throw new ArgumentNullException("toExclusive");
            if (options == null) throw new ArgumentNullException("parallelOptions");
            if (body == null) throw new ArgumentNullException("body");
            if (options.CancellationToken.IsCancellationRequested) return Task.FromCanceled(options.CancellationToken);
            T current = fromInclusive;
            var gate = new object();
            return Dispatch<T>(options, () =>
            {
                lock (gate)
                {
                    if (current >= toExclusive) return default((bool, T));
                    T value = current;
                    current++;
                    return (true, value);
                }
            }, body, null);
        }

        /// <summary>
        /// The workers of an asynchronous loop: each takes the next item, awaits its body and goes on; a failure stops the
        /// others from taking items and cancels the token the bodies see; the task is canceled when the options' token
        /// is, faulted with every worker's exception otherwise.
        /// </summary>
        private static Task Dispatch<T>(ParallelOptions options, Func<(bool, T)> next, Func<T, CancellationToken, ValueTask> body, IDisposable resource)
        {
            int degree = Degree(options);
            CancellationToken userToken = options.CancellationToken;
            var linked = CancellationTokenSource.CreateLinkedTokenSource(userToken);
            var exceptions = new ConcurrentQueue<Exception>();
            var completion = new TaskCompletionSource();
            async Task Worker()
            {
                await Task.Yield();
                while (!linked.IsCancellationRequested)
                {
                    (bool has, T item) taken;
                    try { taken = next(); }
                    catch (Exception e) { exceptions.Enqueue(e); linked.Cancel(); return; }
                    if (!taken.has) return;
                    try { await body(taken.item, linked.Token).ConfigureAwait(false); }
                    catch (Exception e)
                    {
                        if (!(e is OperationCanceledException && linked.IsCancellationRequested)) exceptions.Enqueue(e);
                        linked.Cancel();
                        return;
                    }
                }
            }
            async Task Run()
            {
                try
                {
                    var workers = new Task[degree];
                    for (int w = 0; w < workers.Length; w++) workers[w] = Worker();
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                finally { resource?.Dispose(); linked.Dispose(); }
                if (!exceptions.IsEmpty) completion.SetException(exceptions);
                else if (userToken.IsCancellationRequested) completion.SetCanceled(userToken);
                else completion.SetResult();
            }
            _ = Run();
            return completion.Task;
        }
    }

    // The overloads of Parallel that take a loop state, an index, a partitioner or options, each a thin adapter over ParallelStateLoops.
    [ShadowMembers("System.Threading.Tasks.Parallel")]
    internal static class ParallelStateMembers
    {
        private static void Need(object value, string name) { if (value == null) throw new ArgumentNullException(name); }

        // ---- For (int and long) ---------------------------------------------------------------------------------

        public static ParallelLoopResultImpl For(int fromInclusive, int toExclusive, Action<int, ParallelLoopState> body)
        {
            Need(body, "body");
            return Result(ParallelStateLoops.For(fromInclusive, toExclusive, new ParallelOptions(), (i, s) => body((int)i, s)));
        }

        public static ParallelLoopResultImpl For(long fromInclusive, long toExclusive, Action<long, ParallelLoopState> body)
        {
            Need(body, "body");
            return Result(ParallelStateLoops.For(fromInclusive, toExclusive, new ParallelOptions(), body));
        }

        public static ParallelLoopResultImpl For(int fromInclusive, int toExclusive, ParallelOptions parallelOptions, Action<int, ParallelLoopState> body)
        {
            Need(body, "body");
            Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.For(fromInclusive, toExclusive, parallelOptions, (i, s) => body((int)i, s)));
        }

        public static ParallelLoopResultImpl For(long fromInclusive, long toExclusive, ParallelOptions parallelOptions, Action<long, ParallelLoopState> body)
        {
            Need(body, "body");
            Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.For(fromInclusive, toExclusive, parallelOptions, body));
        }

        public static ParallelLoopResultImpl For<TLocal>(int fromInclusive, int toExclusive, Func<TLocal> localInit,
            Func<int, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            NeedLocal(body, localInit, localFinally);
            return Result(ParallelStateLoops.For<TLocal>(fromInclusive, toExclusive, new ParallelOptions(), localInit, (i, s, l) => body((int)i, s, l), localFinally));
        }

        public static ParallelLoopResultImpl For<TLocal>(long fromInclusive, long toExclusive, Func<TLocal> localInit,
            Func<long, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            NeedLocal(body, localInit, localFinally);
            return Result(ParallelStateLoops.For<TLocal>(fromInclusive, toExclusive, new ParallelOptions(), localInit, body, localFinally));
        }

        public static ParallelLoopResultImpl For<TLocal>(int fromInclusive, int toExclusive, ParallelOptions parallelOptions, Func<TLocal> localInit,
            Func<int, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            NeedLocal(body, localInit, localFinally);
            Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.For<TLocal>(fromInclusive, toExclusive, parallelOptions, localInit, (i, s, l) => body((int)i, s, l), localFinally));
        }

        public static ParallelLoopResultImpl For<TLocal>(long fromInclusive, long toExclusive, ParallelOptions parallelOptions, Func<TLocal> localInit,
            Func<long, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            NeedLocal(body, localInit, localFinally);
            Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.For<TLocal>(fromInclusive, toExclusive, parallelOptions, localInit, body, localFinally));
        }

        private static void NeedLocal(object body, object localInit, object localFinally)
        {
            Need(body, "body");
            Need(localInit, "localInit");
            Need(localFinally, "localFinally");
        }

        // (The result is csharpc's ParallelLoopResultImpl: return types are not compared when a call is mapped.)
        private static ParallelLoopResultImpl Result(ParallelLoopResultImpl result) => result;

        // ---- ForEach over an IEnumerable -----------------------------------------------------------------------

        public static ParallelLoopResultImpl ForEach<TSource>(IEnumerable<TSource> source, Action<TSource, ParallelLoopState> body)
        {
            Need(source, "source"); Need(body, "body");
            return Result(ParallelStateLoops.ForEachSource<TSource, object>(source, new ParallelOptions(), null, (x, s, i, l) => { body(x, s); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource>(IEnumerable<TSource> source, Action<TSource, ParallelLoopState, long> body)
        {
            Need(source, "source"); Need(body, "body");
            return Result(ParallelStateLoops.ForEachSource<TSource, object>(source, new ParallelOptions(), null, (x, s, i, l) => { body(x, s, i); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Action<TSource, ParallelLoopState> body)
        {
            Need(source, "source"); Need(body, "body"); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachSource<TSource, object>(source, parallelOptions, null, (x, s, i, l) => { body(x, s); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Action<TSource, ParallelLoopState, long> body)
        {
            Need(source, "source"); Need(body, "body"); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachSource<TSource, object>(source, parallelOptions, null, (x, s, i, l) => { body(x, s, i); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(IEnumerable<TSource> source, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally);
            return Result(ParallelStateLoops.ForEachSource<TSource, TLocal>(source, new ParallelOptions(), localInit, (x, s, i, l) => body(x, s, l), localFinally));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(IEnumerable<TSource> source, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, long, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally);
            return Result(ParallelStateLoops.ForEachSource<TSource, TLocal>(source, new ParallelOptions(), localInit, body, localFinally));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachSource<TSource, TLocal>(source, parallelOptions, localInit, (x, s, i, l) => body(x, s, l), localFinally));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, long, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachSource<TSource, TLocal>(source, parallelOptions, localInit, body, localFinally));
        }

        // ---- ForEach over a Partitioner / OrderablePartitioner ---------------------------------------------------

        public static ParallelLoopResultImpl ForEach<TSource>(Partitioner<TSource> source, Action<TSource, ParallelLoopState> body)
        {
            Need(source, "source"); Need(body, "body");
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, object>(source, new ParallelOptions(), false, null, (x, s, i, l) => { body(x, s); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource>(Partitioner<TSource> source, ParallelOptions parallelOptions, Action<TSource> body)
        {
            Need(source, "source"); Need(body, "body"); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, object>(source, parallelOptions, false, null, (x, s, i, l) => { body(x); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource>(Partitioner<TSource> source, ParallelOptions parallelOptions, Action<TSource, ParallelLoopState> body)
        {
            Need(source, "source"); Need(body, "body"); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, object>(source, parallelOptions, false, null, (x, s, i, l) => { body(x, s); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource>(OrderablePartitioner<TSource> source, Action<TSource, ParallelLoopState, long> body)
        {
            Need(source, "source"); Need(body, "body");
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, object>(source, new ParallelOptions(), true, null, (x, s, i, l) => { body(x, s, i); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource>(OrderablePartitioner<TSource> source, ParallelOptions parallelOptions, Action<TSource, ParallelLoopState, long> body)
        {
            Need(source, "source"); Need(body, "body"); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, object>(source, parallelOptions, true, null, (x, s, i, l) => { body(x, s, i); return l; }, null));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(Partitioner<TSource> source, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally);
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, TLocal>(source, new ParallelOptions(), false, localInit, (x, s, i, l) => body(x, s, l), localFinally));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(Partitioner<TSource> source, ParallelOptions parallelOptions, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, TLocal>(source, parallelOptions, false, localInit, (x, s, i, l) => body(x, s, l), localFinally));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(OrderablePartitioner<TSource> source, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, long, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally);
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, TLocal>(source, new ParallelOptions(), true, localInit, body, localFinally));
        }

        public static ParallelLoopResultImpl ForEach<TSource, TLocal>(OrderablePartitioner<TSource> source, ParallelOptions parallelOptions, Func<TLocal> localInit,
            Func<TSource, ParallelLoopState, long, TLocal, TLocal> body, Action<TLocal> localFinally)
        {
            Need(source, "source"); NeedLocal(body, localInit, localFinally); Need(parallelOptions, "parallelOptions");
            return Result(ParallelStateLoops.ForEachPartitioned<TSource, TLocal>(source, parallelOptions, true, localInit, body, localFinally));
        }

        // ---- Invoke, ForEachAsync, ForAsync --------------------------------------------------------------------------

        public static void Invoke(ParallelOptions parallelOptions, params Action[] actions) => ParallelStateLoops.Invoke(parallelOptions, actions);

        public static Task ForEachAsync<TSource>(IEnumerable<TSource> source, Func<TSource, CancellationToken, ValueTask> body) =>
            ParallelStateLoops.ForEachAsync(source, new ParallelOptions(), body);

        public static Task ForEachAsync<TSource>(IEnumerable<TSource> source, CancellationToken cancellationToken, Func<TSource, CancellationToken, ValueTask> body) =>
            ParallelStateLoops.ForEachAsync(source, new ParallelOptions { CancellationToken = cancellationToken }, body);

        public static Task ForEachAsync<TSource>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Func<TSource, CancellationToken, ValueTask> body) =>
            ParallelStateLoops.ForEachAsync(source, parallelOptions, body);

        public static Task ForAsync<T>(T fromInclusive, T toExclusive, Func<T, CancellationToken, ValueTask> body) where T : notnull, IBinaryInteger<T> =>
            ParallelStateLoops.ForAsync(fromInclusive, toExclusive, new ParallelOptions(), body);

        public static Task ForAsync<T>(T fromInclusive, T toExclusive, CancellationToken cancellationToken, Func<T, CancellationToken, ValueTask> body) where T : notnull, IBinaryInteger<T> =>
            ParallelStateLoops.ForAsync(fromInclusive, toExclusive, new ParallelOptions { CancellationToken = cancellationToken }, body);

        public static Task ForAsync<T>(T fromInclusive, T toExclusive, ParallelOptions parallelOptions, Func<T, CancellationToken, ValueTask> body) where T : notnull, IBinaryInteger<T> =>
            ParallelStateLoops.ForAsync(fromInclusive, toExclusive, parallelOptions, body);
    }
}
