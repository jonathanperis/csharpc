// The public TaskFactory / TaskFactory<TResult> overloads of ContinueWhenAll, ContinueWhenAny and FromAsync:
// every overload .NET has, each forwarding to TaskFactoryOps (TaskFactoryOps.cs holds the behavior) with
// the factory's default token, continuation or creation options and scheduler where the overload doesn't
// name them. Parameter names are .NET's, for named arguments.
#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    public partial class TaskFactoryImpl
    {
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction) =>
            TaskFactoryOps.WhenAllAction(tasks, continuationAction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAllAction(tasks, continuationAction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAllAction(tasks, continuationAction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAllAction(tasks, continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction) =>
            TaskFactoryOps.WhenAllActionOf(tasks, continuationAction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAllActionOf(tasks, continuationAction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAllActionOf(tasks, continuationAction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAllActionOf(tasks, continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction) =>
            TaskFactoryOps.WhenAnyAction(tasks, continuationAction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAnyAction(tasks, continuationAction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAnyAction(tasks, continuationAction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAnyAction(tasks, continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction) =>
            TaskFactoryOps.WhenAnyActionOf(tasks, continuationAction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAnyActionOf(tasks, continuationAction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAnyActionOf(tasks, continuationAction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAnyActionOf(tasks, continuationAction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, m_defaultCreationOptions, DefaultScheduler);
        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, creationOptions, DefaultScheduler);
        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, creationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, creationOptions, DefaultScheduler);
        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, creationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task FromAsync(Func<AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(beginMethod, endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(beginMethod, endMethod, state, m_defaultCreationOptions);
        public Task FromAsync(Func<AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(beginMethod, endMethod, state, creationOptions);
        public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(beginMethod, endMethod, state, creationOptions);
        public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1), endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1), endMethod, state, m_defaultCreationOptions);
        public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1), endMethod, state, creationOptions);
        public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1), endMethod, state, creationOptions);
        public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2), endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2), endMethod, state, m_defaultCreationOptions);
        public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2), endMethod, state, creationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2), endMethod, state, creationOptions);
        public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2, arg3), endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2, arg3), endMethod, state, m_defaultCreationOptions);
        public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2, arg3), endMethod, state, creationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2, arg3), endMethod, state, creationOptions);
    }

    public partial class TaskFactoryImpl<TResult>
    {
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAllFunc(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAllFuncOf(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAnyFunc(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, m_defaultCancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, cancellationToken, m_defaultContinuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, m_defaultCancellationToken, continuationOptions, DefaultScheduler);
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.WhenAnyFuncOf(tasks, continuationFunction, cancellationToken, continuationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> FromAsync(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, m_defaultCreationOptions, DefaultScheduler);
        public Task<TResult> FromAsync(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, creationOptions, DefaultScheduler);
        public Task<TResult> FromAsync(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            TaskFactoryOps.FromAsyncResult(asyncResult, endMethod, creationOptions, (TaskSchedulerImpl)(object)scheduler);
        public Task<TResult> FromAsync(Func<AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(beginMethod, endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync(Func<AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(beginMethod, endMethod, state, creationOptions);
        public Task<TResult> FromAsync<TArg1>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1), endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync<TArg1>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1), endMethod, state, creationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2), endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2), endMethod, state, creationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2, arg3), endMethod, state, m_defaultCreationOptions);
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state, TaskCreationOptions creationOptions) =>
            TaskFactoryOps.FromAsyncBeginEnd(TaskFactoryOps.Bind(beginMethod, arg1, arg2, arg3), endMethod, state, creationOptions);
    }
}
