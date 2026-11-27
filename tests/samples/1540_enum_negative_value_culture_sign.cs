// An enum value with no name formats as its number, and a negative number uses the current culture's NegativeSign (#182):
// sv-SE writes U+2212. Names, X, and unsigned or non-negative values are unaffected.
using System;
using System.Globalization;

enum Color { Red, Green, Minus = -1 }
[Flags] enum Bits { A = 1, B = 2 }
enum Small : sbyte { X = 1 }
enum Wide : long { Y = 1 }
enum Unsigned : uint { Z = 1 }

static class Program
{
    static void Show(string label, string text) => Console.WriteLine($"{label}: {text} [{string.Join(" ", Array.ConvertAll(text.ToCharArray(), c => ((int)c).ToString("X4")))}]");

    static void Main()
    {
        foreach (var name in new[] { "sv-SE", "en-US" })
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            Console.WriteLine(name);
            Show("DayOfWeek", ((DayOfWeek)(-3)).ToString());
            Show("G", ((Color)(-3)).ToString());
            Show("named negative", Color.Minus.ToString());
            Show("D", ((Color)(-3)).ToString("D"));
            Show("D named", Color.Minus.ToString("D"));
            Show("X", ((Color)(-3)).ToString("X"));
            Show("interpolated", $"{(Color)(-4)}");
            Show("flags", ((Bits)(-8)).ToString());
            Show("flags F", ((Bits)(-8)).ToString("F"));
            Show("sbyte", ((Small)(-5)).ToString());
            Show("long", ((Wide)(-9)).ToString());
            Show("uint", ((Unsigned)7).ToString());
            Show("positive", ((Color)12).ToString());
            object boxed = (Color)(-6);
            Show("boxed", boxed.ToString()!);
            Show("Enum.Format", Enum.Format(typeof(Color), (Color)(-7), "G"));
        }
    }
}
