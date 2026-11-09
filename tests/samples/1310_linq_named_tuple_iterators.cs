// System.Linq from the framework's IL: Iterator<T>.Select/Where/Skip/Take are generic virtual methods, called through types whose
// type arguments are named tuples (`Iterator<(string Name, int Age)>`) on iterators the library instantiates with unnamed ones
// (`OrderedIterator<(string, int)>`): tuple element names are not part of a type's runtime identity.
var people = new[] { (Name: "ann", Age: 31), (Name: "bob", Age: 25), (Name: "cy", Age: 40), (Name: "di", Age: 25) };

Console.WriteLine(string.Join(",", people.OrderByDescending(p => p.Age).Select(p => p.Name)));
Console.WriteLine(string.Join(",", people.OrderBy(p => p.Name).Where(p => p.Age > 26).Select(p => p.Age)));
Console.WriteLine(string.Join(",", people.OrderBy(p => p.Age).ThenByDescending(p => p.Name).Skip(1).Take(2).Select(p => (p.Name, Double: p.Age * 2))));
Console.WriteLine(string.Join(";", people.GroupBy(p => p.Age / 10).OrderBy(g => g.Key).Select(g => (g.Key, Names: string.Join("+", g.Select(p => p.Name))))));
Console.WriteLine(string.Join(",", people.Where(p => p.Age < 35).OrderBy(p => p.Age).Select(p => p.Name).Reverse()));
Console.WriteLine(people.OrderBy(p => p.Age).Select(p => p.Age).Sum() + " " + people.OrderBy(p => p.Name).Select(p => p.Name).Last());
var pairs = people.Select(p => (Left: p.Name, Right: p.Age)).ToList();
Console.WriteLine(string.Join(",", pairs.OrderByDescending(p => p.Right).ThenBy(p => p.Left).Take(3).Select(p => p.Left + p.Right)));
Console.WriteLine(string.Join(",", pairs.ToDictionary(p => p.Left, p => p.Right).OrderBy(kv => kv.Key).Select(kv => kv.Value)));
