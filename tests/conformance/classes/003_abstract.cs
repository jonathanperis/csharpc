Shape s = new Sq(3);
Console.WriteLine(s.Area() + " " + s.Describe());
abstract class Shape { public abstract double Area(); public string Describe() => "area=" + Area(); }
class Sq(double side) : Shape { public override double Area() => side * side; }
