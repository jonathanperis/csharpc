// Static initialization timing, as in .NET: a type with a static constructor initializes on the
// first call of a static member or constructor (never if unused); a type with only static field
// initializers (beforefieldinit) on the first static field access. Exceptions become a cached
// TypeInitializationException. Initialization cycles see default values.
using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("start");
Console.WriteLine("calling BFI.M");
BFI.M();
Console.WriteLine("new BFI");
_ = new BFI();
Console.WriteLine("reading BFI.X");
Console.WriteLine(BFI.X);
Console.WriteLine("reading BFI2.X in a helper");
Helper();
Console.WriteLine("Explicit.M");
Explicit.M();
Console.WriteLine("Explicit.M again");
Explicit.M();
Console.WriteLine("Derived.S");
Derived.S();
Console.WriteLine("new Derived");
_ = new Derived();
Console.WriteLine("Const " + Explicit2.C);
Console.WriteLine("typeof " + typeof(Explicit2).Name);
Console.WriteLine("Nested");
Explicit2.Inner.M();
try { Thrower.M(); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message + " / " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message); }
try { Thrower.M(); } catch (TypeInitializationException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message + " / " + e.InnerException?.Message); }
try { _ = BfiThrower.X; } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message + " / " + e.InnerException?.Message); }
Exception? first = null, second = null;
try { _ = new Thrower2(); } catch (Exception e) { first = e; }
try { Thrower2.Value = 3; } catch (Exception e) { second = e; }
Console.WriteLine(ReferenceEquals(first, second));
Console.WriteLine("S struct");
S s = default;
s.Inst();
Console.WriteLine("S.Static");
S.Static();
Console.WriteLine("cycle " + CycA.X + " " + CycB.Y);
Console.WriteLine("property " + WithProperty.Answer);
Console.WriteLine("event");
WithEvent.Changed += () => Console.WriteLine("handler");
WithEvent.Raise();
Console.WriteLine("delegate");
Func<int> f = ViaDelegate.Get;
Console.WriteLine("created");
Console.WriteLine(f());
Console.WriteLine("async");
Console.WriteLine(await AsyncHolder.GetAsync());
Console.WriteLine("constants " + Constants.Table.Length + " " + Constants.Name);
Console.WriteLine("threads");
var threads = new Thread[4];
var results = new int[4];
for (int i = 0; i < threads.Length; i++)
{
    int k = i;
    threads[i] = new Thread(() => results[k] = Slow.Value);
    threads[i].Start();
}
foreach (var t in threads) t.Join();
Console.WriteLine(string.Join(",", results) + " initialized " + Slow.Count + " time(s)");
Console.WriteLine("end");

static void Helper() => Console.WriteLine(BFI2.X);

class BFI { public static int X = Log("BFI init", 1); public static void M() => Console.WriteLine("BFI.M"); public static int Log(string s, int v) { Console.WriteLine(s); return v; } }
class BFI2 { public static int X = BFI.Log("BFI2 init", 2); }
class Unused { static Unused() => Console.WriteLine("Unused cctor (never printed)"); public static void M() { } }
class UnusedBfi { public static int X = BFI.Log("UnusedBfi init (never printed)", 0); }
class Explicit { public static int X = BFI.Log("Explicit field init", 3); static Explicit() => Console.WriteLine("Explicit cctor"); public static void M() => Console.WriteLine("Explicit.M"); }
class Explicit2 { public const int C = 5; static Explicit2() => Console.WriteLine("Explicit2 cctor"); public class Inner { public static void M() => Console.WriteLine("Inner.M"); } }
class Base { static Base() => Console.WriteLine("Base cctor"); public Base() => Console.WriteLine("Base ctor"); }
class Derived : Base { static Derived() => Console.WriteLine("Derived cctor"); public Derived() => Console.WriteLine("Derived ctor"); public static void S() => Console.WriteLine("Derived.S"); }
class Thrower { static Thrower() { Console.WriteLine("Thrower cctor"); throw new InvalidOperationException("bad"); } public static void M() { } }
class Thrower2 { public static int Value; static Thrower2() => throw new FormatException("thrower2"); }
class BfiThrower { public static int X = Fail(); static int Fail() => throw new ArgumentException("nope"); }
struct S { static S() => Console.WriteLine("S cctor"); public void Inst() => Console.WriteLine("S.Inst"); public static void Static() => Console.WriteLine("S.Static"); }
class CycA { public static int X = CycB.Y + 1; static CycA() => Console.WriteLine("CycA cctor"); }
class CycB { public static int Y = CycA.X + 10; static CycB() => Console.WriteLine("CycB cctor"); }
class WithProperty { static WithProperty() => Console.WriteLine("WithProperty cctor"); public static int Answer { get; } = 42; }
class WithEvent { static WithEvent() => Console.WriteLine("WithEvent cctor"); public static event Action? Changed; public static void Raise() => Changed?.Invoke(); }
class ViaDelegate { static ViaDelegate() => Console.WriteLine("ViaDelegate cctor"); public static int Get() => 7; }
class AsyncHolder { static AsyncHolder() => Console.WriteLine("AsyncHolder cctor"); public static async Task<int> GetAsync() { await Task.Yield(); return 99; } }
class Constants { public static readonly int[] Table = { 1, 2, 3 }; public static readonly string Name = "constants"; }
class Slow
{
    public static int Count;
    public static readonly int Value = Compute();
    static int Compute() { Interlocked.Increment(ref Count); Thread.Sleep(50); return 5; }
}
