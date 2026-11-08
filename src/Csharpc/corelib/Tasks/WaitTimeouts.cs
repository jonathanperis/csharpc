// Wait(TimeSpan) of the runtime's wait handles (ManualResetEventSlim, events: FunctionCodeGen.Threading.cs):
// the timeout in milliseconds, checked as .NET checks it.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class WaitTimeouts
    {
        /// <summary>WaitHandle.WaitOne(int) and CountdownEvent.Wait(int): below -1 is an error, not an infinite wait.</summary>
        public static int Milliseconds(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            return millisecondsTimeout;
        }

        public static int TimeoutMilliseconds(TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(totalMilliseconds, -1, "timeout");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(totalMilliseconds, int.MaxValue, "timeout");
            return (int)totalMilliseconds;
        }
    }
}
