// Property, extended property, positional (Deconstruct / tuple) and list-free recursive patterns.
var shapes = new Shape[] { new Circle(1), new Rect(2, 3), new Rect(4, 4), new Circle(10), new Line(new Point(0, 0), new Point(3, 4)) };
foreach (var s in shapes) Console.WriteLine(Describe(s));

Console.WriteLine(Rps("rock", "scissors") + " " + Rps("paper", "scissors") + " " + Rps("rock", "rock"));

string? text = "hello";
Console.WriteLine(text is { Length: > 3 } ? "long" : "short");
text = null;
Console.WriteLine(text is not null and { Length: > 3 } ? "long" : "nope");

var p = new Point(2, -3);
Console.WriteLine(p switch
{
    (0, 0) => "origin",
    (var x, 0) => $"x-axis at {x}",
    ( > 0, < 0) => "fourth quadrant",
    _ => "elsewhere",
});
Console.WriteLine(Quadrant(new Point(-1, 5)) + " " + Quadrant(new Point(0, 0)));

var order = new Order(new Customer("Ada", true), 250m == 250m ? 250 : 0);
Console.WriteLine(order is { Customer: { IsVip: true, Name: var who }, Total: >= 100 } ? $"discount for {who}" : "no discount");
Console.WriteLine(order is { Customer.Name.Length: 3 } ? "three-letter name" : "other");

static string Describe(Shape s) => s switch
{
    Circle { Radius: 0 } => "point",
    Circle { Radius: > 5 } c => $"big circle r={c.Radius}",
    Circle c => $"circle r={c.Radius}",
    Rect { Width: var w, Height: var h } when w == h => $"square {w}",
    Rect r => $"rect {r.Width}x{r.Height}",
    Line { From: (0, 0), To: var (tx, ty) } => $"line from origin to ({tx}, {ty})",
    _ => "unknown",
};

static string Rps(string a, string b) => (a, b) switch
{
    ("rock", "scissors") or ("paper", "rock") or ("scissors", "paper") => "first",
    var (x, y) when x == y => "tie",
    _ => "second",
};

static string Quadrant(Point pt) => pt switch
{
    { X: 0, Y: 0 } => "origin",
    { X: < 0, Y: > 0 } => "II",
    _ => "other",
};

abstract class Shape { }
class Circle(double r) : Shape { public double Radius => r; }
class Rect(double w, double h) : Shape { public double Width => w; public double Height => h; }
class Line(Point from, Point to) : Shape { public Point From => from; public Point To => to; }

struct Point(int x, int y)
{
    public int X => x;
    public int Y => y;
    public void Deconstruct(out int px, out int py) { px = x; py = y; }
}

class Customer(string name, bool vip) { public string Name => name; public bool IsVip => vip; }
class Order(Customer customer, int total) { public Customer Customer => customer; public int Total => total; }
