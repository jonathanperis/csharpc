// User-defined operators of a struct lifted to Nullable<T>: the operator runs only when both
// operands have a value; == / != compare null-ness otherwise, relational operators are false,
// and other operators produce null.
using System;

Money? a = new Money(5), b = new Money(7), none = null;
Console.WriteLine($"{a == b} {a != b} {a == none} {none == none} {none != none} {a != null} {none == null}");
Console.WriteLine($"{a < b} {a > b} {a < none} {none >= none}");
Console.WriteLine($"{(a + b)?.Amount} {(a + none)?.Amount ?? -1} {(none + none).HasValue}");
Money? c = a;
c += b;
Console.WriteLine(c?.Amount);
Money plain = new Money(5);
Console.WriteLine($"{plain == a} {plain == none}");
Console.WriteLine(Money.Calls);

struct Money(int amount)
{
    public static int Calls;
    public int Amount => amount;
    public static bool operator ==(Money x, Money y) { Calls++; return x.Amount == y.Amount; }
    public static bool operator !=(Money x, Money y) { Calls++; return x.Amount != y.Amount; }
    public static bool operator <(Money x, Money y) { Calls++; return x.Amount < y.Amount; }
    public static bool operator >(Money x, Money y) { Calls++; return x.Amount > y.Amount; }
    public static bool operator <=(Money x, Money y) { Calls++; return x.Amount <= y.Amount; }
    public static bool operator >=(Money x, Money y) { Calls++; return x.Amount >= y.Amount; }
    public static Money operator +(Money x, Money y) { Calls++; return new Money(x.Amount + y.Amount); }
    public override bool Equals(object? obj) => obj is Money m && m.Amount == Amount;
    public override int GetHashCode() => Amount;
}
