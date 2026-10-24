// Stack-trace frames of compiler-generated methods carry the names Roslyn gives them: top-level
// statements (<Main>$), lambdas in the <>c cache class or in closure classes (<>c__DisplayClass),
// local functions (static, capturing through a struct, or living in a closure class), nested
// lambdas, lambdas in constructors and field initializers, and generic methods' closures.
using System;
using System.Collections.Generic;
using System.Text;

Show("lambda", () => Fail("lambda"));
int captured = 3;
Show("capturing lambda", () => Fail("captured " + captured));
Show("static local function", StaticLocal);
Show("local function in closure class", () => LocalUsingCapture(1));
Show("struct closure", () => { int k = 1; void Bump() { k++; Fail("bump " + k); } Bump(); });
Show("nested lambdas", () =>
{
    Action outer = () =>
    {
        Func<int> inner = () => Fail("inner");
        inner();
    };
    outer();
});
Show("lambda in constructor", () => new Widget(true));
Show("field initializer", () => new Gadget());
Show("generic closure", () => Generic.Run(5, "five"));
Show("lambda in loop", () =>
{
    for (int i = 0; i < 2; i++)
    {
        int copy = i;
        Func<int> f = () => copy == 1 ? Fail("loop " + copy) : copy;
        f();
    }
});
Show("local function calling local function", () => Outer(2));

static void StaticLocal() => Fail("static local");
void LocalUsingCapture(int extra) => Fail("local " + (captured + extra));
static int Outer(int n)
{
    return Inner(n + 1);
    static int Inner(int m) => Fail("inner local " + m);
}

static int Fail(string message) => throw new InvalidOperationException(message);

static void Show(string name, Action action)
{
    Console.WriteLine("== " + name);
    try
    {
        action();
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
        Console.Write(Clean(e.StackTrace));
    }
}

/// <summary>"   at M() in /some/dir/file.cs:line 3" -> "   at M() in file.cs:line 3", one line each.</summary>
static string Clean(string? text)
{
    if (text == null) return "<null>" + Environment.NewLine;
    var sb = new StringBuilder();
    foreach (var line in text.Split(Environment.NewLine))
    {
        var at = line.IndexOf(" in ", StringComparison.Ordinal);
        var cut = Math.Max(line.LastIndexOf('/'), line.LastIndexOf('\\'));
        sb.AppendLine(at >= 0 && cut > at ? line.Substring(0, at + 4) + line.Substring(cut + 1) : line);
    }
    return sb.ToString();
}

class Widget
{
    private readonly Func<int> _check;

    public Widget(bool fail)
    {
        _check = () => fail ? Throw("widget") : 0;
        _check();
    }

    static int Throw(string what) => throw new ArgumentException(what);
}

class Gadget
{
    private readonly List<int> _values = Build(() => throw new NotSupportedException("initializer"));

    static List<int> Build(Func<int> make) => new List<int> { make() };
}

static class Generic
{
    public static void Run<T>(int count, T value)
    {
        Func<T> echo = () => count > 3 ? throw new ArgumentOutOfRangeException(nameof(count), value?.ToString()) : value;
        echo();
    }
}
