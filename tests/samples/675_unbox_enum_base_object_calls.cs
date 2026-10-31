// Regressions fixed for expression trees: unboxing follows the CLR (an enum and its underlying primitive
// unbox as each other, int and uint don't), `is Delegate` / `as MulticastDelegate`, and calls of
// System.Object's own ToString/Equals/GetHashCode from overrides (`base.X()`), also inside the corelib
// (MemberInfo.GetHashCode used to recurse forever).
#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

enum Color { Red, Green = 5 }
enum Shade : byte { Light = 1, Dark = 2 }
enum Other { First, Second = 5 }

class Named
{
    public string Name = "n";
    public override string ToString() => "Named(" + base.ToString() + ")";
    public override bool Equals(object obj) => obj is Named && base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode() == base.GetHashCode() ? 7 : 0;
}

class Program
{
    static void Main()
    {
        object red = Color.Red, green = Color.Green, one = 1, shade = Shade.Dark;
        Console.WriteLine((int)green);
        Console.WriteLine((Color)one);
        Console.WriteLine((Other)green);
        Console.WriteLine((byte)shade);
        Console.WriteLine((Color)(object)5 == Color.Green);
        // Nullable<T> unboxes the exact type only.
        try { int? maybe = (int?)green; Console.WriteLine(maybe); } catch (InvalidCastException) { Console.WriteLine("int?: InvalidCast"); }
        Color? exact = (Color?)green;
        Console.WriteLine(exact);
        try { Console.WriteLine((uint)one); } catch (InvalidCastException) { Console.WriteLine("uint: InvalidCast"); }
        try { Console.WriteLine((long)red); } catch (InvalidCastException) { Console.WriteLine("long: InvalidCast"); }
        try { Console.WriteLine((Shade)red); } catch (InvalidCastException) { Console.WriteLine("Shade: InvalidCast"); }
        Console.WriteLine(green is int);
        Console.WriteLine(one is Color);

        Func<int> f = () => 1;
        object d = f, s = "x";
        Console.WriteLine($"{d is Delegate} {d is MulticastDelegate} {s is Delegate} {(d as Delegate) != null}");
        try { var x = (Delegate)s; Console.WriteLine(x); } catch (InvalidCastException e) { Console.WriteLine(e.Message); }

        var n = new Named();
        Console.WriteLine(n.ToString());
        Console.WriteLine($"{n.Equals(n)} {n.Equals(new Named())} {n.GetHashCode()}");

        MethodInfo m = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        var set = new HashSet<MethodInfo> { m, m };
        Console.WriteLine($"{set.Count} {m.GetHashCode() == m.GetHashCode()} {m.Equals(m)}");
    }
}
