// StringBuilder.Append/AppendLine/Insert with interpolated strings (AppendInterpolatedStringHandler),
// inside loops and instance methods, with format and alignment specifiers.
using System;
using System.Collections.Generic;
using System.Text;

class Account
{
    private readonly List<(DateTime Date, decimal Amount, string Note)> _items = new();
    public void Add(DateTime d, decimal a, string n) => _items.Add((d, a, n));

    public string History()
    {
        var report = new StringBuilder();
        decimal balance = 0;
        report.AppendLine("Date\t\tAmount\tBalance\tNote");
        foreach (var item in _items)
        {
            balance += item.Amount;
            report.AppendLine($"{item.Date.ToShortDateString()}\t{item.Amount}\t{balance}\t{item.Note}");
        }
        return report.ToString();
    }
}

class Program
{
    static void Main()
    {
        var acct = new Account();
        acct.Add(new DateTime(2024, 1, 2), 100m, "open");
        acct.Add(new DateTime(2024, 2, 29), -25.5m, "rent");
        Console.Write(acct.History());

        var sb = new StringBuilder();
        int n = 42;
        double pi = 3.14159;
        sb.Append($"n={n,5}|{n,-5}|{n:X4}|{pi:F2}|{pi,8:F3}|");
        sb.Append($"{null}{(object?)null}{'c'}{true}{n > 40}");
        sb.AppendLine();
        sb.AppendLine($"{"left",-8}|{"right",8}|");
        sb.Insert(0, $"[{n * 2}] ");
        string? missing = null;
        sb.Append($"<{missing}>").Append('!');
        for (int i = 0; i < 3; i++) sb.Append($"{i}{(i < 2 ? "," : "")}");
        Console.WriteLine(sb.ToString());
        Console.WriteLine(sb.Length);

        // The handler is also used when the builder is reached through an expression.
        var builders = new[] { new StringBuilder("a"), new StringBuilder("b") };
        foreach (var b in builders) b.Append($"-{b.Length}");
        Console.WriteLine($"{builders[0]} {builders[1]}");
    }
}
