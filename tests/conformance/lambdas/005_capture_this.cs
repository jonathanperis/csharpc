var c = new C(5);
Console.WriteLine(c.Adder()(10));
class C(int baseValue) { public Func<int, int> Adder() => x => x + baseValue; }
