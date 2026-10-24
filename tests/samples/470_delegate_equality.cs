// Delegate equality (==, !=, Equals, Delegate.Equals): same delegate type, same targets and methods
// in the same invocation list — not reference equality.
var holder = new Holder(1);
var other = new Holder(1);
Action a = M;
Action b = M;
Action c = () => { };
Action instance1 = holder.Run, instance2 = holder.Run, instance3 = other.Run;
Func<string, bool> isEmpty1 = string.IsNullOrEmpty, isEmpty2 = string.IsNullOrEmpty;
Console.WriteLine($"{a == b} {a != b} {a == c} {a.Equals(b)} {a.Equals(c)} {a.Equals(null)} {a.Equals((object)"M")}");
Console.WriteLine($"{(a + c) == (b + c)} {(a + c) == (c + a)} {(a + c + a) - a == a + c} {a == null} {null == a}");
Console.WriteLine($"{instance1 == instance2} {instance1 == instance3} {isEmpty1 == isEmpty2} {Delegate.Equals(a, b)} {object.Equals(a, b)}");
Action? none = null;
Console.WriteLine($"{none == null} {none == a} {ReferenceEquals(a, b)}");
EventHandler e1 = (s, e) => { };
Console.WriteLine(e1 == e1);
Console.WriteLine(true.ToString(System.Globalization.CultureInfo.InvariantCulture));
// Static method groups are cached per converting type (C# 11): the same object there, an equal one elsewhere.
Func<int, int> twice1 = Helpers.Twice, twice2 = Helpers.Twice;
Action viaNew = new Action(M);
Console.WriteLine($"{ReferenceEquals(twice1, twice2)} {ReferenceEquals(twice1, Helpers.Get())} {twice1 == Helpers.Get()} {ReferenceEquals(Helpers.Get(), Helpers.Get())} {ReferenceEquals(a, viaNew)} {a == viaNew}");
Func<string, bool> isEmpty3 = string.IsNullOrEmpty;
Console.WriteLine($"{ReferenceEquals(isEmpty1, isEmpty3)} {isEmpty1 == isEmpty3}");

static void M() { }

static class Helpers
{
    public static int Twice(int x) => 2 * x;
    public static Func<int, int> Get() => Twice;
}

sealed class Holder(int id)
{
    public int Id => id;
    public void Run() { }
}
