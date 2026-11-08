// PoolingAsyncValueTaskMethodBuilder<T> used by hand: the builder's Task is read before the state machine's first await,
// so the pooled box exists before the state machine's type is known (.NET boxes it as StateMachineBox<IAsyncStateMachine>
// and copies the state machine in at the await). The continuation must still run the copy, and the box is good for one
// await only, like every pooled box.
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

var machine = new Machine { Builder = PoolingAsyncValueTaskMethodBuilder<int>.Create(), Seed = 40 };
ValueTask<int> task = machine.Builder.Task;
machine.Builder.Start(ref machine);
Console.WriteLine("started");
Console.WriteLine(await task);
try { Console.WriteLine(await task); }
catch (InvalidOperationException e) { Console.WriteLine(e.GetType().Name); }

// The same, resuming twice.
var twice = new TwoAwaits { Builder = PoolingAsyncValueTaskMethodBuilder<int>.Create() };
ValueTask<int> second = twice.Builder.Task;
twice.Builder.Start(ref twice);
Console.WriteLine(await second);

// Faulting before any await.
var failing = new Failing { Builder = PoolingAsyncValueTaskMethodBuilder<int>.Create() };
ValueTask<int> failed = failing.Builder.Task;
failing.Builder.Start(ref failing);
try { await failed; } catch (ArgumentException e) { Console.WriteLine(e.Message); }

struct Machine : IAsyncStateMachine
{
    public PoolingAsyncValueTaskMethodBuilder<int> Builder;
    public int Seed;
    private int _state;

    public void MoveNext()
    {
        if (_state == 0)
        {
            _state = 1;
            var awaiter = Task.Yield().GetAwaiter();
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            return;
        }
        Builder.SetResult(Seed + 2);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }
}

struct TwoAwaits : IAsyncStateMachine
{
    public PoolingAsyncValueTaskMethodBuilder<int> Builder;
    private int _state;
    private int _sum;

    public void MoveNext()
    {
        if (_state < 2)
        {
            _sum += ++_state;
            var awaiter = Task.Yield().GetAwaiter();
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            return;
        }
        Builder.SetResult(_sum * 100);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }
}

struct Failing : IAsyncStateMachine
{
    public PoolingAsyncValueTaskMethodBuilder<int> Builder;

    public void MoveNext() => Builder.SetException(new ArgumentException("failed before the first await"));

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }
}
