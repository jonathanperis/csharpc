// Globalization-invariant mode (367_culture_invariant_mode.env: DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
// with a Brazilian locale): the invariant culture everywhere, ordinal comparisons, invariant casing, and
// only the invariant culture can be created (PredefinedCulturesOnly defaults to true).
using System;
using System.Globalization;
using System.Linq;

Console.WriteLine("[" + CultureInfo.CurrentCulture.Name + "] [" + CultureInfo.CurrentUICulture.Name + "]");
Console.WriteLine(1234.5.ToString("N2") + " " + (-3.5).ToString() + " " + double.PositiveInfinity + " " + 0.5.ToString("C") + " " + new DateTime(2026, 1, 2, 3, 4, 5));
string[] w = { "b", "B", "a", "A", "é", "e", "é", "é", "_x", "-x", "ä", "z" };
Console.WriteLine(string.Join(",", w.OrderBy(x => x)));
Console.WriteLine(string.Compare("a", "B") + " " + string.Compare("é", "é") + " " + "abc­def".IndexOf("cd") + " " + "x".CompareTo("X"));
Console.WriteLine("straße".ToUpper() + " " + "ǅ".ToLower() + " " + "İ".ToLower() + " " + char.ToUpper('ı') + " " + string.Equals("i", "I", StringComparison.CurrentCultureIgnoreCase));
foreach (string name in new[] { "pt-BR", "en-US", "", "xx-YY" })
{
    try
    {
        var c = new CultureInfo(name);
        Console.WriteLine("created [" + c.Name + "] " + 1.5.ToString(c));
    }
    catch (CultureNotFoundException e)
    {
        Console.WriteLine("CultureNotFoundException: " + e.Message.Replace("\r", "").Replace("\n", " / ") + " [" + e.InvalidCultureName + "] " + e.ParamName);
    }
}
Console.WriteLine(CultureInfo.InvariantCulture.CompareInfo.Compare("a", "A", CompareOptions.IgnoreCase) + " " + CultureInfo.InvariantCulture.EnglishName);
