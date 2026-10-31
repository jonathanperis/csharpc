// Parallel.ForEach over an enumerable or a partitioner (arrays go to the runtime's parallel loop,
// FunctionCodeGen.Threading.cs): like .NET's PartitionerForEach, a worker per processor drains the
// source's dynamic partitions; the first exception stops the workers from taking new elements, and every
// exception thrown surfaces in one AggregateException.
#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class ParallelForEachSupport
    {
        public static void ForEach<T>(IEnumerable<T> source, Action<T> body)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (body == null) throw new ArgumentNullException("body");
            Run(Partitioner.Create(source).GetDynamicPartitions(), body);
        }

        public static void ForEachPartitioner<T>(Partitioner<T> source, Action<T> body)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (body == null) throw new ArgumentNullException("body");
            if (!source.SupportsDynamicPartitions) throw new InvalidOperationException("The Partitioner used here must support dynamic partitioning.");
            IEnumerable<T> partitions = source.GetDynamicPartitions();
            if (partitions == null) throw new InvalidOperationException("The Partitioner used here returned a null partitioner source.");
            Run(partitions, body);
        }

        /// <summary>
        /// ForEach(source, ParallelOptions, body): at most MaxDegreeOfParallelism workers; a token canceled before
        /// the loop throws at once, one canceled while it runs stops new iterations and ends in
        /// OperationCanceledException (corelib/Tasks/ParallelLoops.cs has the same rules for For).
        /// </summary>
        public static ParallelLoopResultImpl ForEachOptions<T>(IEnumerable<T> source, ParallelOptions parallelOptions, Action<T> body)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (body == null) throw new ArgumentNullException("body");
            if (parallelOptions == null) throw new ArgumentNullException("parallelOptions");
            CancellationToken token = parallelOptions.CancellationToken;
            token.ThrowIfCancellationRequested();
            int degree = parallelOptions.MaxDegreeOfParallelism == -1 ? Math.Max(1, Environment.ProcessorCount) : parallelOptions.MaxDegreeOfParallelism;
            Run(Partitioner.Create(source).GetDynamicPartitions(), body, degree, token);
            token.ThrowIfCancellationRequested();
            return ParallelForSupport.Completed();
        }

        private static void Run<T>(IEnumerable<T> partitions, Action<T> body) =>
            Run(partitions, body, Math.Max(1, Environment.ProcessorCount), CancellationToken.None);

        private static void Run<T>(IEnumerable<T> partitions, Action<T> body, int degree, CancellationToken token)
        {
            var exceptions = new ConcurrentQueue<Exception>();
            int stop = 0;
            var workers = new Task[degree];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = Task.Run(() =>
                {
                    IEnumerator<T> partition;
                    try { partition = partitions.GetEnumerator(); }
                    catch (Exception e) { exceptions.Enqueue(e); Volatile.Write(ref stop, 1); return; }
                    using (partition)
                    {
                        while (Volatile.Read(ref stop) == 0 && !token.IsCancellationRequested)
                        {
                            try
                            {
                                if (!partition.MoveNext()) break;
                                body(partition.Current);
                            }
                            catch (Exception e)
                            {
                                exceptions.Enqueue(e);
                                Volatile.Write(ref stop, 1);
                            }
                        }
                    }
                });
            }
            foreach (Task worker in workers) worker.Wait(); // the workers catch everything: none faults
            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);
        }
    }
}
