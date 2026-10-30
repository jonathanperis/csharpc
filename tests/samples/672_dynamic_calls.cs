// `dynamic` calls: overload resolution with the run-time types of the arguments, named/optional/params
// arguments, ref and out, constructors, generic methods, user-defined operators and conversions, events,
// delegates and struct receivers.
using System;
using System.Collections.Generic;

struct Money
{
    public decimal Amount;
    public Money(decimal amount) { Amount = amount; }
    public static Money operator +(Money a, Money b) => new Money(a.Amount + b.Amount);
    public static Money operator *(Money a, int factor) => new Money(a.Amount * factor);
    public static bool operator ==(Money a, Money b) => a.Amount == b.Amount;
    public static bool operator !=(Money a, Money b) => a.Amount != b.Amount;
    public static implicit operator Money(int whole) => new Money(whole);
    public static explicit operator double(Money m) => (double)m.Amount;
    public override bool Equals(object o) => o is Money m && m.Amount == Amount;
    public override int GetHashCode() => Amount.GetHashCode();
    public override string ToString() => Amount.ToString("0.00") + "$";
}

struct Counter
{
    public int Count;
    public void Increment(int by) => Count += by;
}

class Calc
{
    public string Kind(int x) => "int";
    public string Kind(long x) => "long";
    public string Kind(double x) => "double";
    public string Kind(string x) => "string";
    public string Kind(object x) => "object";
    public string Kind(IEnumerable<int> x) => "IEnumerable<int>";
    public string Sum(params int[] values) { var s = 0; foreach (var v in values) s += v; return "sum=" + s; }
    public string Greet(string name, string greeting = "Hello", int times = 1) => string.Join(" ", Repeat(greeting + ", " + name, times));
    public bool TryHalf(int value, out int half) { half = value / 2; return value % 2 == 0; }
    public void Twice(ref int value) => value *= 2;
    public static string Static(int x) => "static " + x;
    public T Echo<T>(T value) => value;
    public string Pair<A, B>(A a, B b) => typeof(A).Name + "+" + typeof(B).Name;
    public Func<int, int> Square = x => x * x;
    public event Action<string> Changed;
    public void Raise(string what) => Changed?.Invoke(what);
    public int this[int i] => i * 10;
    public string this[string key] => "key:" + key;
    static IEnumerable<string> Repeat(string s, int n) { for (var i = 0; i < n; i++) yield return s; }
}

class Shape
{
    public string Name;
    public Shape(string name) { Name = name; }
    public Shape(int sides) { Name = sides + "-gon"; }
    public Shape(string name, int sides) { Name = name + "/" + sides; }
}

class Program
{
    static void Main()
    {
        dynamic calc = new Calc();
        foreach (var value in new object[] { 1, 2L, 3.0, "four", 'c', 5f, (short)6, new List<int> { 7 }, new int[] { 8 } })
        {
            dynamic v = value;
            Console.WriteLine($"{value} -> {calc.Kind(v)}");
        }
        Console.WriteLine(calc.Sum());
        Console.WriteLine(calc.Sum(1, 2, 3));
        dynamic three = 3;
        Console.WriteLine(calc.Sum(three, three));
        Console.WriteLine(calc.Sum(new[] { 4, 5 }));
        Console.WriteLine(calc.Greet("Ana"));
        Console.WriteLine(calc.Greet("Bo", "Hi"));
        Console.WriteLine(calc.Greet("Cy", times: 2));
        Console.WriteLine(calc.Greet(greeting: "Yo", name: "Di"));

        int half;
        Console.WriteLine(calc.TryHalf(10, out half) + " " + half);
        Console.WriteLine(calc.TryHalf(7, out half) + " " + half);
        var number = 21;
        calc.Twice(ref number);
        Console.WriteLine(number);

        Console.WriteLine(Calc.Static(three));
        Console.WriteLine(calc.Echo(three) + 1);
        Console.WriteLine(calc.Echo("text").Length);
        Console.WriteLine(calc.Pair(three, "s"));
        Console.WriteLine(calc.Pair<long, object>(three, 1));
        Console.WriteLine(calc.Square(9));
        Console.WriteLine(calc[4] + calc["k"]);

        calc.Changed += new Action<string>(s => Console.WriteLine("changed: " + s));
        calc.Raise("first");

        dynamic shapeArg = 4;
        dynamic shape = new Shape(shapeArg);
        Console.WriteLine(shape.Name);
        shapeArg = "tri";
        Console.WriteLine(new Shape(shapeArg).Name);
        Console.WriteLine(new Shape(shapeArg, three).Name);

        // User-defined operators and conversions.
        dynamic m = new Money(2.5m);
        Console.WriteLine(m + m);
        Console.WriteLine(m * 3);
        Console.WriteLine(m + 4);
        Console.WriteLine(m == new Money(2.5m));
        Console.WriteLine(m != m);
        Console.WriteLine((double)m);
        Money fromInt = three;
        Console.WriteLine(fromInt);

        // Struct receivers: a struct variable is passed by reference, a boxed struct is mutated in the box.
        var counter = new Counter();
        counter.Increment(three);
        counter.Increment(three);
        Console.WriteLine(counter.Count);
        dynamic boxed = new Counter();
        boxed.Increment(5);
        boxed.Count += 1;
        Console.WriteLine(boxed.Count);

        // Delegates.
        dynamic add = new Func<int, int, int>((x, y) => x + y);
        Console.WriteLine(add(2, 3));
        Console.WriteLine(add.Invoke(three, three));
        dynamic print = new Action<object>(o => Console.WriteLine("print " + o));
        print("hi");
        print(three);
    }
}
