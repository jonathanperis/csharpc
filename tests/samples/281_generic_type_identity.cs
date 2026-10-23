using System;
using System.Collections.Generic;

// typeof and GetType() of closed generic types (and types nested in them) name the type arguments
// like the CLR, and the Type objects of a boxed value and of typeof are the same.
struct G<T> { public T V; }
class C<T> { }

class Outer<T>
{
    public class Mid
    {
        public struct In { public int X; }
        public class InC { }
    }
    public struct S { }
}

class Program
{
    static void Main()
    {
        object o = new G<int>();
        Console.WriteLine(o.GetType() == typeof(G<int>));
        Console.WriteLine(new G<int>().GetType() == typeof(G<int>));
        Console.WriteLine(o.GetType() == typeof(G<long>));
        Console.WriteLine(typeof(G<int>));
        Console.WriteLine(typeof(C<int>));
        Console.WriteLine(new C<int>().GetType() == typeof(C<int>));
        Console.WriteLine(typeof(List<int>));
        Console.WriteLine(new List<int>().GetType());
        Console.WriteLine(new List<int>().GetType() == typeof(List<int>));
        Console.WriteLine(typeof(List<int>.Enumerator));
        Console.WriteLine(typeof(KeyValuePair<int, string>));
        Console.WriteLine(typeof(List<>));
        Console.WriteLine(typeof(G<>));
        Console.WriteLine(typeof(Func<int, string>));
        Console.WriteLine(typeof(IEnumerable<int>));
        Console.WriteLine(typeof(Dictionary<string, int>.KeyCollection));
        object kv = new KeyValuePair<int, string>(1, "a");
        Console.WriteLine(kv.GetType() == typeof(KeyValuePair<int, string>));

        Console.WriteLine(new Outer<int>.Mid.In().GetType());
        object inner = new Outer<int>.Mid.In();
        Console.WriteLine(inner.GetType());
        Console.WriteLine(inner.GetType() == typeof(Outer<int>.Mid.In));
        Console.WriteLine(new Outer<string>.Mid.InC().GetType());
        Console.WriteLine(new Outer<string>.S().GetType());
        Console.WriteLine(new Outer<string>.Mid().GetType().Name);
    }
}
