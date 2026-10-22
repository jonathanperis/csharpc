// Anonymous types: members, ToString, value equality, nesting, arrays of them.
var ada = new { Name = "Ada", Age = 36 };
var ada2 = new { Name = "Ada", Age = 36 };
var point = new { X = 1.5, Y = -2.0, Label = (string?)null };
Console.WriteLine(ada);
Console.WriteLine(point);
Console.WriteLine($"{ada.Name} is {ada.Age}");
Console.WriteLine(ada.Equals(ada2));
Console.WriteLine(ada == ada2);                     // reference comparison
Console.WriteLine(ada.GetHashCode() == ada2.GetHashCode());
var nested = new { Person = ada, Tags = new[] { "math", "code" }, Empty = new { } };
Console.WriteLine(nested.Person.Name + " " + nested.Tags.Length + " " + nested.Empty);
var items = new[] { new { Sku = "A", Qty = 3 }, new { Sku = "B", Qty = 0 }, new { Sku = "C", Qty = 7 } };
int total = 0;
foreach (var item in items) if (item.Qty > 0) { total += item.Qty; Console.WriteLine(item); }
Console.WriteLine(total);
string name = "Grace";
int year = 1906;
var projected = new { name, year, Decade = year / 10 * 10 };
Console.WriteLine(projected);
