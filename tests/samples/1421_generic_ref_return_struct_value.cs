using System;
using System.Runtime.CompilerServices;

// The value of a ref-returning call whose type depends on the enclosing generic type's parameter (a generic struct), used as a
// delegate argument, a local and a return value: loaded at the instantiation's type.
var ints = new Holder<int>(x => Console.WriteLine($"int pair {x.A} {x.B}"));
ints.Set(1, 2);
Console.WriteLine(ints.Copy().B);
var strings = new Holder<string>(x => Console.WriteLine($"string pair {x.A} {x.B}"));
strings.Set("a", "b");
Console.WriteLine(strings.Copy().A);

struct Pair<T> { public T A; public T B; }
struct Mirror<T> { public T A; public T B; }

sealed class Holder<T>(Action<Mirror<T>> changed)
{
    private readonly Pair<T>[] _items = new Pair<T>[1];

    private ref Pair<T> First() => ref _items[0];

    public void Set(T a, T b)
    {
        var pair = new Pair<T> { A = a, B = b };
        _items[0] = pair;
        changed(Unsafe.As<Pair<T>, Mirror<T>>(ref pair));
        changed(Unsafe.As<Pair<T>, Mirror<T>>(ref First()));
    }

    public Pair<T> Copy()
    {
        Pair<T> local = First();
        return local;
    }
}
