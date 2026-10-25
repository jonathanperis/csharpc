using System;

// A generic virtual method overridden in a generic class: the call dispatches to the receiver's
// instantiation (csharpc once rejected it with a diagnostic rather than calling the base implementation).
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
        Base[] items = { new Base(), new Plain(), new Wrapper<int>() };
        foreach (Base item in items) Console.WriteLine(item.Describe(42));
    }
}
