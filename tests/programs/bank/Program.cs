// A small banking domain: accounts, transactions, interest, fees, LINQ reports, decimal money.
var bank = new Bank("First Csharpc Bank");
var start = new DateTime(2026, 1, 1);

var alice = bank.Open(new CheckingAccount("Alice", start, overdraftLimit: 200m));
var bob = bank.Open(new SavingsAccount("Bob", start, annualRate: 0.035m));
var carol = bank.Open(new CheckingAccount("Carol", start.AddDays(10), overdraftLimit: 0m));
var dave = bank.Open(new SavingsAccount("Dave", start.AddDays(3), annualRate: 0.05m));

var rng = new Lcg(42);
var day = start;
for (int i = 0; i < 120; i++)
{
    day = day.AddDays(rng.Next(0, 3));
    var accounts = bank.Accounts;
    var from = accounts[rng.Next(0, accounts.Count)];
    var amount = Math.Round(rng.Next(1, 50000) / 100m, 2);
    try
    {
        switch (rng.Next(0, 4))
        {
            case 0: from.Deposit(amount, day, "salary"); break;
            case 1: from.Withdraw(amount / 4, day, "atm"); break;
            case 2:
                var to = accounts[rng.Next(0, accounts.Count)];
                if (to != from) bank.Transfer(from, to, amount / 2, day);
                break;
            default: from.Deposit(Math.Round(amount / 10, 2), day, "refund"); break;
        }
    }
    catch (InsufficientFundsException e)
    {
        bank.Log($"{day:yyyy-MM-dd} declined: {e.Message}");
    }
}

foreach (var acct in bank.Accounts) acct.MonthEnd(new DateTime(2026, 4, 30));

Console.WriteLine($"=== {bank.Name} ===");
Console.WriteLine($"{"Owner",-8}{"Type",-10}{"Opened",-12}{"Balance",12}{"Txns",6}");
foreach (var a in bank.Accounts.OrderByDescending(a => a.Balance))
    Console.WriteLine($"{a.Owner,-8}{a.Kind,-10}{a.Opened:yyyy-MM-dd}  {a.Balance,12:N2}{a.Transactions.Count,6}");
Console.WriteLine($"{"Total",-30}{bank.Accounts.Sum(a => a.Balance),12:N2}");

Console.WriteLine();
Console.WriteLine("Transactions by category:");
var byCategory = bank.Accounts.SelectMany(a => a.Transactions)
    .GroupBy(t => t.Category)
    .Select(g => new { Category = g.Key, Count = g.Count(), Total = g.Sum(t => t.Amount) })
    .OrderBy(x => x.Category);
foreach (var c in byCategory) Console.WriteLine($"  {c.Category,-10} {c.Count,4} {c.Total,12:N2}");

Console.WriteLine();
Console.WriteLine("Monthly net flow:");
foreach (var m in bank.Accounts.SelectMany(a => a.Transactions).GroupBy(t => (t.Date.Year, t.Date.Month)).OrderBy(g => g.Key))
    Console.WriteLine($"  {m.Key.Year}-{m.Key.Month:D2}: {m.Sum(t => t.Amount),10:N2} over {m.Count()} txns, largest {m.Max(t => Math.Abs(t.Amount)):N2}");

var biggest = bank.Accounts.SelectMany(a => a.Transactions.Select(t => (a.Owner, t))).MaxBy(x => x.t.Amount);
Console.WriteLine($"\nLargest credit: {biggest.Owner} {biggest.t.Amount:C} on {biggest.t.Date:dddd, MMM d}");
Console.WriteLine($"Declined operations: {bank.Journal.Count(l => l.Contains("declined"))}");
foreach (var line in bank.Journal.Take(3)) Console.WriteLine("  " + line);
var span = bank.Accounts.Max(a => a.Transactions.LastOrDefault()?.Date ?? a.Opened) - start;
Console.WriteLine($"Activity span: {span.TotalDays} days ({span})");

// ------------------------------------------------------------------ domain
record Transaction(DateTime Date, decimal Amount, string Category, decimal BalanceAfter);

class InsufficientFundsException(string owner, decimal requested, decimal available)
    : Exception($"{owner} requested {requested:N2} but only {available:N2} is available");

interface IInterestBearing { decimal AccrueInterest(DateTime asOf); }

abstract class Account(string owner, DateTime opened)
{
    private readonly List<Transaction> _transactions = [];
    public string Owner { get; } = owner;
    public DateTime Opened { get; } = opened;
    public decimal Balance { get; private set; }
    public IReadOnlyList<Transaction> Transactions => _transactions;
    public abstract string Kind { get; }
    protected virtual decimal Available => Balance;

    public void Deposit(decimal amount, DateTime when, string category)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "deposit must be positive");
        Post(amount, when, category);
    }

    public void Withdraw(decimal amount, DateTime when, string category)
    {
        if (amount > Available) throw new InsufficientFundsException(Owner, amount, Available);
        Post(-amount, when, category);
    }

    protected void Post(decimal amount, DateTime when, string category)
    {
        Balance += amount;
        _transactions.Add(new Transaction(when, amount, category, Balance));
    }

    public virtual void MonthEnd(DateTime when) { }
    public override string ToString() => $"{Kind}({Owner}, {Balance:N2})";
}

class CheckingAccount(string owner, DateTime opened, decimal overdraftLimit) : Account(owner, opened)
{
    public override string Kind => "Checking";
    protected override decimal Available => Balance + overdraftLimit;

    public override void MonthEnd(DateTime when)
    {
        if (Balance < 0) Post(-Math.Round(-Balance * 0.02m + 5m, 2), when, "fee");
    }
}

class SavingsAccount(string owner, DateTime opened, decimal annualRate) : Account(owner, opened), IInterestBearing
{
    public override string Kind => "Savings";

    public decimal AccrueInterest(DateTime asOf)
    {
        var days = (asOf - Opened).Days;
        return Math.Round(Balance * annualRate * days / 365m, 2, MidpointRounding.ToEven);
    }

    public override void MonthEnd(DateTime when)
    {
        var interest = AccrueInterest(when);
        if (interest > 0) Post(interest, when, "interest");
    }
}

class Bank(string name)
{
    private readonly List<Account> _accounts = [];
    public string Name { get; } = name;
    public List<string> Journal { get; } = [];
    public IReadOnlyList<Account> Accounts => _accounts;

    public T Open<T>(T account) where T : Account
    {
        _accounts.Add(account);
        Log($"{account.Opened:yyyy-MM-dd} opened {account}");
        return account;
    }

    public void Transfer(Account from, Account to, decimal amount, DateTime when)
    {
        from.Withdraw(amount, when, "transfer");
        to.Deposit(amount, when, "transfer");
    }

    public void Log(string line) => Journal.Add(line);
}

// Deterministic pseudo-random numbers (System.Random's algorithm differs across runtimes).
class Lcg(uint seed)
{
    private uint _state = seed;
    public int Next(int min, int max)
    {
        _state = _state * 1664525u + 1013904223u;
        return min + (int)((_state >> 8) % (uint)(max - min));
    }
}
