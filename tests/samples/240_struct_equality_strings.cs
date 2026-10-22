// ValueType.Equals/GetHashCode on structs with string fields compare by value (field by field), not
// by reference: struct keys in Dictionary/HashSet, List.Contains/IndexOf, Distinct, boxed Equals.
using System;
using System.Collections.Generic;
using System.Linq;

static string Make(string s, int n) => s + n; // a fresh string instance each call

var a = new Person { Name = Make("ann", 1), Age = 30 };
var b = new Person { Name = Make("ann", 1), Age = 30 };
var c = new Person { Name = Make("ann", 2), Age = 30 };
Console.WriteLine($"{a.Equals(b)} {a.Equals(c)} {a.GetHashCode() == b.GetHashCode()} {ReferenceEquals(a.Name, b.Name)}");
Console.WriteLine($"{new Person().Equals(new Person())} {new Person().Equals(a)} {a.Equals(new Person { Age = 30 })}");

object boxedA = a, boxedB = b;
Console.WriteLine($"{boxedA.Equals(boxedB)} {Equals(boxedA, boxedB)} {boxedA.Equals(null)} {boxedA.Equals("ann1")} {boxedA.Equals(new Prop { Name = "ann1", Age = 30 })}");

// Auto-properties: their backing fields take part like any other field.
var p1 = new Prop { Name = Make("p", 1), Age = 5 };
var p2 = new Prop { Name = Make("p", 1), Age = 5 };
Console.WriteLine($"{p1.Equals(p2)} {p1.GetHashCode() == p2.GetHashCode()}");

var ages = new Dictionary<Person, string>();
for (int i = 0; i < 50; i++) ages[new Person { Name = Make("n", i % 10), Age = i % 3 }] = "v" + i;
Console.WriteLine(ages.Count + " " + ages[new Person { Name = Make("n", 4), Age = 1 }] + " " + ages.ContainsKey(new Person { Name = "n4", Age = 2 }) + " " + ages.ContainsKey(new Person { Name = "n4", Age = 9 }));

var set = new HashSet<Person> { a, b, c, new Person { Name = null, Age = 30 }, new Person { Name = null, Age = 30 } };
Console.WriteLine(set.Count + " " + set.Contains(new Person { Name = Make("ann", 2), Age = 30 }));

var list = new List<Person> { c, a, new Person() };
Console.WriteLine($"{list.Contains(b)} {list.IndexOf(b)} {list.IndexOf(new Person())} {list.LastIndexOf(new Person { Name = Make("ann", 2), Age = 30 })} {list.Remove(b)} {list.Count}");
Person[] array = [c, a, b];
Console.WriteLine($"{Array.IndexOf(array, b)} {array.Distinct().Count()} {Enumerable.Contains(array, new Person { Name = "ann1", Age = 30 })}");
Console.WriteLine(EqualityComparer<Person>.Default.Equals(a, b) + " " + (EqualityComparer<Person>.Default.GetHashCode(a) == EqualityComparer<Person>.Default.GetHashCode(b)));

var groups = Enumerable.Range(0, 20).Select(i => new Person { Name = Make("g", i % 4), Age = 1 }).GroupBy(p => p).Select(g => g.Key.Name + ":" + g.Count());
Console.WriteLine(string.Join(" ", groups));

struct Person
{
    public string Name;
    public int Age;
}

struct Prop
{
    public string Name { get; set; }
    public int Age { get; init; }
}
