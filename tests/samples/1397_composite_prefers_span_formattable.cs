// Composite formatting and string interpolation prefer ISpanFormattable.TryFormat over IFormattable.ToString, as .NET's string.Format,
// StringBuilder.AppendFormat, FormattableString and the interpolation handlers do; a type may answer the two differently. So do the
// collections' key messages (SR.Format of the key). Built-in numbers answer both alike. string.Format offers an ISpanFormattable item
// the space left in its buffer, once, and falls back to IFormattable when it does not fit or when the item is right-aligned.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

static class Program
{
    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var t = new Twice(7);
        object boxed = t;
        Console.WriteLine(string.Format("{0}|{0:X}|{0,12}|{0,-12}|", t));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} and {1}", boxed, 3.5));
        Console.WriteLine($"{t}|{t:F}|{boxed}|{boxed:G}|{t,10}");
        Console.WriteLine(new StringBuilder().AppendFormat("{0}/{1}", t, 42).ToString());
        FormattableString fs = $"fs {t:P}";
        Console.WriteLine(fs.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(string.Format("{0}", new Long(3)));
        Console.WriteLine(string.Format("{0}", new OnlyFormattable()));
        // The space string.Format leaves for the item depends on its buffer: 256 chars, grown at once to the format's length plus 8 per
        // argument, then as an ArrayPool<char> array (powers of two).
        foreach (int literal in new[] { 0, 200, 300, 600, 1500 })
        {
            string text = string.Format(new string('.', literal) + "{0}", new Long(1));
            Console.WriteLine(literal + ": " + text.Length + " " + text.Substring(literal, Math.Min(12, text.Length - literal)));
            text = string.Format(new string('.', literal) + "{0}{1}", new Long(1), new Long(2));
            Console.WriteLine(literal + ": " + text.Length);
        }
        var dict = new Dictionary<Twice, int>();
        try { _ = dict[t]; } catch (KeyNotFoundException e) { Console.WriteLine(e.Message); }
        dict[t] = 1;
        try { dict.Add(t, 2); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        var objects = new Dictionary<object, int> { [boxed] = 1 };
        try { objects.Add(boxed, 2); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
    }
}

readonly struct Twice(int value) : ISpanFormattable
{
    public override string ToString() => "ToString(" + value + ")";
    public string ToString(string? format, IFormatProvider? provider) => "IFormattable(" + value + "," + (format ?? "null") + ")";
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        string text = "TryFormat(" + value + "," + (format.IsEmpty ? "empty" : format.ToString()) + ")";
        if (!text.AsSpan().TryCopyTo(destination)) { charsWritten = 0; return false; }
        charsWritten = text.Length;
        return true;
    }
}

// TryFormat fails until the buffer is large: the caller grows it and asks again.
sealed class Long(int repeat) : ISpanFormattable
{
    public string ToString(string? format, IFormatProvider? provider) => "IFormattable";
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        string text = new string('L', 300 * repeat);
        if (!text.AsSpan().TryCopyTo(destination)) { charsWritten = 0; return false; }
        charsWritten = text.Length;
        return true;
    }
}

sealed class OnlyFormattable : IFormattable
{
    public string ToString(string? format, IFormatProvider? provider) => "only IFormattable";
}
