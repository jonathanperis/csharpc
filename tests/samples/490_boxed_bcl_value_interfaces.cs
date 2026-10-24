// Boxed decimal, DateTime and TimeSpan implement IFormattable, IComparable, IComparable<T> and
// IEquatable<T> as in .NET (type tests were silently false before, casts threw).
using System;
using System.Collections.Generic;
using System.Globalization;

class Program
{
    static string Describe(object o) =>
        $"{o.GetType().Name}: IFormattable={o is IFormattable} IComparable={o is IComparable} " +
        $"IConvertible={o is IConvertible} ISpanFormattable={o is ISpanFormattable}";

    static string Fmt(object o, string? format) => o is IFormattable f ? f.ToString(format, CultureInfo.InvariantCulture) : "(not formattable)";

    static int Compare<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);
    static bool Same<T>(T a, T b) where T : IEquatable<T> => a.Equals(b);

    static void Main()
    {
        object[] values = [12.5m, -0.125m, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc), TimeSpan.FromMinutes(90.5), 7];
        foreach (var v in values)
        {
            Console.WriteLine(Describe(v));
            Console.WriteLine($"  null: {Fmt(v, null)} | empty: {Fmt(v, "")}");
        }
        Console.WriteLine(Fmt(1234.5678m, "N2") + " " + Fmt(1234.5678m, "F1") + " " + Fmt(0.5m, "P0") + " " + Fmt(42m, "E3"));
        Console.WriteLine(Fmt(new DateTime(2024, 5, 6), "yyyy-MM-dd") + " " + Fmt(new DateTime(2024, 5, 6, 13, 4, 5), "s"));
        Console.WriteLine(Fmt(TimeSpan.FromSeconds(3725), "c") + " " + Fmt(TimeSpan.FromSeconds(3725), @"hh\:mm"));

        IComparable c = 2.50m;
        Console.WriteLine($"{c.CompareTo(2.5m)} {c.CompareTo(3m)} {c.CompareTo(-1m)} {c.CompareTo(null)}");
        IComparable d = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
        Console.WriteLine($"{d.CompareTo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))} {d.CompareTo(DateTime.MaxValue)}");
        IComparable t = TimeSpan.FromHours(1);
        Console.WriteLine($"{t.CompareTo(TimeSpan.FromMinutes(60))} {t.CompareTo(TimeSpan.Zero)}");
        try { c.CompareTo(1); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { d.CompareTo("x"); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

        Console.WriteLine($"{Compare(1.0m, 1.00m)} {Compare(1m, 2m)} {Same(1.0m, 1.00m)} {Same(1m, 2m)}");
        Console.WriteLine($"{Compare(DateTime.MinValue, DateTime.MaxValue)} {Same(TimeSpan.FromDays(1), TimeSpan.FromHours(24))}");

        IEquatable<decimal> e1 = 3.0m;
        IComparable<DateTime> e2 = new DateTime(2000, 1, 1);
        Console.WriteLine($"{e1.Equals(3m)} {e2.CompareTo(new DateTime(1999, 12, 31))}");

        var list = new List<object> { 3.5m, 1m, 2.25m };
        list.Sort((a, b) => ((IComparable)a).CompareTo(b));
        Console.WriteLine(string.Join(", ", list));
        var formattables = new List<IFormattable> { 1.5m, new DateTime(2001, 2, 3), TimeSpan.FromMilliseconds(1500), 3.25 };
        foreach (var f in formattables) Console.WriteLine(f.ToString("G", CultureInfo.InvariantCulture));
    }
}
