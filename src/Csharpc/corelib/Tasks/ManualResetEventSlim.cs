// System.Threading.ManualResetEventSlim: a class of the corelib (derivable — System.Linq.Parallel's
// IntValueEvent derives from it) over one of the runtime's events (runtime/threads.c, csrt_event_*), with
// .NET's argument checks. Waits with a CancellationToken go through CancelableWaits.
#nullable disable
using System;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.ManualResetEventSlim")]
    public class ManualResetEventSlimImpl : IDisposable
    {
        private readonly object _event;
        private readonly int _spinCount;

        public ManualResetEventSlimImpl() : this(false) { }

        public ManualResetEventSlimImpl(bool initialState)
        {
            _event = CoreIntrinsics.csrt_event_new(0, initialState ? 1 : 0, 0);
            _spinCount = Environment.ProcessorCount == 1 ? 1 : 35; // SpinWait.SpinCountforSpinBeforeWait
        }

        public ManualResetEventSlimImpl(bool initialState, int spinCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(spinCount);
            if (spinCount > 2047) throw new ArgumentOutOfRangeException("spinCount", string.Format("The spinCount argument must be in the range 0 to {0}, inclusive.", 2047));
            _event = CoreIntrinsics.csrt_event_new(0, initialState ? 1 : 0, 0);
            _spinCount = spinCount;
        }

        /// <summary>The runtime event (CancelableWaits).</summary>
        internal object Event => _event;

        public bool IsSet => CoreIntrinsics.csrt_event_is_set(_event);
        public int SpinCount => _spinCount;

        public void Set() => CoreIntrinsics.csrt_event_set(_event, 1);
        public void Reset() => CoreIntrinsics.csrt_event_reset(_event);

        public void Wait() => CoreIntrinsics.csrt_event_wait(_event, -1);

        public bool Wait(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            return CoreIntrinsics.csrt_event_wait(_event, millisecondsTimeout);
        }

        public bool Wait(TimeSpan timeout) => Wait(CancelableWaits.Milliseconds(timeout, null));

        public void Wait(CancellationToken cancellationToken) => Wait(-1, cancellationToken);

        public bool Wait(TimeSpan timeout, CancellationToken cancellationToken) => Wait(CancelableWaits.Milliseconds(timeout, null), cancellationToken);

        public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            return CancelableWaits.Wait(Event, millisecondsTimeout, cancellationToken);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }
    }
}
