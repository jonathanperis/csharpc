// Array.Sort: .NET's introsort ported exactly, so ties keep the CLR's order; Comparison<T> and IComparable<T>.
var people = new Person[]
{
    new("Ana", 31), new("Bo", 25), new("Cy", 31), new("Di", 19), new("Ed", 25), new("Flo", 40), new("Gus", 31),
    new("Hal", 19), new("Ivy", 25), new("Jo", 40), new("Kai", 31), new("Lu", 22), new("Mo", 25), new("Ned", 19),
    new("Oli", 33), new("Pia", 31), new("Quin", 22), new("Rex", 40), new("Sue", 25), new("Tom", 19), new("Uma", 31),
};
Array.Sort(people, (a, b) => a.Age.CompareTo(b.Age));
Show(people, false);
Array.Sort(people);          // IComparable<Person>: by name
Show(people, true);
Array.Sort(people, (a, b) => b.Age - a.Age);
Show(people, false);

var scores = new Score[] { new(3, "c"), new(1, "a"), new(2, "b"), new(1, "z"), new(3, "y") };
Array.Sort(scores);          // struct IComparable<Score>
foreach (var s in scores) Console.Write(s.Tag);
Console.WriteLine();

int[] big = new int[200];
uint seed = 12345;
for (int i = 0; i < big.Length; i++) { seed = seed * 1103515245 + 12345; big[i] = (int)(seed >> 16) % 1000; }
Array.Sort(big);
long checksum = 0;
for (int i = 0; i < big.Length; i++) checksum = checksum * 31 + big[i];
Console.WriteLine(big[0] + " " + big[100] + " " + big[199] + " " + checksum);
var words = new string[] { "pear", "Apple", "apple", "fig", "Fig", "banana", "Banana", "cherry", "date", "Date" };
Array.Sort(words, (x, y) => x.Length.CompareTo(y.Length));
Console.WriteLine(string.Join(",", words));
Array.Sort(words);
Console.WriteLine(string.Join(",", words));
Person?[] withNull = [new("b", 1), null, new("a", 2)];
Array.Sort(withNull);
foreach (var p in withNull) Console.Write((p?.Name ?? "null") + ",");
Console.WriteLine();
Console.WriteLine(double.Round(2.5) + " " + double.Round(3.14159, 2) + " " + int.Max(3, 9) + " " + int.Clamp(15, 0, 10) + " " + double.Sqrt(2) + " " + long.Abs(-5) + " " + int.Sign(-4));

static void Show(Person[] ps, bool namesOnly)
{
    var sb = new System.Text.StringBuilder();
    foreach (var p in ps) sb.Append(namesOnly ? p.Name : p.ToString()).Append(' ');
    Console.WriteLine(sb.ToString());
}

class Person(string name, int age) : IComparable<Person>
{
    public string Name { get; } = name;
    public int Age { get; } = age;
    public int CompareTo(Person? other) => other is null ? 1 : string.CompareOrdinal(Name, other.Name);
    public override string ToString() => $"{Name}:{Age}";
}

struct Score(int value, string tag) : IComparable<Score>
{
    public int Value = value;
    public string Tag = tag;
    public int CompareTo(Score other) => Value.CompareTo(other.Value);
}
