// Type tests on a generic parameter instantiated with a struct (ILogger.Log<TState> with a struct state).
using System;
using System.Collections.Generic;

Console.WriteLine(Check(new Pairs()));
Console.WriteLine(Check(42));
Console.WriteLine(Check("text"));
Console.WriteLine(Check(new Plain(3)));
Console.WriteLine(Check<int?>(5));
Console.WriteLine(Check<int?>(null));
Console.WriteLine(Count(new Pairs()));

static string Check<T>(T state) =>
    (state is IReadOnlyList<KeyValuePair<string, object>>) + " " + (state is IComparable) + " " + (state is object);

static int Count<T>(T state) => state is IReadOnlyList<KeyValuePair<string, object>> list ? list.Count : -1;

readonly struct Pairs : IReadOnlyList<KeyValuePair<string, object>>
{
    public KeyValuePair<string, object> this[int index] => new("k" + index, index);
    public int Count => 2;
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { yield return this[0]; yield return this[1]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

readonly record struct Plain(int Value);
