Money? a = new Money(5), n = null;
Console.WriteLine((a + new Money(3))?.V);
Console.WriteLine(n == null);
Console.WriteLine(a != n);
class Money(int v) { public int V => v; public static Money operator +(Money x, Money y) => new(x.V + y.V); }
