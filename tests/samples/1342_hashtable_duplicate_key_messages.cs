// Hashtable from the framework's IL (#373): the duplicate-key message is SR.Format(Argument_AddingDuplicate__, existing, key), that is
// string.Format with the current culture, for keys of every kind: string, the primitives, an IFormattable, an ISpanFormattable whose
// TryFormat and ToString differ (string.Format takes TryFormat), a type with only ToString, and one whose ToString returns null.
using System;
using System.Collections;
using System.Globalization;

static class Program
{
    static void Duplicate(object key, object again)
    {
        var table = new Hashtable { [key] = 1 };
        try { table.Add(again, 2); Console.WriteLine("added " + again); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message + " | param " + (e.ParamName ?? "null")); }
    }

    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Duplicate("alpha", "alpha");
        Duplicate(42, 42);
        Duplicate(-7L, -7L);
        Duplicate(1.5, 1.5);
        Duplicate(2.5m, 2.5m);
        Duplicate('x', 'x');
        Duplicate(true, true);
        Duplicate(new DateTime(2024, 2, 29, 13, 45, 0), new DateTime(2024, 2, 29, 13, 45, 0));
        Duplicate(Guid.Empty, Guid.Empty);
        Duplicate(DayOfWeek.Friday, DayOfWeek.Friday);
        Duplicate(new Point(3, 4), new Point(3, 4));
        Duplicate(new Span(1), new Span(1));
        Duplicate(new Plain("p"), new Plain("p"));
        Duplicate(new NullText(), new NullText());
        Duplicate("{0}", "{0}");
        Duplicate("{{}}", "{{}}");

        var t = new Hashtable();
        try { t.Add(null!, 1); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        t["k"] = 1;
        t["k"] = 2;
        Console.WriteLine("indexer overwrites: " + t["k"] + ", count " + t.Count);
    }
}

sealed class Point(int x, int y) : IFormattable
{
    public override bool Equals(object? obj) => obj is Point p && p.ToString() == ToString();
    public override int GetHashCode() => x * 31 + y;
    public override string ToString() => "Point.ToString";
    public string ToString(string? format, IFormatProvider? provider) => $"({x}; {y}) in {(provider as CultureInfo)?.Name ?? (provider is null ? "null" : "other")} fmt {format ?? "null"}";
}

sealed class Span(int v) : ISpanFormattable
{
    public override bool Equals(object? obj) => obj is Span s && s.GetHashCode() == GetHashCode();
    public override int GetHashCode() => v;
    public override string ToString() => "Span.ToString";
    public string ToString(string? format, IFormatProvider? provider) => "Span.IFormattable";
    public bool TryFormat(System.Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        const string text = "Span.TryFormat";
        if (destination.Length < text.Length) { charsWritten = 0; return false; }
        text.AsSpan().CopyTo(destination);
        charsWritten = text.Length;
        return true;
    }
}

sealed class Plain(string s)
{
    public override bool Equals(object? obj) => obj is Plain;
    public override int GetHashCode() => 1;
    public override string ToString() => "Plain " + s;
}

sealed class NullText
{
    public override bool Equals(object? obj) => obj is NullText;
    public override int GetHashCode() => 2;
    public override string? ToString() => null;
}
