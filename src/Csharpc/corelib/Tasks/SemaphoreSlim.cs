// System.Threading.SemaphoreSlim, after dotnet/runtime's SemaphoreSlim.cs (release/11.0, MIT): a count
// under a monitor; synchronous waiters wait on it, asynchronous ones are a FIFO list of tasks that
// Release completes once no synchronous waiter takes the count. Timeouts and cancellation of an
// asynchronous wait race the waiter task against Task.Delay, as .NET does. (csharpc: the waiters are
// TaskCompletionSources rather than Task subclasses; no spinning before blocking; AvailableWaitHandle is
// not supported yet.)
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.SemaphoreSlim")]
    public class SemaphoreSlimImpl : IDisposable
    {
        private volatile int _currentCount;
        private readonly int _maxCount;
        private int _waitCount;
        private int _countOfWaitersPulsedToWake;
        private readonly object _lock = new object();
        private bool _disposed;
        private readonly LinkedList<TaskCompletionSource<bool>> _asyncWaiters = new LinkedList<TaskCompletionSource<bool>>();

        private static Task<bool> s_trueTask, s_falseTask;
        private static Task<bool> TrueTask => s_trueTask ??= Task.FromResult(true);
        private static Task<bool> FalseTask => s_falseTask ??= Task.FromResult(false);

        public SemaphoreSlimImpl(int initialCount) : this(initialCount, int.MaxValue) { }

        public SemaphoreSlimImpl(int initialCount, int maxCount)
        {
            if (initialCount < 0 || initialCount > maxCount)
                throw new ArgumentOutOfRangeException("initialCount", initialCount, "The initialCount argument must be non-negative and less than or equal to the maximumCount.");
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException("maxCount", maxCount, "The maximumCount argument must be a positive number. If a maximum is not required, use the constructor without a maxCount parameter.");
            _maxCount = maxCount;
            _currentCount = initialCount;
        }

        public int CurrentCount => _currentCount;

        public WaitHandle AvailableWaitHandle => throw new NotSupportedException("SemaphoreSlim.AvailableWaitHandle is not supported by csharpc yet.");

        private void CheckDispose()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private static int Milliseconds(TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException("timeout", timeout, "The value needs to translate in milliseconds to -1 (signifying an infinite timeout), or be non-negative.");
            return (int)totalMilliseconds;
        }

        private static void CheckTimeout(int millisecondsTimeout)
        {
            if (millisecondsTimeout < -1)
                throw new ArgumentOutOfRangeException("millisecondsTimeout", millisecondsTimeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
        }

        public void Wait() => WaitCore(Timeout.Infinite, CancellationToken.None);
        public void Wait(CancellationToken cancellationToken) => WaitCore(Timeout.Infinite, cancellationToken);
        public bool Wait(TimeSpan timeout) => WaitCore(Milliseconds(timeout), CancellationToken.None);
        public bool Wait(TimeSpan timeout, CancellationToken cancellationToken) => WaitCore(Milliseconds(timeout), cancellationToken);

        public bool Wait(int millisecondsTimeout)
        {
            CheckTimeout(millisecondsTimeout);
            return WaitCore(millisecondsTimeout, CancellationToken.None);
        }

        public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckTimeout(millisecondsTimeout);
            return WaitCore(millisecondsTimeout, cancellationToken);
        }

        private bool WaitCore(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckDispose();
            cancellationToken.ThrowIfCancellationRequested();
            if (millisecondsTimeout == 0 && _currentCount == 0) return false;
            long startTime = millisecondsTimeout > 0 ? Environment.TickCount64 : 0;
            Task<bool> asyncWaitTask = null;
            bool waitSuccessful = false;
            // A canceled token wakes the waiters so this one can throw.
            CancellationTokenRegistration registration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(static s => { var self = (SemaphoreSlimImpl)s; lock (self._lock) Monitor.PulseAll(self._lock); }, this)
                : default;
            try
            {
                lock (_lock)
                {
                    _waitCount++;
                    try
                    {
                        // Asynchronous waiters were first: this one waits behind them.
                        if (_asyncWaiters.Count > 0) asyncWaitTask = WaitAsyncCore(millisecondsTimeout, cancellationToken);
                        else
                        {
                            OperationCanceledException oce = null;
                            if (_currentCount == 0)
                            {
                                if (millisecondsTimeout == 0) return false;
                                try { waitSuccessful = WaitUntilCountOrTimeout(millisecondsTimeout, startTime, cancellationToken); }
                                catch (OperationCanceledException e) { oce = e; }
                            }
                            if (_currentCount > 0)
                            {
                                waitSuccessful = true;
                                _currentCount--;
                            }
                            else if (oce != null) throw oce;
                        }
                    }
                    finally { _waitCount--; }
                }
            }
            finally { registration.Dispose(); }
            return asyncWaitTask != null ? asyncWaitTask.GetAwaiter().GetResult() : waitSuccessful;
        }

        private bool WaitUntilCountOrTimeout(int millisecondsTimeout, long startTime, CancellationToken cancellationToken)
        {
            int wait = Timeout.Infinite;
            while (_currentCount == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (millisecondsTimeout != Timeout.Infinite)
                {
                    long remaining = millisecondsTimeout - (Environment.TickCount64 - startTime);
                    if (remaining <= 0) return false;
                    wait = (int)remaining;
                }
                bool signaled = Monitor.Wait(_lock, wait);
                if (_countOfWaitersPulsedToWake != 0) _countOfWaitersPulsedToWake--;
                if (!signaled) return false;
            }
            return true;
        }

        public Task WaitAsync() => WaitAsync(Timeout.Infinite, CancellationToken.None);
        public Task WaitAsync(CancellationToken cancellationToken) => WaitAsync(Timeout.Infinite, cancellationToken);
        public Task<bool> WaitAsync(int millisecondsTimeout) => WaitAsync(millisecondsTimeout, CancellationToken.None);
        public Task<bool> WaitAsync(TimeSpan timeout) => WaitAsync(Milliseconds(timeout), CancellationToken.None);
        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => WaitAsync(Milliseconds(timeout), cancellationToken);

        public Task<bool> WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckDispose();
            CheckTimeout(millisecondsTimeout);
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<bool>(cancellationToken);
            lock (_lock) return WaitAsyncCore(millisecondsTimeout, cancellationToken);
        }

        // Under the lock.
        private Task<bool> WaitAsyncCore(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (_currentCount > 0)
            {
                _currentCount--;
                return TrueTask;
            }
            if (millisecondsTimeout == 0) return FalseTask;
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _asyncWaiters.AddLast(waiter);
            return millisecondsTimeout == Timeout.Infinite && !cancellationToken.CanBeCanceled
                ? waiter.Task
                : WaitUntilCountOrTimeoutAsync(waiter, millisecondsTimeout, cancellationToken);
        }

        private async Task<bool> WaitUntilCountOrTimeoutAsync(TaskCompletionSource<bool> waiter, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using (var cts = cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : new CancellationTokenSource())
            {
                Task delay = Task.Delay(millisecondsTimeout, cts.Token);
                if (waiter.Task == await Task.WhenAny(waiter.Task, delay).ConfigureAwait(false))
                {
                    cts.Cancel(); // the delay's timer goes away
                    return true;
                }
            }
            lock (_lock)
            {
                // Still queued: the wait timed out or was canceled (not granted).
                if (_asyncWaiters.Remove(waiter))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return false;
                }
            }
            return await waiter.Task.ConfigureAwait(false);
        }

        public int Release() => Release(1);

        public int Release(int releaseCount)
        {
            CheckDispose();
            if (releaseCount < 1)
                throw new ArgumentOutOfRangeException("releaseCount", releaseCount, "The releaseCount argument must be greater than zero.");
            int returnCount;
            List<TaskCompletionSource<bool>> granted = null;
            lock (_lock)
            {
                int currentCount = _currentCount;
                returnCount = currentCount;
                if (_maxCount - currentCount < releaseCount) throw new SemaphoreFullException();
                currentCount += releaseCount;

                int waitersToNotify = Math.Min(currentCount, _waitCount) - _countOfWaitersPulsedToWake;
                if (waitersToNotify > 0)
                {
                    if (waitersToNotify > releaseCount) waitersToNotify = releaseCount;
                    _countOfWaitersPulsedToWake += waitersToNotify;
                    for (int i = 0; i < waitersToNotify; i++) Monitor.Pulse(_lock);
                }

                int maxAsyncToRelease = currentCount - _waitCount;
                while (maxAsyncToRelease > 0 && _asyncWaiters.First != null)
                {
                    --currentCount;
                    --maxAsyncToRelease;
                    var waiter = _asyncWaiters.First.Value;
                    _asyncWaiters.RemoveFirst();
                    (granted ??= new List<TaskCompletionSource<bool>>()).Add(waiter);
                }
                _currentCount = currentCount;
            }
            // RunContinuationsAsynchronously: completing them here doesn't run their continuations inline.
            if (granted != null) foreach (var waiter in granted) waiter.TrySetResult(true);
            return returnCount;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) _disposed = true;
        }
    }
}
