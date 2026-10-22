var shape = new Circle(2.5);
int total = 0;
for (int i = 1; i <= 3; i++)
    total += Fib(i + 5);
string label = "fib total";
Console.WriteLine($"{label}: {total} area={shape.Area()}");

static int Fib(int n)
{
    if (n < 2) return n;
    return Fib(n - 1) + Fib(n - 2);
}

class Circle(double radius)
{
    public double Radius = radius;
    public double Area() => Math.PI * Radius * Radius;
}
