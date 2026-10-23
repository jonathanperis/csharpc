// Constant patterns compare with Equals semantics: `double.NaN` / `float.NaN` patterns match NaN
// (an ordered == never does). Against an object/interface input, a constant pattern is a type
// test for the constant's type followed by a value comparison.
using System;

foreach (double m in new[] { double.NaN, -1.0, 0.0, -0.0, 2.5 })
{
    switch (m)
    {
        case < 0.0: Console.WriteLine("negative"); break;
        case double.NaN: Console.WriteLine("NaN"); break;
        case 0.0: Console.WriteLine("zero"); break;
        default: Console.WriteLine("other"); break;
    }
}
float f = float.NaN;
Console.WriteLine(f switch { float.NaN => "NaN", _ => "other" });
Console.WriteLine(1f switch { float.NaN => "NaN", _ => "other" });
double d = double.NaN;
Console.WriteLine(d is double.NaN);
Console.WriteLine(d is not double.NaN);
Console.WriteLine(3.0 is double.NaN);
Console.WriteLine(d is double.NaN or 1.0);
double? nd = double.NaN;
Console.WriteLine(nd is double.NaN);
nd = null;
Console.WriteLine(nd is double.NaN);
Console.WriteLine(Classify(double.PositiveInfinity) + " " + Classify(double.NaN) + " " + Classify(0.0));

object[] values = { double.NaN, 1.5, 2.5, 1.5f, float.NaN, 1, 1L, "abc", new string('a', 1) + "bc", 'x', Color.Red, Color.Green, null!, true };
foreach (object o in values)
{
    Console.WriteLine($"{o ?? "null"}: {o is double.NaN} {o is 1.5} {o is 2.5} {o is float.NaN} {o is 1.5f} {o is 1} {o is 1L} {o is "abc"} {o is 'x'} {o is Color.Red} {o is true} {o is null}");
    Console.WriteLine(o switch
    {
        double.NaN => "  double NaN",
        1.5 => "  double 1.5",
        1 => "  int 1",
        "abc" => "  string abc",
        Color.Red => "  Color.Red",
        float.NaN => "  float NaN",
        null => "  null",
        _ => "  other",
    });
}
IComparable c = 42;
Console.WriteLine(c is 42);
Console.WriteLine(c is 42L);
ValueType v = 7.0;
Console.WriteLine(v is 7.0);

static string Classify(double x) => x switch
{
    double.PositiveInfinity => "+inf",
    double.NaN => "nan",
    _ => "finite",
};

enum Color { Red, Green }
