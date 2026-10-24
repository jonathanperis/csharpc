// Type tests against generic delegate types that variance makes compatible with other instantiations.
using System;

object strings = new Func<string>(() => "text");
object ints = new Func<int>(() => 42);
object action = new Action<object>(o => Console.WriteLine($"action {o}"));
object none = null;

Console.WriteLine(strings is Func<object>);
Console.WriteLine(strings is Func<string>);
Console.WriteLine(ints is Func<object>);
Console.WriteLine(action is Action<string>);
Console.WriteLine(none is Func<object>);
Console.WriteLine((strings as Func<object>)?.Invoke());
Console.WriteLine((ints as Func<object>) is null);
Console.WriteLine(((Func<object>)strings)());
((Action<string>)action)("contravariant");
Console.WriteLine((Func<object>)none is null);
Converter<string, object> converter = s => s.Length;
object boxed = converter;
Console.WriteLine(boxed is Converter<string, object>);
Console.WriteLine(Describe(strings) + " " + Describe(ints) + " " + Describe(action));

try
{
    var f = (Func<object>)ints;
    Console.WriteLine(f());
}
catch (InvalidCastException e)
{
    Console.WriteLine(e.Message);
}

static string Describe(object o) => o switch
{
    Func<object> f => "func:" + f(),
    Action<string> a => "action",
    _ => "other",
};
