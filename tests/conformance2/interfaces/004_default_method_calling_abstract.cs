IGreeter g = new En();
Console.WriteLine(g.Greet("Ada"));
Console.WriteLine(new Fr().Greet2());
interface IGreeter { string Hello { get; } string Greet(string n) => $"{Hello}, {n}!"; }
class En : IGreeter { public string Hello => "Hello"; }
class Fr : IGreeter { public string Hello => "Bonjour"; public string Greet2() => ((IGreeter)this).Greet("Marie"); }
