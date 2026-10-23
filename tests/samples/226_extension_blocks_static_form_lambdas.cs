// C# 14 extension blocks: members called in their static form (E.M(receiver, ...)) and lambdas /
// local functions inside extension members (capturing the receiver).
using System;
using System.Collections.Generic;
using System.Linq;

string s = "Hello Extension Methods";
Console.WriteLine(s.WordCount());
Console.WriteLine(E.WordCount(s));
Console.WriteLine(E.Repeat(s, 2).Length);
Console.WriteLine(new[] { 3.0, 1.0, 2.0 }.Smallest());
Console.WriteLine(E.Smallest(new List<double> { 5, 4 }));
Console.WriteLine(string.Join(",", new[] { 1, 2, 3, 4 }.EveryOther()));
Console.WriteLine(string.Join(",", E.EveryOther(new[] { "a", "b", "c" })));
Console.WriteLine(new[] { 1, 5, 2 }.MaxBy2(x => -x));
Console.WriteLine(E.MaxBy2(new[] { "aa", "b", "cccc" }, x => x.Length));
Console.WriteLine(s.CountWhere(char.IsUpper));
Console.WriteLine(E.CountWhere("abcABC", c => c > 'a'));
Console.WriteLine(E.Describe(42));
Console.WriteLine(7.Doubler()() + E.Doubler(1)());

static class E
{
    extension(string str)
    {
        public int WordCount() => str.Split(' ').Length;
        public string Repeat(int times) => string.Concat(Enumerable.Repeat(str, times));
        public int CountWhere(Func<char, bool> predicate)
        {
            int Count() => str.Count(c => predicate(c));
            return Count();
        }
    }

    extension(IEnumerable<double> source)
    {
        public double Smallest() => source.OrderBy(n => n).First();
    }

    extension<T>(IEnumerable<T> items)
    {
        public IEnumerable<T> EveryOther() => items.Where((_, i) => i % 2 == 0);
        public T MaxBy2<TKey>(Func<T, TKey> key) where TKey : IComparable<TKey> =>
            items.Aggregate((best, x) => key(x).CompareTo(key(best)) > 0 ? x : best);
    }

    extension(int n)
    {
        public string Describe() => n switch { > 0 => $"positive {n}", _ => "other" };
        public Func<int> Doubler() => () => n * 2;
    }
}
