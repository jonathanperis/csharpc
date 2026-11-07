using System;
using System.Linq;
using System.Reflection;

// MethodInfo.CreateDelegate without a target (open instance delegates) makes an entry point for every listed
// method; Type.TypeHandle returns System.RuntimeTypeHandle, a struct csharpc has no layout for. That method has
// no delegate function (CreateDelegate of it throws NotSupportedException) but the program must still compile:
// the signature's types were mapped outside the try, so the compiler stopped with "struct 'System.RuntimeTypeHandle'"
// (the TechEmpower Platform entry, whose ASP.NET Core code lists the members of Type).
class Counter
{
    public int Value = 5;
    public int Get() => Value;
}

class Program
{
    static void Main()
    {
        PropertyInfo[] properties = typeof(Type).GetProperties();
        Console.WriteLine(properties.Any(p => p.Name == "TypeHandle"));
        Console.WriteLine(properties.Any(p => p.Name == "IsAbstract"));

        var open = (Func<Counter, int>)typeof(Counter).GetMethod("Get").CreateDelegate(typeof(Func<Counter, int>));
        Console.WriteLine(open(new Counter { Value = 9 }));
    }
}
