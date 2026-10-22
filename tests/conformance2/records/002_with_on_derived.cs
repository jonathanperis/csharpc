Person p = new Student("Ada", 3);
var q = p with { Name = "Bea" };
Console.WriteLine(q);
Console.WriteLine(q.GetType().Name);
record Person(string Name);
record Student(string Name, int Year) : Person(Name);
