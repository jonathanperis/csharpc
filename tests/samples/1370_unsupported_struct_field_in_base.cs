using System;

// The scenario sample 283 covered until LockCookie became supported (#414): a field of a library struct csharpc doesn't
// support (ArrayWithOffset; ModuleHandle until it became supported, #104) in a base class used to leave a half-built class
// layout behind; the derived class then crashed LLVM (SIGSEGV). The expected outcome is an explicit diagnostic at each use (1370_unsupported_struct_field_in_base.diagnostics).
public class Base
{
    public System.Runtime.InteropServices.ArrayWithOffset Date { get; set; }
    public int Temperature { get; set; }
}

public class Derived : Base
{
    public string City { get; set; } = "";
}

public struct Reading
{
    public System.Runtime.InteropServices.ArrayWithOffset When;
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
