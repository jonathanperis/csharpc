Shape[] s = { new Sq(2), new Ci(1) };
foreach (var x in s) Console.WriteLine($"{x.Name} {x.Area:F2} {x.Describe()}");
abstract class Shape { public abstract string Name { get; } public abstract double Area { get; } public virtual string Describe() => $"{Name}!"; }
class Sq(double a) : Shape { public override string Name => "square"; public override double Area => a * a; }
class Ci(double r) : Shape { public override string Name => "circle"; public override double Area => Math.PI * r * r; public override string Describe() => "round " + base.Describe(); }
