using System;

// A field of a library struct in a base class (LockCookie) used to leave a half-built class layout behind when the struct
// was not supported; the derived class then crashed LLVM (SIGSEGV). LockCookie comes from System.Threading.dll's IL since
// #414, so the layouts are built and the program runs as under .NET.
public class Base
{
    public System.Threading.LockCookie Date { get; set; }
    public int Temperature { get; set; }
}

public class Derived : Base
{
    public string City { get; set; } = "";
}

public struct Reading
{
    public System.Threading.LockCookie When;
    public int Value;
}

public class Holder
{
    public Reading Last;
}

class Program
{
    static void Main()
    {
        var d = new Derived { City = "x", Temperature = 3 };
        Console.WriteLine(d.City + d.Temperature);
        object boxed = new Holder().Last;
        Console.WriteLine(boxed is Reading);
    }
}
