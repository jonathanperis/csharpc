// new Random(seed) reproduces .NET's seeded sequences exactly (the legacy subtractive generator .NET keeps for
// seeded instances), including derived classes that override Sample and the argument errors.
var r = new Random(42);
Console.WriteLine(string.Join(",", Enumerable.Range(0, 5).Select(_ => r.Next())));
Console.WriteLine(string.Join(",", Enumerable.Range(0, 5).Select(_ => r.Next(100))));
Console.WriteLine(string.Join(",", Enumerable.Range(0, 3).Select(_ => r.Next(-1000, 1000))));
Console.WriteLine(r.Next(int.MinValue, int.MaxValue));
Console.WriteLine(r.NextDouble().ToString("R"));
var bytes = new byte[8]; r.NextBytes(bytes); Console.WriteLine(string.Join(",", bytes));
Console.WriteLine(r.NextInt64() + " " + r.NextInt64(1000) + " " + r.NextInt64(-5, 5));
Console.WriteLine(r.NextSingle().ToString("R"));
var arr = Enumerable.Range(1, 10).ToArray(); new Random(7).Shuffle(arr); Console.WriteLine(string.Join(",", arr));
Console.WriteLine(string.Join(",", new Random(3).GetItems(new[] { 'a', 'b', 'c' }, 6)));
var neg = new Random(-17); Console.WriteLine(neg.Next() + " " + new Random(int.MinValue).Next());
var d = new MyRandom(5); Console.WriteLine(d.Next(10) + " " + d.Next() + " " + d.NextDouble());
try { r.Next(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { r.Next(5, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
Console.WriteLine(Random.Shared.Next(1, 2));
int x = new Random().Next(10); Console.WriteLine(x >= 0 && x < 10);
class MyRandom : Random { public MyRandom(int s) : base(s) { } protected override double Sample() => 0.5; }
