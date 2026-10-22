Bus.Message += m => Console.WriteLine("h1 " + m);
Bus.Message += m => Console.WriteLine("h2 " + m);
Bus.Send("hi");
var t = new Tracked();
t.Changed += () => Console.WriteLine("changed!");
t.Raise();
static class Bus { public static event Action<string>? Message; public static void Send(string m) => Message?.Invoke(m); }
class Tracked { Action? h; public event Action Changed { add { Console.WriteLine("add"); h += value; } remove { h -= value; } } public void Raise() => h?.Invoke(); }
