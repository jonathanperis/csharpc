// Virtual dispatch over a polymorphic array — the classic OO microbenchmark.
int n = 1_000_000;
Shape[] shapes = new Shape[n];
uint seed = 7;
for (int i = 0; i < n; i++)
{
    seed = seed * 1664525 + 1013904223;
    double size = (seed >> 8) % 1000 / 100.0 + 0.5;
    switch (seed % 4)
    {
        case 0: shapes[i] = new Circle(size); break;
        case 1: shapes[i] = new Square(size); break;
        case 2: shapes[i] = new Rectangle(size, size * 0.5); break;
        default: shapes[i] = new Triangle(size, size * 2); break;
    }
}
double total = 0;
for (int round = 0; round < 40; round++)
    for (int i = 0; i < shapes.Length; i++)
        total += shapes[i].Area() + shapes[i].Sides();
Console.WriteLine(total);

abstract class Shape
{
    public abstract double Area();
    public virtual int Sides() => 0;
}

sealed class Circle : Shape
{
    private readonly double _r;
    public Circle(double r) { _r = r; }
    public override double Area() => 3.141592653589793 * _r * _r;
}

class Rectangle : Shape
{
    protected readonly double W, H;
    public Rectangle(double w, double h) { W = w; H = h; }
    public override double Area() => W * H;
    public override int Sides() => 4;
}

sealed class Square : Rectangle
{
    public Square(double s) : base(s, s) { }
}

sealed class Triangle : Shape
{
    private readonly double _b, _h;
    public Triangle(double b, double h) { _b = b; _h = h; }
    public override double Area() => 0.5 * _b * _h;
    public override int Sides() => 3;
}
