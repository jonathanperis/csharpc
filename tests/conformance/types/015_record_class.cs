var p = new Person("Ada", 36);
Console.WriteLine(p);
var q = p with { Age = 37 };
Console.WriteLine(q.Age + " " + p.Age);
Console.WriteLine(p == new Person("Ada", 36));
var (n, a) = p;
Console.WriteLine(n + a);
record Person(string Name, int Age);
