// System.Threading.Timer and System.TimeProvider: Kestrel's heartbeat and connection timeouts, the
// file watchers and Microsoft.Extensions.Hosting's shutdown timeout run on them. A timer is a loop of
// Task.Delay on the runtime's timer thread; its callbacks run on the thread pool, as in .NET.
#nullable disable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.Timer")]
    public sealed class TimerImpl : IDisposable, IAsyncDisposable, ITimer
    {
        // .NET's limit: Timer.MaxSupportedTimeout (0xfffffffe ms). Task.Delay takes an int here.
        private const long MaxDelay = int.MaxValue;

        private readonly TimerCallback _callback;
        private readonly object _state;
        private readonly object _lock = new object();
        private CancellationTokenSource _schedule;
        private bool _disposed;
        private static long s_active;

        /// <summary>Timer.ActiveCount: the timers created and not disposed.</summary>
        public static long ActiveCount => Interlocked.Read(ref s_active);

        public TimerImpl(TimerCallback callback) : this(callback, null, -1L, -1L, false) => _state = this; // .NET passes the timer itself

        public TimerImpl(TimerCallback callback, object state, int dueTime, int period) : this(callback, state, (long)dueTime, period, true) { }

        public TimerImpl(TimerCallback callback, object state, uint dueTime, uint period)
            : this(callback, state, dueTime == uint.MaxValue ? -1L : dueTime, period == uint.MaxValue ? -1L : period, true) { }

        public TimerImpl(TimerCallback callback, object state, long dueTime, long period) : this(callback, state, dueTime, period, true) { }

        public TimerImpl(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
            : this(callback, state, Milliseconds(dueTime, "dueTime"), Milliseconds(period, "period"), true) { }

        private TimerImpl(TimerCallback callback, object state, long dueTime, long period, bool check)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            _callback = callback;
            _state = state;
            if (check) Check(dueTime, period);
            Schedule(dueTime, period);
            Interlocked.Increment(ref s_active);
        }

        private static long Milliseconds(TimeSpan time, string paramName)
        {
            long ms = (long)time.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(ms, -1L, paramName); // .NET 11's checks and messages
            ArgumentOutOfRangeException.ThrowIfGreaterThan(ms, 0xfffffffeL, paramName);
            return ms;
        }

        private static void Check(long dueTime, long period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1L, "dueTime");
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1L, "period");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dueTime, 0xfffffffeL, "dueTime");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(period, 0xfffffffeL, "period");
            if (dueTime > MaxDelay || period > MaxDelay)
                throw new NotSupportedException("Timer intervals longer than Int32.MaxValue milliseconds are not supported by csharpc yet.");
        }

        public bool Change(int dueTime, int period) => Change((long)dueTime, period);

        public bool Change(uint dueTime, uint period) =>
            Change(dueTime == uint.MaxValue ? -1L : dueTime, period == uint.MaxValue ? -1L : period);

        public bool Change(TimeSpan dueTime, TimeSpan period) => Change(Milliseconds(dueTime, "dueTime"), Milliseconds(period, "period"));

        public bool Change(long dueTime, long period)
        {
            Check(dueTime, period);
            return Schedule(dueTime, period);
        }

        private bool Schedule(long dueTime, long period)
        {
            lock (_lock)
            {
                if (_disposed) return false; // as .NET: changing a disposed timer does nothing
                _schedule?.Cancel();
                _schedule = null;
                if (dueTime == -1) return true;
                var schedule = new CancellationTokenSource();
                _schedule = schedule;
                Run((int)dueTime, (int)period, schedule.Token);
                return true;
            }
        }

        // An exception escaping a callback is unhandled, as in .NET (async void: the process ends).
        private async void Run(int dueTime, int period, CancellationToken token)
        {
            try
            {
                await Task.Delay(dueTime, token).ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    _callback(_state);
                    if (period <= 0) return; // Infinite or zero: one call only
                    await Task.Delay(period, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                Interlocked.Decrement(ref s_active);
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

    [Shadows("System.TimeProvider")]
    public abstract class TimeProviderImpl
    {
        private static TimeProviderImpl s_system;

        protected TimeProviderImpl() { }

        public static TimeProvider System => (TimeProvider)(object)(s_system ??= new SystemTimeProvider());

        public virtual DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

        public DateTimeOffset GetLocalNow()
        {
            DateTimeOffset utc = GetUtcNow();
            TimeZoneInfo zone = LocalTimeZone;
            if (zone == null) throw new InvalidOperationException("The operation cannot be performed when TimeProvider.LocalTimeZone is null.");
            TimeSpan offset = zone.GetUtcOffset(utc);
            if (offset.Ticks == 0) return utc;
            long localTicks = utc.Ticks + offset.Ticks;
            if ((ulong)localTicks > (ulong)DateTime.MaxValue.Ticks) localTicks = localTicks < DateTime.MinValue.Ticks ? DateTime.MinValue.Ticks : DateTime.MaxValue.Ticks;
            return new DateTimeOffset(localTicks, offset);
        }

        public virtual TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

        public virtual long TimestampFrequency => Stopwatch.Frequency;

        public virtual long GetTimestamp() => Stopwatch.GetTimestamp();

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        {
            long frequency = TimestampFrequency;
            if (frequency <= 0) throw new InvalidOperationException("The operation cannot be performed when TimeProvider.TimestampFrequency is zero or negative.");
            return new TimeSpan((long)((endingTimestamp - startingTimestamp) * ((double)TimeSpan.TicksPerSecond / frequency)));
        }

        public TimeSpan GetElapsedTime(long startingTimestamp) => GetElapsedTime(startingTimestamp, GetTimestamp());

        public virtual ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            return new Timer(callback, state, dueTime, period);
        }

        private sealed class SystemTimeProvider : TimeProviderImpl
        {
        }
    }
}
