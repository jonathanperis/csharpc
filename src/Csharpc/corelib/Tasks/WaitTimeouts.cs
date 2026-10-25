// Wait(TimeSpan) of the runtime's wait handles (ManualResetEventSlim, events: FunctionCodeGen.Threading.cs):
// the timeout in milliseconds, checked as .NET checks it.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class WaitTimeouts
    {
        public static int TimeoutMilliseconds(TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(totalMilliseconds, -1, "timeout");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(totalMilliseconds, int.MaxValue, "timeout");
            return (int)totalMilliseconds;
        }
    }
}
