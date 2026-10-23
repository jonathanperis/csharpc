// Static initialization of generic types, per instantiation as in the CLR: separate statics for
// each instantiation, lazy timing (initializers on first static access, an explicit static
// constructor before first use), nested generic types, and a failing initializer's cached
// TypeInitializationException (named after the generic definition) that leaves other instantiations alone.
Console.WriteLine("start");
Console.WriteLine(Holder<int>.Name + " | " + Holder<string>.Name + " | " + Holder<int>.Count);
Holder<int>.Count++;
Console.WriteLine(Holder<int>.Count + " " + Holder<string>.Count);
Console.WriteLine("before cctor type");
Console.WriteLine(WithCctor<double>.Value);
Console.WriteLine(WithCctor<double>.Value + " (cctor ran once)");
Outer<char>.Inner<bool>.Show();
Console.WriteLine(Cache<string>.Items.Count + " " + Cache<int>.Items.Count);
Cache<string>.Items.Add("a");
Console.WriteLine(Cache<string>.Items.Count + " " + Cache<int>.Items.Count);
for (int i = 0; i < 2; i++)
{
    try { Console.WriteLine(Broken<int>.Value); }
    catch (TypeInitializationException e) { Console.WriteLine(e.Message + " <- " + e.InnerException!.Message); }
}
Console.WriteLine(Broken<string>.Ok);

class Holder<T>
{
    public static string Name = "holder of " + typeof(T).Name;
    public static int Count = Init();
    static int Init() { Console.WriteLine("init Holder<" + typeof(T).Name + ">"); return 10; }
}
class WithCctor<T>
{
    public static string Value;
    static WithCctor() { Console.WriteLine("cctor WithCctor<" + typeof(T).Name + ">"); Value = "value of " + typeof(T).Name; }
}
class Outer<A> { public class Inner<B> { static readonly string s = typeof(A).Name + "/" + typeof(B).Name; public static void Show() => Console.WriteLine(s); } }
static class Cache<T> { public static readonly List<T> Items = new(); }
class Broken<T>
{
    public static int Value = Fail();
    public static string Ok => "other instantiation still fine";
    static int Fail() => typeof(T) == typeof(int) ? throw new InvalidOperationException("boom") : 1;
}
