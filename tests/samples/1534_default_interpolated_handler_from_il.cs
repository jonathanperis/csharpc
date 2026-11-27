// DefaultInterpolatedStringHandler comes from CoreLib's IL (#466): string.Create(provider, $"..."), a custom formatter, alignment
// and format strings, null and ISpanFormattable/IFormattable/plain holes, a stack buffer that overflows into ArrayPool, and the
// formatting that libraries compiled from IL do with it (IPAddress, IPEndPoint, Version).
using System;
using System.Globalization;
using System.Net;

class Upper : IFormatProvider, ICustomFormatter
{
    public object? GetFormat(Type? t) => t == typeof(ICustomFormatter) ? this : null;
    public string Format(string? format, object? arg, IFormatProvider? p) =>
        arg is string s ? s.ToUpperInvariant() : arg is IFormattable f ? "<" + f.ToString(format, CultureInfo.InvariantCulture) + ">" : arg?.ToString() ?? "(null)";
}

struct Plain { public override string ToString() => "plain"; }

struct Money : IFormattable
{
    public decimal Amount;
    public string ToString(string? format, IFormatProvider? provider) => "$" + Amount.ToString(format ?? "F2", provider);
}

static class Program
{
    static void Main()
    {
        int n = 42; double d = 3.14159; string? none = null; object? nothing = null; var when = new DateTime(2026, 11, 22, 13, 5, 0);
        var inv = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Create(inv, $"n={n,5} d={d:F2} none=[{none}] obj=[{nothing}] when={when:yyyy-MM-dd HH:mm} hex={n:x4}|{n,-6}|{-n,4:D3}"));
        var comma = (NumberFormatInfo)inv.NumberFormat.Clone();
        comma.NumberDecimalSeparator = ","; comma.NumberGroupSeparator = ".";
        Console.WriteLine(string.Create(comma, $"d={d} big={1234567.891:N2} n={n:N0}"));
        Console.WriteLine(string.Create(new Upper(), $"name={"alice"} n={n} d={d:F1} null={none} plain={new Plain()}"));
        Console.WriteLine(string.Create(inv, $"money={new Money { Amount = 12.5m }} {new Money { Amount = 3m }:F0} plain={new Plain(),8}| guid={new Guid(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)}"));
        Span<char> small = stackalloc char[8];
        Console.WriteLine(string.Create(inv, small, $"longer than the initial buffer: {n} {d} {when:O} {"tail"}"));
        string long1 = new string('x', 600);
        Console.WriteLine(string.Create(inv, $"{long1}|{n}").Length);
        Console.WriteLine(new IPEndPoint(IPAddress.Parse("10.1.2.3"), 8080) + " " + IPAddress.Parse("2001:db8::1") + " " + new IPEndPoint(IPAddress.Parse("::1"), 443));
        Console.WriteLine(new Version(1, 2, 3, 4) + " " + new Version(10, 0));
        FormattableString fs = $"fs {n:D5} {d:E2}";
        Console.WriteLine(fs.ToString(inv) + " | " + FormattableString.Invariant($"inv {d}"));
    }
}
