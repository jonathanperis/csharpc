// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: System.Threading.Tasks.ConcurrentExclusiveSchedulerPair after dotnet/runtime release/11.0
// (ConcurrentExclusiveSchedulerPair.cs): tasks queued to ConcurrentScheduler run on the target scheduler, up to
// MaximumConcurrencyLevel at a time, while no exclusive task runs; tasks queued to ExclusiveScheduler run one at
// a time, with nothing concurrent, and before queued concurrent ones. Each worker is a task on the target
// scheduler that runs up to maxItemsPerTask queued tasks (TaskScheduler.Current is the pair's scheduler while
// one runs). Complete() stops new work; Completion finishes once the queued work has run. Differences of
// substance: a task is never inlined (TryExecuteTaskInline answers false), which only decides on which thread
// it runs.
#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.Tasks.ConcurrentExclusiveSchedulerPair")]
    public class ConcurrentExclusiveSchedulerPairImpl
    {
        private readonly object _lock = new object();
        private readonly TaskScheduler _target;
        private readonly int _maxConcurrencyLevel;
        private readonly int _maxItemsPerTask;
        private readonly PairScheduler _concurrent, _exclusive;
        // > 0: concurrent workers running; -1: the exclusive worker; 0: none.
        private int _processingCount;
        private bool _completionRequested;
        private TaskCompletionSource _completion;
        private List<Exception> _faults;

        private static int DefaultMaxConcurrencyLevel => Environment.ProcessorCount;

        public ConcurrentExclusiveSchedulerPairImpl() : this(TaskScheduler.Default, DefaultMaxConcurrencyLevel, -1) { }

        public ConcurrentExclusiveSchedulerPairImpl(TaskScheduler taskScheduler) : this(taskScheduler, DefaultMaxConcurrencyLevel, -1) { }

        public ConcurrentExclusiveSchedulerPairImpl(TaskScheduler taskScheduler, int maxConcurrencyLevel) : this(taskScheduler, maxConcurrencyLevel, -1) { }

        public ConcurrentExclusiveSchedulerPairImpl(TaskScheduler taskScheduler, int maxConcurrencyLevel, int maxItemsPerTask)
        {
            ArgumentNullException.ThrowIfNull(taskScheduler);
            if (maxConcurrencyLevel == 0 || maxConcurrencyLevel < -1) throw new ArgumentOutOfRangeException("maxConcurrencyLevel");
            if (maxItemsPerTask == 0 || maxItemsPerTask < -1) throw new ArgumentOutOfRangeException("maxItemsPerTask");
            _target = taskScheduler;
            _maxConcurrencyLevel = maxConcurrencyLevel == -1 ? int.MaxValue : maxConcurrencyLevel;
            // Never more concurrency than the target scheduler offers.
            int targetLevel = taskScheduler.MaximumConcurrencyLevel;
            if (targetLevel > 0 && targetLevel < _maxConcurrencyLevel) _maxConcurrencyLevel = targetLevel;
            _maxItemsPerTask = maxItemsPerTask == -1 ? int.MaxValue : maxItemsPerTask;
            _concurrent = new PairScheduler(this, _maxConcurrencyLevel, exclusive: false);
            _exclusive = new PairScheduler(this, 1, exclusive: true);
        }

        public TaskScheduler ConcurrentScheduler => (TaskScheduler)(object)_concurrent;
        public TaskScheduler ExclusiveScheduler => (TaskScheduler)(object)_exclusive;

        public void Complete()
        {
            lock (_lock)
            {
                if (_completionRequested) return;
                _completionRequested = true;
                CompleteIfDone();
            }
        }

        public Task Completion
        {
            get
            {
                lock (_lock) return CompletionSource.Task;
            }
        }

        private TaskCompletionSource CompletionSource => _completion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Under _lock.
        private void CompleteIfDone()
        {
            if (!_completionRequested || _processingCount != 0 || _exclusive.Queue.Count != 0 || _concurrent.Queue.Count != 0) return;
            if (_faults != null) CompletionSource.TrySetException(_faults);
            else CompletionSource.TrySetResult();
        }

        internal void Enqueue(PairScheduler scheduler, Task task)
        {
            lock (_lock)
            {
                if (_completionRequested) throw new InvalidOperationException("System.Threading.Tasks.ConcurrentExclusiveSchedulerPair+ConcurrentExclusiveTaskScheduler");
                scheduler.Queue.Enqueue(task);
                ProcessIfNecessary();
            }
        }

        // Under _lock: starts the workers the queues allow. Exclusive work goes first and alone.
        private void ProcessIfNecessary()
        {
            if (_processingCount == -1) return;
            if (_exclusive.Queue.Count > 0)
            {
                if (_processingCount == 0)
                {
                    _processingCount = -1;
                    if (!StartWorker(exclusive: true)) _processingCount = 0;
                }
                return;
            }
            int queued = _concurrent.Queue.Count;
            while (queued > 0 && _processingCount < _maxConcurrencyLevel)
            {
                _processingCount++;
                if (!StartWorker(exclusive: false)) { _processingCount--; break; }
                queued--;
            }
            CompleteIfDone();
        }

        private bool StartWorker(bool exclusive)
        {
            try
            {
                Task.Factory.StartNew(exclusive ? (Action)ProcessExclusive : ProcessConcurrent, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _target);
                return true;
            }
            catch (Exception e)
            {
                // The target scheduler refused the worker: the pair faults, as .NET's does.
                (_faults ??= new List<Exception>()).Add(e);
                _completionRequested = true;
                _exclusive.Queue.Clear();
                _concurrent.Queue.Clear();
                return false;
            }
        }

        private void ProcessExclusive()
        {
            for (int i = 0; i < _maxItemsPerTask; i++)
            {
                Task next;
                lock (_lock)
                {
                    if (_exclusive.Queue.Count == 0) break;
                    next = _exclusive.Queue.Dequeue();
                }
                _exclusive.Run(next);
            }
            lock (_lock)
            {
                _processingCount = 0;
                ProcessIfNecessary();
                CompleteIfDone();
            }
        }

        private void ProcessConcurrent()
        {
            for (int i = 0; i < _maxItemsPerTask; i++)
            {
                Task next;
                lock (_lock)
                {
                    // Waiting exclusive work stops the concurrent workers.
                    if (_exclusive.Queue.Count > 0 || _concurrent.Queue.Count == 0) break;
                    next = _concurrent.Queue.Dequeue();
                }
                _concurrent.Run(next);
            }
            lock (_lock)
            {
                _processingCount--;
                ProcessIfNecessary();
                CompleteIfDone();
            }
        }

        internal sealed class PairScheduler : TaskSchedulerImpl
        {
            private readonly ConcurrentExclusiveSchedulerPairImpl _pair;
            private readonly int _maxConcurrencyLevel;
            internal readonly Queue<Task> Queue = new Queue<Task>();

            internal PairScheduler(ConcurrentExclusiveSchedulerPairImpl pair, int maxConcurrencyLevel, bool exclusive)
            {
                _pair = pair;
                _maxConcurrencyLevel = maxConcurrencyLevel;
            }

            public override int MaximumConcurrencyLevel => _maxConcurrencyLevel;

            protected internal override void QueueTask(Task task) => _pair.Enqueue(this, task);

            protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

            protected override IEnumerable<Task> GetScheduledTasks()
            {
                lock (_pair._lock) return Queue.ToArray();
            }

            internal void Run(Task task) => TryExecuteTask(task);
        }
    }
}
