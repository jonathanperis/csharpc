// async lambdas, Task.Run and closures over async state.
int counter = 0;
Func<Task> increment = async () => { await Task.Delay(1); counter++; };
await increment();
await increment();
Console.WriteLine($"counter = {counter}");

Func<int, Task<int>> square = async x => { await Task.Yield(); return x * x; };
Console.WriteLine(await square(12));

var results = new int[3];
var work = new Task[3];
for (int i = 0; i < 3; i++)
{
    int slot = i;
    work[i] = Task.Run(async () => { await Task.Delay(3 - slot); results[slot] = slot * 100; });
}
await Task.WhenAll(work);
Console.WriteLine($"{results[0]} {results[1]} {results[2]}");

Console.WriteLine(await Task.Run(() => 6 * 7));
Console.WriteLine(await Task.Run(async () => { await Task.Yield(); return "async run"; }));
await Task.Run(() => Console.WriteLine("action ran"));

var log = await Pipeline(async s => { await Task.Yield(); return s.ToUpper(); }, "pipeline");
Console.WriteLine(log);

static async Task<string> Pipeline(Func<string, Task<string>> stage, string input)
{
    var first = await stage(input);
    var second = await stage(first + "!");
    return second;
}
