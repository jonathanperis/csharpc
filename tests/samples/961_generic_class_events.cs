// Field-like events declared in generic classes: instance and static, in a derived generic class, an
// interface implementation, and several instantiations side by side.
using System;

class Observable<T>
{
    public event EventHandler<T>? Changed;
    public static event Action<T>? Broadcast;
    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
    public void Raise(T value) => Changed?.Invoke(this, value);
    public static void Announce(T value) => Broadcast?.Invoke(value);
    public void SubscribeSelf() => Changed += (_, v) => Console.WriteLine($"self saw {v}");
}

interface INotify<T>
{
    event Action<T> Notified;
}

class Notifier<T> : INotify<T>
{
    public event Action<T>? Notified;
    public void Fire(T value) => Notified?.Invoke(value);
}

class Derived<T> : Observable<T>
{
    public event Action? Done;
    public void Finish() => Done?.Invoke();
}

class Pair<TKey, TValue>
{
    public event Action<TKey, TValue>? Added;
    public void Add(TKey key, TValue value) => Added?.Invoke(key, value);
}

class Program
{
    static void Main()
    {
        var o = new Observable<int>();
        EventHandler<int> handler = (_, v) => Console.WriteLine($"changed {v}");
        o.Changed += handler;
        o.Raise(5);
        Console.WriteLine(o.Subscribers);
        o.Changed -= handler;
        o.Raise(6);
        Console.WriteLine(o.Subscribers);
        o.SubscribeSelf();
        o.Raise(7);

        var s = new Observable<string>();
        s.Changed += (_, v) => Console.WriteLine("string " + v);
        s.Raise("hello");
        o.Raise(8);

        Observable<int>.Broadcast += v => Console.WriteLine($"broadcast int {v}");
        Observable<string>.Broadcast += v => Console.WriteLine($"broadcast string {v}");
        Observable<int>.Announce(1);
        Observable<string>.Announce("two");

        INotify<double> n = new Notifier<double>();
        n.Notified += v => Console.WriteLine($"notified {v}");
        ((Notifier<double>)n).Fire(2.5);

        var d = new Derived<long>();
        d.Changed += (_, v) => Console.WriteLine($"derived changed {v}");
        d.Done += () => Console.WriteLine("done");
        d.Raise(9L);
        d.Finish();

        var p = new Pair<string, int>();
        p.Added += (k, v) => Console.WriteLine($"{k}={v}");
        p.Add("a", 1);
    }
}
