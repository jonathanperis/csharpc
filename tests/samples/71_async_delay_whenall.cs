// Task.WhenAll / WhenAny / Delay. Only deterministic observations are printed.
var tasks = new Task<int>[4];
for (int i = 0; i < tasks.Length; i++) tasks[i] = Compute(i, (tasks.Length - i) * 3);
int[] results = await Task.WhenAll(tasks);
for (int i = 0; i < results.Length; i++) Console.Write(results[i] + " ");
Console.WriteLine();

int[] pair = await Task.WhenAll(Compute(10, 2), Compute(20, 1));
Console.WriteLine(pair[0] + pair[1]);

await Task.WhenAll(new Task[] { Task.Delay(4), Task.Delay(2), Task.CompletedTask });
Console.WriteLine("all delays finished");

var slow = Task.Delay(200);
var fast = Task.Delay(1);
var winner = await Task.WhenAny(slow, fast);
Console.WriteLine(winner == fast);

long started = Environment.TickCount64;
await Task.Delay(30);
Console.WriteLine(Environment.TickCount64 - started >= 25);

static async Task<int> Compute(int x, int delay)
{
    await Task.Delay(delay);
    return x * x + 1;
}
