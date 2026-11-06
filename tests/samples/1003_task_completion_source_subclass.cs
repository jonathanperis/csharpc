// Classes deriving from TaskCompletionSource / TaskCompletionSource<T> (System.Net.Http's connection waiters and
// write-queue entries, Kestrel's): the derived object is the task's source, carries its own fields, completes with
// SetResult / SetException / SetCanceled, is awaited through .Task, takes part in WhenAll, can be a ValueTask's
// task and answers type tests and casts.
using System.Threading.Tasks;

var waiter = new Waiter<string>("first");
Task<string> task = waiter.Task;
Console.WriteLine("status before: " + task.Status + ", name " + waiter.Name);
var consumer = Consume(waiter);
waiter.Extra = 7;
waiter.SetResult("done");
Console.WriteLine(await consumer);
Console.WriteLine("status after: " + task.Status + ", result " + task.Result + ", extra " + waiter.Extra);
Console.WriteLine("same task: " + ReferenceEquals(task, waiter.Task));

// A value-type payload and the non-generic source.
var number = new Waiter<long>("number");
number.SetResult(1L << 40);
Console.WriteLine("long result: " + await number.Task);
var plain = new PlainWaiter { Label = "plain" };
Console.WriteLine("plain pending: " + !plain.Task.IsCompleted);
plain.SetResult();
await plain.Task;
Console.WriteLine("plain completed: " + plain.Task.IsCompletedSuccessfully + " " + plain.Label);

// Faults and cancellation.
var faulty = new Waiter<int>("faulty");
faulty.SetException(new InvalidOperationException("boom"));
try { await faulty.Task; }
catch (InvalidOperationException e) { Console.WriteLine("faulted: " + e.Message + " / " + faulty.Task.Status); }
var canceled = new Waiter<int>("canceled");
canceled.SetCanceled();
try { await canceled.Task; }
catch (TaskCanceledException) { Console.WriteLine("canceled: " + canceled.Task.Status); }
Console.WriteLine("TrySetResult after completion: " + faulty.TrySetResult(1));

// Type tests, casts and a collection of them.
object boxed = waiter;
Console.WriteLine(boxed is Waiter<string>);
Console.WriteLine(boxed is TaskCompletionSource<string>);
Console.WriteLine(boxed is Waiter<int>);
Console.WriteLine(((TaskCompletionSource<string>)boxed).Task.Result);
Console.WriteLine(boxed.GetType().Name);
object plainBoxed = plain;
Console.WriteLine(plainBoxed is TaskCompletionSource);
Console.WriteLine(plainBoxed is PlainWaiter);

var many = new List<Waiter<int>>();
for (int i = 0; i < 5; i++) many.Add(new Waiter<int>("w" + i));
var all = Task.WhenAll(many.Select(w => w.Task));
_ = Task.Run(() => { for (int i = 0; i < many.Count; i++) many[i].SetResult(i * i); });
Console.WriteLine(string.Join(",", await all));

// Used as a ValueTask's task, as System.Net.Http's connection pool does.
var forValueTask = new Waiter<string>("value task");
ValueTask<string> valueTask = new(forValueTask.Task);
forValueTask.SetResult("through a ValueTask");
Console.WriteLine(await valueTask);

// The base constructor's task creation options and the virtual members of object.
var cont = new Waiter<int>("options", TaskCreationOptions.RunContinuationsAsynchronously);
Console.WriteLine(cont + " " + cont.GetHashCode().Equals(cont.GetHashCode()));

async Task<string> Consume(Waiter<string> w) => "consumed " + await w.Task;

class Waiter<T> : TaskCompletionSource<T>
{
    public string Name;
    public int Extra;
    public Waiter(string name) : base() { Name = name; }
    public Waiter(string name, TaskCreationOptions options) : base(options) { Name = name; }
    public override string ToString() => "Waiter(" + Name + ")";
}

class PlainWaiter : TaskCompletionSource
{
    public string? Label;
}
