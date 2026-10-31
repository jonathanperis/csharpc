// Task.WhenAll / WhenAny over a ReadOnlySpan<Task> held in a variable (the params-span overloads, which compiled
// C# reaches through an inline array): the tasks are copied out of the span, results keep their order.
using System;
using System.Threading.Tasks;

Task<int> a = Task.FromResult(1), b = Task.Run(() => 2), c = Delay(3);
ReadOnlySpan<Task<int>> typed = [a, b, c];
int[] results = await Task.WhenAll(typed);
Console.WriteLine(string.Join(",", results));
ReadOnlySpan<Task> plain = [Task.CompletedTask, Delay(4)];
await Task.WhenAll(plain);
Console.WriteLine("all done");
var first = await Task.WhenAny([Task.FromResult(7)]);
Console.WriteLine(first.Result);
try
{
    ReadOnlySpan<Task> failing = [Task.FromException(new InvalidOperationException("boom")), Task.CompletedTask];
    await Task.WhenAll(failing);
}
catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }

static async Task<int> Delay(int value) { await Task.Yield(); return value; }
