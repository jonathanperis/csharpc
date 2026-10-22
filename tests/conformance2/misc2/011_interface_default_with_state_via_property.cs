var c = new Counter();
ICount ic = c;
ic.Bump(); ic.Bump();
Console.WriteLine(c.N);
interface ICount { int N { get; set; } void Bump() => N++; }
class Counter : ICount { public int N { get; set; } }
