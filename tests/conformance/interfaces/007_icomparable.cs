var people = new[] { new P("c", 3), new P("a", 1), new P("b", 2) };
Array.Sort(people);
foreach (var p in people) Console.WriteLine(p.Name);
class P(string name, int rank) : IComparable<P>
{
    public string Name = name;
    public int Rank = rank;
    public int CompareTo(P? o) => Rank.CompareTo(o!.Rank);
}
