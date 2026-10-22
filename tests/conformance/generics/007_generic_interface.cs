IRepo<int> r = new MemRepo();
r.Add(4); r.Add(5);
Console.WriteLine(r.All().Count);
interface IRepo<T> { void Add(T x); List<T> All(); }
class MemRepo : IRepo<int> { List<int> _l = new(); public void Add(int x) => _l.Add(x); public List<int> All() => _l; }
