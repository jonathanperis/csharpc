var next = MakeCounter();
Console.WriteLine(next() + " " + next() + " " + next());
static Func<int> MakeCounter() { int n = 0; return () => ++n; }
