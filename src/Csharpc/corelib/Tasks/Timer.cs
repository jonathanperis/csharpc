// System.Threading.Timer over the runtime's delay timers (Task.Delay): the callback runs on the thread
// pool after dueTime and then every period (0 or Infinite: once); Change reschedules, Dispose stops it.
// A callback that throws ends the process with "Unhandled exception." as on .NET's pool. Unlike .NET,
// the next period is timed from the end of a callback (callbacks never overlap), and an unreferenced
// timer keeps firing (.NET may collect it).
#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.Timer")]
    public sealed class TimerImpl : IDisposable, IAsyncDisposable
    {
        private readonly TimerCallback _callback;
        private readonly object _state;
        private readonly object _lock = new object();
        private CancellationTokenSource _schedule;
        private bool _disposed;

        public TimerImpl(TimerCallback callback, object state, int dueTime, int period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1, "dueTime");
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1, "period");
            _callback = callback ?? throw new ArgumentNullException("callback");
            _state = state;
            Schedule(dueTime, period);
        }

        public TimerImpl(TimerCallback callback, object state, long dueTime, long period)
            : this(callback, state, Milliseconds(dueTime, "dueTime"), Milliseconds(period, "period")) { }

        public TimerImpl(TimerCallback callback, object state, uint dueTime, uint period)
            : this(callback, state, Milliseconds(dueTime == uint.MaxValue ? -1 : dueTime, "dueTime"), Milliseconds(period == uint.MaxValue ? -1 : period, "period")) { }

        public TimerImpl(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
            : this(callback, state, Milliseconds(dueTime, "dueTime"), Milliseconds(period, "period")) { }

        // new Timer(callback): the timer object itself is the state, and it doesn't start.
        public TimerImpl(TimerCallback callback)
        {
            _callback = callback ?? throw new ArgumentNullException("callback");
            _state = this;
        }

        private static int Milliseconds(long value, string name)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1L, name);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 0xFFFFFFFEL, name);
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        private static int Milliseconds(TimeSpan value, string name) => Milliseconds((long)value.TotalMilliseconds, name);

        public bool Change(int dueTime, int period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1, "dueTime");
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1, "period");
            lock (_lock)
            {
                if (_disposed) return false; // .NET: a disposed timer just reports that nothing changed
                Schedule(dueTime, period);
            }
            return true;
        }

        public bool Change(long dueTime, long period) => Change(Milliseconds(dueTime, "dueTime"), Milliseconds(period, "period"));
        public bool Change(uint dueTime, uint period) =>
            Change(Milliseconds(dueTime == uint.MaxValue ? -1 : dueTime, "dueTime"), Milliseconds(period == uint.MaxValue ? -1 : period, "period"));
        public bool Change(TimeSpan dueTime, TimeSpan period) => Change(Milliseconds(dueTime, "dueTime"), Milliseconds(period, "period"));

        private void Schedule(int dueTime, int period)
        {
            _schedule?.Cancel();
            _schedule = null;
            if (dueTime == Timeout.Infinite) return;
            var schedule = _schedule = new CancellationTokenSource();
            Fire(dueTime, period, schedule.Token);
        }

        private void Fire(int delay, int period, CancellationToken token)
        {
            Task.Delay(delay, token).ContinueWith(t =>
            {
                if (t.IsCanceled || token.IsCancellationRequested) return;
                try { _callback(_state); }
                catch (Exception e) { CoreIntrinsics.UnhandledException(e); }
                if (period != Timeout.Infinite && period != 0 && !token.IsCancellationRequested) Fire(period, period, token);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _schedule?.Cancel();
                _schedule = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
