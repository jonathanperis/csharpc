// What .NET's streams wait on with SemaphoreSlim(1, 1).WaitAsync to run one async operation at a time,
// in order (Stream's async semaphore in BufferedStream, CryptoStream's): csharpc's SemaphoreSlim has no
// WaitAsync yet.
#nullable disable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal sealed class AsyncGate
    {
        private readonly object _lock = new object();
        private readonly Queue<TaskCompletionSource> _waiters = new Queue<TaskCompletionSource>();
        private bool _held;

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            lock (_lock)
            {
                if (!_held)
                {
                    _held = true;
                    return Task.CompletedTask;
                }
                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Enqueue(waiter);
                return waiter.Task;
            }
        }

        public void Release()
        {
            TaskCompletionSource next = null;
            lock (_lock)
            {
                if (_waiters.Count > 0) next = _waiters.Dequeue();
                else _held = false;
            }
            next?.SetResult();
        }
    }
}
