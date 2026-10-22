// async/await lowered to LLVM coroutines; async Main drives a single-threaded scheduler.
Console.WriteLine("start");
int sum = await AddAsync(20, 22);
Console.WriteLine($"sum = {sum}");
await Task.Delay(5);
Console.WriteLine("after delay");
await Task.Yield();
Console.WriteLine("after yield");
await Chain(3);
var greeter = new Greeter("csharpc");
Console.WriteLine(await greeter.GreetAsync());
Console.WriteLine(await Task.FromResult(7) + await Task.FromResult(8));
await Task.CompletedTask;
Console.WriteLine("done");

static async Task<int> AddAsync(int a, int b)
{
    await Task.Delay(1);
    int c = a + b;
    await Task.Yield();
    return c;
}

static async Task Chain(int depth)
{
    Console.WriteLine($"enter {depth}");
    if (depth > 0) await Chain(depth - 1);
    await Task.Yield();
    Console.WriteLine($"leave {depth}");
}

class Greeter(string name)
{
    private int _count;
    public async Task<string> GreetAsync()
    {
        _count++;
        await Task.Delay(1);
        return $"hello from {name} ({_count})";
    }
}
