// Task.ConfigureAwait(ConfigureAwaitOptions) (.NET 8): ASP.NET Core's hosting awaits shutdown work with
// SuppressThrowing. A configured awaitable is the task itself in csharpc (TypeMapper), so the options
// are applied by awaiting a task that carries them.
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Threading.Tasks.Task")]
    internal static class TaskConfigureAwaitMembers
    {
        private const ConfigureAwaitOptions Known =
            ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding;

        [ShadowInstance]
        public static ConfiguredTaskAwaitable ConfigureAwait(Task task, ConfigureAwaitOptions options)
        {
            if ((options & ~Known) != 0) throw new ArgumentOutOfRangeException("options");
            Task awaited = task;
            // SuppressThrowing: the await completes once the task does, whatever its outcome.
            if ((options & ConfigureAwaitOptions.SuppressThrowing) != 0)
                awaited = task.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            // ForceYielding: the continuation is queued even if the task has completed.
            if ((options & ConfigureAwaitOptions.ForceYielding) != 0) awaited = Yielding(awaited);
            return awaited.ConfigureAwait((options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0);
        }

        private static async Task Yielding(Task task)
        {
            await Task.Yield();
            await task.ConfigureAwait(false);
        }
    }
}
