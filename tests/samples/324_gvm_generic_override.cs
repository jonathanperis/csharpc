using System;

// A generic virtual method overridden in a generic class. csharpc dispatches generic virtual
// methods over the program's non-generic classes only, so instead of silently calling the base
// implementation it rejects the call with an explicit diagnostic (324_gvm_generic_override.diagnostics).
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
