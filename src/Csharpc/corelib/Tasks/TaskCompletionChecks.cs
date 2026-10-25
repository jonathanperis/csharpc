// SetCanceled(token) on a TaskCompletionSource whose task already completed: .NET's InvalidOperationException.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class TaskCompletionChecks
    {
        public static void ThrowIfNotSet(bool set)
        {
            if (!set) throw new InvalidOperationException("An attempt was made to transition a task to a final state when it had already completed.");
        }
    }
}
