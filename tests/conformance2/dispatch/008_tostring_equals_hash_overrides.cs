var a = new K(1); var b = new K(1);
Console.WriteLine($"{a} {a.Equals(b)} {a == b} {a.GetHashCode() == b.GetHashCode()}");
object oa = a;
Console.WriteLine(oa.Equals(b));
var set = new HashSet<K> { a, b };
Console.WriteLine(set.Count);
class K(int id) { public int Id = id; public override bool Equals(object? o) => o is K k && k.Id == Id; public override int GetHashCode() => Id; public override string ToString() => $"K{Id}"; }
