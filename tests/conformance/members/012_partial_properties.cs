var p = new P();
p.Name = "z";
Console.WriteLine(p.Name);
partial class P { public partial string Name { get; set; } }
partial class P { private string _n = ""; public partial string Name { get => _n; set => _n = value; } }
