IDrawable[] items = { new Square(), new Circle() };
foreach (var i in items) Console.WriteLine(i.Draw() + " " + ((Shape)i).Area());
interface IDrawable { string Draw(); }
abstract class Shape : IDrawable { public abstract double Area(); public virtual string Draw() => GetType().Name; }
class Square : Shape { public override double Area() => 4; }
class Circle : Shape { public override double Area() => 3.14; public override string Draw() => "(" + base.Draw() + ")"; }
