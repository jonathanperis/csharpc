// SemaphoreSlim.WaitAsync over the runtime's semaphore (src/runtime/threads.c, FunctionCodeGen.Threading.cs):
// an available count is taken at once and the returned task is complete, as in .NET; otherwise the wait
// continues on a pool thread and completes the task when it acquires the count (or times out or is
// canceled). Which waiter gets a released count first is the runtime's order, not necessarily .NET's FIFO
// queue of asynchronous waiters: programs that depend on it are timing-dependent under .NET too.
#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class SemaphoreWaitAsync
    {
        public static Task WaitAsync(SemaphoreSlim semaphore)
        {
            if (semaphore.Wait(0)) return Task.CompletedTask;
            return Task.Run(() => semaphore.Wait(-1));
        }

        public static Task<bool> WaitAsyncTimeout(SemaphoreSlim semaphore, int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            if (semaphore.Wait(0)) return Task.FromResult(true);
            if (millisecondsTimeout == 0) return Task.FromResult(false);
            return Task.Run(() => semaphore.Wait(millisecondsTimeout));
        }

        public static Task WaitAsyncToken(SemaphoreSlim semaphore, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            if (semaphore.Wait(0)) return Task.CompletedTask;
            if (!cancellationToken.CanBeCanceled) return Task.Run(() => semaphore.Wait(-1));
            return Task.Run(() =>
            {
                // Poll the token while waiting: the runtime semaphore has no cancelable wait.
                while (!semaphore.Wait(10)) cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);
        }

        /// <summary>A TimeSpan timeout in milliseconds, as the Wait(TimeSpan) overloads check it.</summary>
        public static int TimeoutMilliseconds(TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(totalMilliseconds, -1, "timeout");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(totalMilliseconds, int.MaxValue, "timeout");
            return (int)totalMilliseconds;
        }
    }
}
