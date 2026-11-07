using System;
using System.Threading;

// Activator.CreateInstance<object>() and the LazyInitializer overloads that create a plain object (the lock
// objects of ASP.NET Core and Kestrel): T = object has no csharpc-compiled constructor, so the instance is
// allocated directly. The TechEmpower Platform entry could not be compiled without it.
class Program
{
    static object s_shared;
    static object s_lock;
    static bool s_initialized;

    static void Main()
    {
        object created = Activator.CreateInstance<object>();
        Console.WriteLine(created.GetType());
        Console.WriteLine(ReferenceEquals(created, Activator.CreateInstance<object>()));

        object a = LazyInitializer.EnsureInitialized(ref s_shared);
        object b = LazyInitializer.EnsureInitialized(ref s_shared);
        Console.WriteLine(a != null);
        Console.WriteLine(ReferenceEquals(a, b));
        Console.WriteLine(a.GetType() == typeof(object));

        string text = null;
        string made = LazyInitializer.EnsureInitialized(ref text, () => "made");
        Console.WriteLine(made + " " + LazyInitializer.EnsureInitialized(ref text, () => "again"));

        int value = 0;
        int first = LazyInitializer.EnsureInitialized(ref value, ref s_initialized, ref s_lock, () => 42);
        int second = LazyInitializer.EnsureInitialized(ref value, ref s_initialized, ref s_lock, () => 7);
        Console.WriteLine(first + " " + second + " " + s_initialized + " " + (s_lock != null));
    }
}
