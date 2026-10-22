// User-defined exception hierarchies deriving from System.Exception.
try { Withdraw(100, 250); }
catch (InsufficientFundsException e)
{
    Console.WriteLine(e.Message);
    Console.WriteLine($"short by {e.Shortfall}");
    Console.WriteLine(e is BankException);
}

try { throw new BankException("generic bank failure"); }
catch (InsufficientFundsException) { Console.WriteLine("wrong"); }
catch (BankException e) { Console.WriteLine("BankException: " + e.Message); }

try { throw new ValidationException("name", "must not be empty"); }
catch (Exception e)
{
    Console.WriteLine(e.Message);
}
Console.WriteLine(new ValidationException("age", "must be positive").ToString());

var wrapped = new InvalidOperationException("outer", new FormatException("inner cause"));
Console.WriteLine(wrapped.InnerException!.Message);
Console.WriteLine(wrapped.ToString());
Console.WriteLine(new SilentException().Message);
Console.WriteLine(new Exception().Message);
Console.WriteLine(new InvalidOperationException().Message);
Console.WriteLine(new ArgumentOutOfRangeException("index").Message);
Console.WriteLine(new ArgumentException("bad", "arg").Message);

static void Withdraw(int balance, int amount)
{
    if (amount > balance) throw new InsufficientFundsException(amount - balance);
}

class BankException : Exception
{
    public BankException(string message) : base(message) { }
}

class InsufficientFundsException : BankException
{
    public int Shortfall { get; }
    public InsufficientFundsException(int shortfall) : base("insufficient funds") { Shortfall = shortfall; }
}

class ValidationException(string fieldName, string problem) : Exception(problem)
{
    public override string Message => $"{fieldName}: {base.Message}";
}

class SilentException : Exception { }
