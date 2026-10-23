using System;
using System.Collections.Generic;

// Legal C# that instantiates generics recursively (C<T> uses C<C<T>>, F<T> calls F<List<T>>).
// .NET instantiates lazily at run time; an ahead-of-time compiler would expand forever, so csharpc
// rejects it with an explicit diagnostic (284_recursive_generic_expansion.diagnostics) instead of
// exhausting memory.
class C<T>
{
    public int Depth(int n) => n == 0 ? 0 : 1 + new C<C<T>>().Depth(n - 1);
}

class Program
{
    static int F<T>(int n) => n == 0 ? 0 : 1 + F<List<T>>(n - 1);

    static void Main()
    {
        Console.WriteLine(new C<int>().Depth(3));
        Console.WriteLine(F<int>(3));
    }
}
