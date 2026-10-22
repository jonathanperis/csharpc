// Exceptions raised by the runtime (null deref, bounds, /0, overflow, casts) are catchable.
string[] names = ["index", "divide", "modulo", "overflow", "abs", "null", "unbox", "cast", "negative array", "string index"];
for (int i = 0; i < names.Length; i++)
{
    try { Console.WriteLine(names[i] + " = " + Run(i)); }
    catch (Exception e) { Console.WriteLine(names[i] + ": " + e.Message); }
}
Console.WriteLine("all handled");

static int Run(int test)
{
    int[] numbers = [1, 2, 3];
    int zero = numbers.Length - 3;
    string? nothing = numbers.Length > 10 ? "x" : null;
    object boxed = 42;
    Shape shape = new Circle();
    switch (test)
    {
        case 0: return numbers[3];
        case 1: return 10 / zero;
        case 2: return 10 % zero;
        case 3: return int.MinValue / (zero - 1);
        case 4: return Math.Abs(int.MinValue + zero);
        case 5: return nothing!.Length;
        case 6: return (int)(long)boxed;
        case 7: return ((Square)shape).Side;
        case 8: return new int[zero - 1].Length;
        default: return "abc"[5];
    }
}

class Shape { }
class Circle : Shape { }
class Square : Shape { public int Side = 1; }
