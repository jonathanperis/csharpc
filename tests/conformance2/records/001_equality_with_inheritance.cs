Person p = new Person("Ada");
Person s = new Student("Ada", 3);
Console.WriteLine(p == s);
Console.WriteLine(s == new Student("Ada", 3));
Console.WriteLine(s.Equals((object)new Student("Ada", 4)));
record Person(string Name);
record Student(string Name, int Year) : Person(Name);
