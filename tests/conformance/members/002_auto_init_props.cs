var u = new User { Name = "x" };
Console.WriteLine(u.Name + u.Id);
class User { public string Name { get; init; } = ""; public int Id { get; } = 7; }
