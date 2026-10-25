// ManualResetEventSlim.Wait (Tasks/ManualResetEventSlim.cs) and CountdownEvent.Wait with a CancellationToken, over the C runtime's events
// (runtime/threads.c): a callback registered on the token kicks the event's waiters, which then see the
// source canceled (csrt_event_wait_token). The argument checks come in .NET's order (ManualResetEventSlim.cs,
// CountdownEvent.cs).
#nullable disable
using System;
using System.Threading;

namespace CsharpcCoreLib
{
    internal static class CancelableWaits
    {
        private static readonly Action<object> s_kick = e => CoreIntrinsics.csrt_event_kick(e);

        /// <summary>Waits for `e` until signalled (true), the timeout (false) or the token's cancellation (throws).</summary>
        internal static bool Wait(object e, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            object source = CoreIntrinsics.TokenSource(cancellationToken);
            int result;
            if (source == null) result = CoreIntrinsics.csrt_event_wait_token(e, millisecondsTimeout, null);
            else
            {
                using (cancellationToken.UnsafeRegister(s_kick, e))
                    result = CoreIntrinsics.csrt_event_wait_token(e, millisecondsTimeout, source);
            }
            if (result == 2) throw new OperationCanceledException("The operation was canceled.", cancellationToken);
            return result == 1;
        }

        internal static int Milliseconds(TimeSpan timeout, string message)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
                throw message == null ? new ArgumentOutOfRangeException("timeout") : new ArgumentOutOfRangeException("timeout", timeout, message);
            return (int)totalMilliseconds;
        }
    }

    [ShadowMembers("System.Threading.CountdownEvent")]
    internal static class CountdownEventWaits
    {
        [ShadowInstance] public static void Wait(CountdownEvent e, CancellationToken cancellationToken) => Wait(e, -1, cancellationToken);

        [ShadowInstance] public static bool Wait(CountdownEvent e, TimeSpan timeout) =>
            Wait(e, CancelableWaits.Milliseconds(timeout, null), default(CancellationToken));

        [ShadowInstance] public static bool Wait(CountdownEvent e, TimeSpan timeout, CancellationToken cancellationToken) =>
            Wait(e, CancelableWaits.Milliseconds(timeout, null), cancellationToken);

        [ShadowInstance] public static bool Wait(CountdownEvent e, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            cancellationToken.ThrowIfCancellationRequested();
            return CancelableWaits.Wait(e, millisecondsTimeout, cancellationToken);
        }
    }
}
