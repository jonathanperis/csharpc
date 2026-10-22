var c = new Counter(5);
c.Inc();
Console.WriteLine(c.Value);
class Counter(int start) { int n; public void Inc() => n++; public int Value => start + n; }
