using IlRoslynIdioms;

Special a = 3, b = 3, c = 7;
Console.WriteLine($"{Special.Size} {a} {a.Equals(b)} {a.Equals(c)} {a.Equals((object)b)}");
var byKind = new Dictionary<Special, string> { [a] = "three", [c] = "seven" };
Console.WriteLine(byKind[b]);

Console.WriteLine($"{Decimals.Zero} {Decimals.One} {Decimals.MinusOne} {Decimals.Max} {Decimals.Min}");
Console.WriteLine($"{Decimals.Sum([1.5m, 2.25m])} {Decimals.Sum([])}");
