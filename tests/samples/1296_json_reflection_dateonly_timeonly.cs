#:property PublishAot=false
// A reflection-based serializer lists the public constructors of DateOnly and TimeOnly, among them overloads the corelib's
// shadows do not have (DateOnly(y, m, d, Calendar)); listing must not turn into an invalid-IR compiler error.
using System;
using System.Text.Json;

class A
{
    public DateOnly D { get; set; }
    public TimeOnly T { get; set; }
}

static class P
{
    static void Main()
    {
        var a = new A { D = new DateOnly(2020, 1, 2), T = new TimeOnly(3, 4, 5) };
        var json = JsonSerializer.Serialize(a);
        Console.WriteLine(json);
        var b = JsonSerializer.Deserialize<A>(json)!;
        Console.WriteLine($"{b.D} {b.T}");
    }
}
