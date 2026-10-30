// Version.Parse / TryParse: components, limits and .NET's exception types and messages.
using System;

class Program
{
    static void Main()
    {
        foreach (var s in new[] { "1.2", "1.2.3", "1.2.3.4", " 1 . 2 ", "+1.2", "1", "1.2.3.4.5", "1.-2", "1.x", "1.2.99999999999", "", "1..2", "2147483647.0" })
        {
            try { Console.WriteLine($"[{s}] -> {Version.Parse(s)}"); }
            catch (Exception e) { Console.WriteLine($"[{s}] !! {e.GetType().Name}: {e.Message}"); }
            Console.WriteLine("  TryParse " + Version.TryParse(s, out var v) + " " + (v?.ToString() ?? "null"));
        }
        Console.WriteLine(Version.Parse("10.0.1".AsSpan()));
        try { Version.Parse((string)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        Console.WriteLine(Version.TryParse((string?)null, out var n) + " " + (n == null));
    }
}
