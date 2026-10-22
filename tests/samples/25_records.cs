// Records: positional properties, value equality, ToString, with-expressions, Deconstruct, inheritance.
var ada = new Person("Ada", 36);
var ada2 = new Person("Ada", 36);
var bob = ada with { Name = "Bob" };
Console.WriteLine(ada);
Console.WriteLine(bob);
Console.WriteLine(ada == ada2);
Console.WriteLine(ada != bob);
Console.WriteLine(ada.Equals(ada2));
Console.WriteLine(ReferenceEquals(ada, ada2));
Console.WriteLine(ada.GetHashCode() == ada2.GetHashCode());
var (name, age) = bob;
Console.WriteLine($"{name} is {age}");

var e = new Employee("Grace", 45, "Navy");
Console.WriteLine(e);
Person asPerson = e;
Console.WriteLine(asPerson == new Person("Grace", 45));     // different runtime type -> false
Console.WriteLine(e == new Employee("Grace", 45, "Navy"));
var promoted = e with { Org = "Harvard", Age = 46 };
Console.WriteLine(promoted);

var item = new Item { Sku = "X1", Price = 9.5 };
Console.WriteLine(item);
Console.WriteLine(item with { Price = 10 });

var p1 = new Point(1, 2);
var p2 = p1 with { Y = 5 };
Console.WriteLine($"{p1} {p2} {p1 == new Point(1, 2)} {p1.Equals(p2)}");
object boxed = p1;
Console.WriteLine(boxed.Equals(new Point(1, 2)));

var empty = new Empty();
Console.WriteLine(empty);
Console.WriteLine(new Tagged("a", null));

record Person(string Name, int Age);
record Employee(string Name, int Age, string Org) : Person(Name, Age);
record Item
{
    public string Sku { get; init; } = "";
    public double Price { get; init; }
}
record struct Point(int X, int Y);
record Empty;
record Tagged(string Label, string? Note);
