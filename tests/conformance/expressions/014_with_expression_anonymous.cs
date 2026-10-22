var anon = new { Name = "x", Age = 3 };
Console.WriteLine(anon.Name + anon.Age);
var older = anon with { Age = 4 };
Console.WriteLine(older);
