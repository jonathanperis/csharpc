// Multicast delegates and events: += / -= / invocation lists / custom accessors.
Action greet = () => Console.WriteLine("hello");
greet += () => Console.WriteLine("world");
Action bye = () => Console.WriteLine("bye");
greet += bye;
greet();
greet -= bye;
greet();
greet -= bye; // removing something not present is a no-op
greet();

Func<int, int> chain = x => x + 1;
chain += x => x * 10;
Console.WriteLine(chain(5)); // the last delegate's result wins

Func<int, int> none = chain - chain;
Console.WriteLine(none is null);

var button = new Button("OK");
int clicks = 0;
EventHandler<int> onClick = (sender, n) => { clicks += n; Console.WriteLine($"clicked {n}"); };
button.Clicked += onClick;
button.Clicked += (sender, n) => Console.WriteLine("second handler");
button.Click(2);
button.Clicked -= onClick;
button.Click(3);
Console.WriteLine(clicks);

var log = new Log();
log.Changed += m => Console.WriteLine("A:" + m);
log.Changed += m => Console.WriteLine("B:" + m);
log.Write("first");
Console.WriteLine(log.Subscribers);

Counter.Ticked += () => Console.WriteLine("tick");
Counter.Tick();
Counter.Tick();

class Button(string name)
{
    public event EventHandler<int>? Clicked;
    public void Click(int times) => Clicked?.Invoke(this, times);
    public override string ToString() => name;
}

class Log
{
    private Action<string>? _handlers;
    public int Subscribers;
    public event Action<string> Changed
    {
        add { _handlers += value; Subscribers++; }
        remove { _handlers -= value; Subscribers--; }
    }
    public void Write(string message) => _handlers?.Invoke(message);
}

static class Counter
{
    public static event Action? Ticked;
    public static void Tick() => Ticked?.Invoke();
}
