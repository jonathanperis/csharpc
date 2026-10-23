// Regex.Escape / Regex.Unescape, including Unescape's errors, and escaped patterns round-tripping.
using System;
using System.Text.RegularExpressions;

class Program
{
    static string Visible(string s) => s.Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\f", "\\f");

    static void Main()
    {
        string[] inputs = { "plain", "a.b*c+d?", "[x](y){z}", @"back\slash", "^start$", "a|b", "# hash", "tab\there", "new\nline", "spaces  here", "" };
        foreach (string s in inputs)
        {
            string escaped = Regex.Escape(s);
            Console.WriteLine($"{Visible(s)} -> {Visible(escaped)} -> {Regex.IsMatch(s, "^" + escaped + "$")}");
        }

        Console.WriteLine(Visible(Regex.Unescape(@"\t\n\r\f\e\a\\\.\*")));
        Console.WriteLine(Regex.Unescape(@"\x41\u0042\103\cC").Length);
        Console.WriteLine(Regex.Unescape(@"\x41\u0042\103"));
        Console.WriteLine(Regex.Unescape("no escapes"));

        string[] bad = { @"\", @"\x4", @"\u12", @"\c", @"\p{L}", @"\q" };
        foreach (string s in bad)
        {
            try { Console.WriteLine(Regex.Unescape(s)); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        }
        try { Regex.Escape(null!); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
