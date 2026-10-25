// System.Threading.PeriodicTimer, after dotnet/runtime's PeriodicTimer.cs (release/11.0, MIT): a Timer
// that signals one pending tick; WaitForNextTickAsync completes at once for a pending tick (or false once
// disposed), else when the next tick, Dispose or cancellation comes. (csharpc: a TaskCompletionSource
// instead of the reusable IValueTaskSource; a custom TimeProvider is not supported yet.)
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.PeriodicTimer")]
    public sealed class PeriodicTimerImpl : IDisposable
    {
        private readonly Timer _timer;
        private TimeSpan _period;
        private bool _stopped, _signaled;
        private TaskCompletionSource<bool> _waiter;
        private CancellationTokenRegistration _registration;

        public PeriodicTimerImpl(TimeSpan period)
        {
            if (!TryGetMilliseconds(period, out uint ms)) throw new ArgumentOutOfRangeException("period");
            _period = period;
            _timer = new Timer(static s => ((PeriodicTimerImpl)s).Signal(), this, ms, ms);
        }

        public PeriodicTimerImpl(TimeSpan period, TimeProvider timeProvider) : this(period)
        {
            if (timeProvider is null) throw new ArgumentNullException("timeProvider");
            if (timeProvider != TimeProvider.System)
                throw new NotSupportedException("PeriodicTimer with a custom TimeProvider is not supported by csharpc yet.");
        }

        public TimeSpan Period
        {
            get => _period;
            set
            {
                if (!TryGetMilliseconds(value, out _)) throw new ArgumentOutOfRangeException("value");
                _period = value;
                if (!_timer.Change(value, value)) throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private static bool TryGetMilliseconds(TimeSpan value, out uint milliseconds)
        {
            long ms = (long)value.TotalMilliseconds;
            if ((ms >= 1 && ms <= 0xFFFFFFFE) || value == Timeout.InfiniteTimeSpan)
            {
                milliseconds = (uint)ms;
                return true;
            }
            milliseconds = 0;
            return false;
        }

        public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<bool> waiter;
            lock (this)
            {
                // One consumer at a time.
                if (_waiter != null) throw new InvalidOperationException("Operation is not valid due to the current state of the object.");
                if (cancellationToken.IsCancellationRequested) return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
                if (_signaled)
                {
                    if (!_stopped) _signaled = false;
                    return new ValueTask<bool>(!_stopped);
                }
                waiter = _waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (cancellationToken.CanBeCanceled)
                    _registration = cancellationToken.Register(() => Complete(waiter, canceled: true, cancellationToken));
            }
            return new ValueTask<bool>(waiter.Task);
        }

        private void Signal(bool stopping = false)
        {
            TaskCompletionSource<bool> waiter = null;
            lock (this)
            {
                _stopped |= stopping;
                if (!_signaled)
                {
                    _signaled = true;
                    waiter = _waiter;
                }
            }
            if (waiter != null) Complete(waiter, canceled: false, default);
        }

        private void Complete(TaskCompletionSource<bool> waiter, bool canceled, CancellationToken cancellationToken)
        {
            bool result;
            CancellationTokenRegistration registration;
            lock (this)
            {
                if (!ReferenceEquals(_waiter, waiter)) return;
                _waiter = null;
                registration = _registration;
                _registration = default;
                result = !_stopped;
                if (!_stopped) _signaled = false;
            }
            registration.Dispose();
            if (canceled) waiter.TrySetException(new OperationCanceledException(cancellationToken));
            else waiter.TrySetResult(result);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _timer.Dispose();
            Signal(stopping: true);
        }
    }
}
