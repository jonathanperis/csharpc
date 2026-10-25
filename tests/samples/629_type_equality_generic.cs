// Type equality in generic code (folded to method-table comparisons after monomorphization) and
// `(TTo)(object)value` round trips (box + unbox.any of the same type, dropped in IL), next to the
// cases that must still compare at run time.
using System;
using System.Collections.Generic;

static class Kinds<T>
{
    public static string Of() =>
        typeof(T) == typeof(char) ? "char" :
        typeof(T) == typeof(byte) ? "byte" :
        typeof(T) != typeof(string) ? "other" : "string";

    public static int Code(T value) => typeof(T) == typeof(char) ? (char)(object)value! : typeof(T) == typeof(int) ? (int)(object)value! : -1;

    public static bool SameAs<U>() => typeof(T) == typeof(U);
    public static bool EqualsMethod<U>() => typeof(T).Equals(typeof(U));
}

struct Point { public int X, Y; }

static class Program
{
    static Type Pick(int i) => i switch { 0 => typeof(int), 1 => typeof(string), _ => typeof(List<int>) };

    static void Main()
    {
        Console.WriteLine($"{Kinds<char>.Of()} {Kinds<byte>.Of()} {Kinds<string>.Of()} {Kinds<Point>.Of()} {Kinds<int?>.Of()}");
        Console.WriteLine($"{Kinds<char>.Code('A')} {Kinds<int>.Code(-7)} {Kinds<string>.Code("x")}");
        Console.WriteLine($"{Kinds<int>.SameAs<int>()} {Kinds<int>.SameAs<long>()} {Kinds<List<int>>.SameAs<List<int>>()} {Kinds<List<int>>.SameAs<List<long>>()}");
        Console.WriteLine($"{Kinds<int?>.SameAs<int?>()} {Kinds<int?>.SameAs<int>()} {Kinds<object>.EqualsMethod<object>()} {Kinds<object>.EqualsMethod<string>()}");
        object boxed = 42, text = "hi", point = new Point();
        Console.WriteLine($"{boxed.GetType() == typeof(int)} {text.GetType() == typeof(string)} {point.GetType() != typeof(Point)} {text.GetType() == typeof(object)}");
        for (int i = 0; i < 3; i++)
            Console.WriteLine($"{i}: {Pick(i) == typeof(int)} {Pick(i) == typeof(string)} {Pick(i) != typeof(List<int>)} {Pick(i).Equals(Pick(2 - i))}");
        Type? none = null;
        Console.WriteLine($"{none == typeof(int)} {none != typeof(int)} {none == null}");
    }
}
