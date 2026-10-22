var s = new Stack2<P>();
s.Push(new P(1)); s.Push(new P(2));
Console.WriteLine(s.Describe());
class Stack2<T> where T : IDescribable { List<T> items = new(); public void Push(T t) => items.Add(t); public string Describe() => string.Join(",", items.ConvertAll(i => i.Describe())); }
interface IDescribable { string Describe(); }
struct P(int v) : IDescribable { public string Describe() => $"p{v}"; }
