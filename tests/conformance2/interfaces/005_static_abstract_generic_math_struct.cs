var total = Sum(new[] { new Money(150), new Money(275) });
Console.WriteLine(total);
static T Sum<T>(T[] xs) where T : IAdd<T> { T acc = T.Zero; foreach (var x in xs) acc = acc + x; return acc; }
interface IAdd<T> where T : IAdd<T> { static abstract T Zero { get; } static abstract T operator +(T a, T b); }
readonly struct Money(int cents) : IAdd<Money> {
    public static Money Zero => new(0);
    public static Money operator +(Money a, Money b) => new(a.Cents + b.Cents);
    public int Cents => cents;
    public override string ToString() => $"${cents / 100}.{cents % 100:D2}";
}
