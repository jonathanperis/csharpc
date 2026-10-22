var m = new Money(5, "EUR");
Console.WriteLine(m.Add(new Money(7, "EUR")));
readonly struct Money(decimal amount, string currency)
{
    public decimal Amount { get; } = amount;
    public string Currency { get; } = currency;
    public Money Add(Money o) => new(Amount + o.Amount, Currency);
    public override string ToString() => $"{Amount} {Currency}";
}
