// System.Threading.Lock's CoreLib-internal condition operations, which the framework's ManualResetEventSlim waits with (#414):
// Wait(int) releases the lock completely, waits for a Pulse, the timeout or Thread.Interrupt, and takes it back; Pulse wakes the
// oldest waiter, PulseAll all of them. Lock is the runtime's object (runtime/threads.c), so these are its operations.
#nullable disable
using System.Threading;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Threading.Lock")]
    internal static class LockConditions
    {
        [ShadowInstance]
        public static bool Wait(Lock @this, int millisecondsTimeout) => CoreIntrinsics.csrt_lock_wait(@this, millisecondsTimeout);

        /// <summary>Wait(int, object): the object only names the wait for .NET's diagnostics.</summary>
        [ShadowInstance]
        public static bool Wait(Lock @this, int millisecondsTimeout, object associatedObjectForMonitorWait) => CoreIntrinsics.csrt_lock_wait(@this, millisecondsTimeout);

        [ShadowInstance]
        public static void Pulse(Lock @this) => CoreIntrinsics.csrt_lock_pulse(@this);

        [ShadowInstance]
        public static void PulseAll(Lock @this) => CoreIntrinsics.csrt_lock_pulse_all(@this);
    }
}
