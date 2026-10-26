// Array.CreateInstance of a reference type the program never names as T[]: synthesize the array
// method table at run time (configuration / DI binders).
using System;

class Widget
{
    public string Name = "";
    public Widget(string name) => Name = name;
}

class Program
{
    static void Main()
    {
        Array array = Array.CreateInstance(typeof(Widget), 2);
        array.SetValue(new Widget("a"), 0);
        array.SetValue(new Widget("b"), 1);
        Console.WriteLine(array.GetType().FullName);
        Console.WriteLine(array.Length);
        Console.WriteLine(((Widget)array.GetValue(0)!).Name);
        Console.WriteLine(((Widget)array.GetValue(1)!).Name);
        Type made = typeof(Widget).MakeArrayType();
        Console.WriteLine(made.FullName);
        Console.WriteLine(made == array.GetType());
        try { array.SetValue("not a widget", 0); }
        catch (InvalidCastException e) { Console.WriteLine(e.Message); }
    }
}
