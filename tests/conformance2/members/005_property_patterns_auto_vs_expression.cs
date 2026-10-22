var p = new Person("Ada", "Lovelace");
Console.WriteLine(p.Full + " " + p.Initials + " " + p.Version);
p.Version++;
Console.WriteLine(p.Version);
class Person(string first, string last) { public string Full => $"{first} {last}"; public string Initials { get; } = $"{first[0]}{last[0]}"; public int Version { get; set; } = 1; }
