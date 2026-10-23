using Ardalis.GuardClauses;

// Ardalis.GuardClauses (nuget.org) compiled from its IL: generic guards over IComparable/INumber-like
// constraints, CallerArgumentExpression parameter names, exceptions with .NET's messages.
Console.WriteLine(Guard.Against.NullOrEmpty("abc", "name"));
Console.WriteLine(Guard.Against.OutOfRange(5, "n", 1, 10));
Console.WriteLine(Guard.Against.Negative(3, "x") + Guard.Against.NegativeOrZero(2.5, "price"));
Console.WriteLine(Guard.Against.Zero(7L, "count"));
Console.WriteLine(Guard.Against.NullOrWhiteSpace(" padded ", "text"));

Check(() => Guard.Against.Null((string?)null, "input"));
Check(() => Guard.Against.NullOrEmpty("", "name"));
Check(() => Guard.Against.OutOfRange(42, "age", 0, 10));
Check(() => Guard.Against.Negative(-1, "x"));
Check(() => Guard.Against.Zero(0, "divisor"));
Check(() => Guard.Against.NullOrWhiteSpace("   ", "text"));
Check(() => Guard.Against.InvalidInput(5, "even", n => n % 2 == 0));
Check(() => Guard.Against.Expression(n => n > 3, 4, "four is too big"));

int value = -7;
Check(() => Guard.Against.Negative(value));

static void Check(Action guard)
{
    try
    {
        guard();
        Console.WriteLine("no exception");
    }
    catch (ArgumentException e)
    {
        Console.WriteLine($"{e.GetType().Name}: {e.Message}");
    }
}
