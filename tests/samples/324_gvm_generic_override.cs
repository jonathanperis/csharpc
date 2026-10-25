using System;

// A generic virtual method overridden in a generic class: its instantiations are only known once
// lowering is done, so the call goes through a dispatcher filled in rounds over every class with a
// method table (ModuleCodeGen.GenericInterfaces.cs), each case calling that class's override.
class Base
{
    public virtual string Describe<T>(T value) => "base " + value;
}

class Wrapper<U> : Base
{
    public override string Describe<T>(T value) => "wrapper<" + typeof(U).Name + "> " + value;
}

class Plain : Base
{
    public override string Describe<T>(T value) => "plain " + value;
}

class Program
{
    static void Main()
    {
        Base[] items = { new Base(), new Plain(), new Wrapper<int>(), new Wrapper<string>() };
        foreach (Base item in items) Console.WriteLine(item.Describe(42));
        foreach (Base item in items) Console.WriteLine(item.Describe("text"));
    }
}
