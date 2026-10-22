var people = new List<Person> { new("ann", 30), new("bob", 20) };
people.Sort((a, b) => a.Age.CompareTo(b.Age));
foreach (var p in people) Console.WriteLine(p);
Console.WriteLine(people.Find(p => p.Name == "ann")?.Age);
record Person(string Name, int Age);
