// User-declared delegate types and method-group conversions.
BinOp plus = Add;
BinOp times = Multiply;
Console.WriteLine(Apply(plus, 6, 7));
Console.WriteLine(Apply(times, 6, 7));
Console.WriteLine(Apply((a, b) => a - b, 6, 7));

var calc = new Calculator(100);
Func<int, int> offset = calc.Offset;                 // instance method group
Console.WriteLine(offset(5));

Shape s = new Square(3);
Func<double> area = s.Area;                          // virtual: bound to Square.Area
Console.WriteLine(area());

Transformer upper = Shout;
Console.WriteLine(upper("quiet"));

Comparison<int> desc = (x, y) => y - x;
Console.WriteLine(desc(1, 2));

BinOp? none = null;
Console.WriteLine(none == null);
Console.WriteLine(plus != null);
Console.WriteLine(plus.ToString() != null);

static int Add(int a, int b) => a + b;
static int Multiply(int a, int b) => a * b;
static int Apply(BinOp op, int a, int b) => op(a, b);
static string Shout(string s) => s + "!!";

delegate int BinOp(int a, int b);
delegate string Transformer(string input);

class Calculator(int baseValue)
{
    public int Offset(int x) => baseValue + x;
}

abstract class Shape { public abstract double Area(); }
class Square(double side) : Shape { public override double Area() => side * side; }
