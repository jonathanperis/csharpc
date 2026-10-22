var h = new Holder();
h.Mutable.Inc(); h.Mutable.Inc();
h.Frozen.Inc(); h.Frozen.Inc();
Console.WriteLine($"{h.Mutable.N} {h.Frozen.N}");
class Holder { public Counter Mutable; public readonly Counter Frozen; }
struct Counter { public int N; public void Inc() => N++; }
