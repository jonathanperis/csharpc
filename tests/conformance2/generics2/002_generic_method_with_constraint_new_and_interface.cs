var list = Build<Item>(3);
Console.WriteLine(string.Join(",", list.Select(i => i.Id)));
static List<T> Build<T>(int n) where T : IIdentified, new() { var l = new List<T>(); for (int i = 0; i < n; i++) { var t = new T(); t.Id = i * 10; l.Add(t); } return l; }
interface IIdentified { int Id { get; set; } }
class Item : IIdentified { public int Id { get; set; } }
