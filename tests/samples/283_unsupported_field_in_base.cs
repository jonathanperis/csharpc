using System;

// A field of a library struct csharpc doesn't support (StreamingContext) in a base class used to
// leave a half-built class layout behind; the derived class then crashed LLVM (SIGSEGV). The
// expected outcome is an explicit diagnostic at the field (283_unsupported_field_in_base.diagnostics).
public class Base
{
    public System.Runtime.Serialization.StreamingContext Date { get; set; }
    public int Temperature { get; set; }
}

public class Derived : Base
{
    public string City { get; set; } = "";
}

public struct Reading
{
    public System.Runtime.Serialization.StreamingContext When;
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
