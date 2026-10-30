// `dynamic`: members, operators, conversions, indexers and collections bound at run time
// (csharpc binds them per run-time type at compile time: docs/adr/0019-dynamic.md).
using System;
using System.Collections.Generic;
using System.Text;

class Point
{
    public int X;
    public int Y { get; set; }
    public Point(int x, int y) { X = x; Y = y; }
    public int Sum() => X + Y;
    public string Describe(string prefix) => prefix + "(" + X + ", " + Y + ")";
    public string Describe(int times) => new string('P', times);
    public double Scale(double factor) => (X + Y) * factor;
    public override string ToString() => $"Point({X}, {Y})";
}

class Program
{
    static dynamic Make(int kind) => kind switch
    {
        0 => 42,
        1 => "text",
        2 => 2.5,
        3 => new Point(1, 2),
        4 => new List<int> { 3, 1, 2 },
        5 => 'c',
        6 => 10L,
        _ => true,
    };

    static void Main()
    {
        // Values of different run-time types through the same variable.
        for (var k = 0; k <= 7; k++)
        {
            dynamic v = Make(k);
            Console.WriteLine($"{k}: {v} is {v.GetType().Name}, ToString={v.ToString()}");
        }

        // Members: fields, properties, methods, overloads chosen by the run-time argument type.
        dynamic p = new Point(3, 4);
        Console.WriteLine(p.X + p.Y);
        p.X = 10;
        p.Y += 5;
        p.X++;
        Console.WriteLine(p.Sum());
        Console.WriteLine(p.Describe("at"));
        Console.WriteLine(p.Describe(3));
        dynamic arg = "dyn";
        Console.WriteLine(p.Describe(arg));
        arg = 2;
        Console.WriteLine(p.Describe(arg));
        Console.WriteLine(p.Scale(2));      // int argument converts to double
        Console.WriteLine(p.Scale(0.5f));
        Console.WriteLine(p);

        // Operators on numbers of every width, strings and mixes.
        dynamic a = 7, b = 2;
        Console.WriteLine($"{a + b} {a - b} {a * b} {a / b} {a % b} {a << b} {a >> 1} {a & b} {a | b} {a ^ b} {-a} {~a}");
        Console.WriteLine($"{a == 7} {a != b} {a < b} {a >= b}");
        dynamic big = 3000000000L, small = (byte)200;
        Console.WriteLine(big + small);
        Console.WriteLine((small + small).GetType());
        Console.WriteLine((big * 2.0).GetType());
        dynamic dec = 1.5m;
        Console.WriteLine(dec * 2);
        Console.WriteLine("x" + a + b);
        Console.WriteLine(a + b + "x");
        dynamic s = "hello";
        Console.WriteLine(s + " world");
        Console.WriteLine(s.ToUpper() + s.Length);
        Console.WriteLine(s[1]);
        Console.WriteLine(s.Substring(1, 3).Replace("l", "L"));
        Console.WriteLine(s == "hello");

        // Conditions and logical operators.
        dynamic yes = true, no = false;
        if (yes) Console.WriteLine("yes is true");
        if (!no) Console.WriteLine("no is false");
        Console.WriteLine(yes && no);
        Console.WriteLine(yes || no);
        Console.WriteLine(a > 5 && b < 5);
        Console.WriteLine(a > 100 ? "big" : "small");

        // Conversions back to static types.
        int i = a;
        long l = a;
        double d = a;
        string str = s;
        object o = p;
        Console.WriteLine($"{i} {l} {d} {str} {o}");
        Console.WriteLine((int)dec);
        Console.WriteLine((byte)big.GetHashCode());

        // Collections.
        dynamic list = new List<string> { "b", "a" };
        list.Add("c");
        list.Sort();
        Console.WriteLine(list.Count + " " + string.Join(",", list));
        list[0] = "z";
        Console.WriteLine(list[0] + list[2]);
        foreach (var item in list) Console.Write(item);
        Console.WriteLine();
        dynamic map = new Dictionary<string, int>();
        map["one"] = 1;
        map["two"] = 2;
        map["one"] += 10;
        Console.WriteLine(map["one"] + map["two"]);
        Console.WriteLine(map.ContainsKey("two"));
        dynamic array = new[] { 5, 6, 7 };
        array[1] *= 3;
        Console.WriteLine(array[1] + array.Length);
        int total = 0;
        foreach (var x in array) total += x;
        Console.WriteLine(total);

        // Static calls with dynamic arguments: overloads picked at run time.
        dynamic n = 9;
        Console.WriteLine(Math.Max(n, 4));
        Console.WriteLine(Math.Abs(n * -1.5));
        Console.WriteLine(string.Concat(n, "!"));
        n = null;
        Console.WriteLine(n == null);
        Console.WriteLine(n?.ToString() ?? "null value");
        dynamic sb = new StringBuilder();
        sb.Append(1).Append('x').Append("y").Append(2.5);
        Console.WriteLine(sb.ToString());
    }
}
