var c = new Counter();
Action inc = c.Increment;
inc(); inc();
Console.WriteLine(c.N);
class Counter { public int N; public void Increment() => N++; }
