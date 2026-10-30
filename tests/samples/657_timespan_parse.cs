// TimeSpan.Parse / TryParse / ParseExact / TryParseExact (dotnet/runtime's TimeSpanParse, ported):
// the general forms, fractions, limits, styles and the exact exception types and messages.
using System;
using System.Globalization;

class Program
{
    static void P(string s)
    {
        try { Console.WriteLine($"[{s}] -> {TimeSpan.Parse(s, CultureInfo.InvariantCulture)}"); }
        catch (Exception e) { Console.WriteLine($"[{s}] !! {e.GetType().Name}: {e.Message}"); }
    }

    static void X(string s, string f, TimeSpanStyles st = TimeSpanStyles.None)
    {
        try { Console.WriteLine($"[{s}|{f}|{st}] -> {TimeSpan.ParseExact(s, f, CultureInfo.InvariantCulture, st)}"); }
        catch (Exception e) { Console.WriteLine($"[{s}|{f}|{st}] !! {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        foreach (var s in new[] { "1", "12:30", "1:2:3", "01:02:03.5", "1.02:03:04", "1.02:03:04.1234567", "-3:00",
                                  "  5:06 ", "10675199.02:48:05.4775807", "10675199.02:48:05.4775808", "24:00", "0:60",
                                  "1:2:3:4", "1:2:3:4.5", "abc", "", "1.2.3", "99999999", ":1", "1:", "00:00:00.00000001" })
            P(s);

        Console.WriteLine(TimeSpan.TryParse("3:04", out var t1) + " " + t1);
        Console.WriteLine(TimeSpan.TryParse("bad", out var t2) + " " + t2);
        Console.WriteLine(TimeSpan.TryParse("1:02".AsSpan(), CultureInfo.InvariantCulture, out var t3) + " " + t3);
        Console.WriteLine(TimeSpan.Parse("6:07".AsSpan()));

        X("01:02:03", "c");
        X("1.02:03:04.5", "c");
        X("1:2:3", "g");
        X("1:02:03:04.0000000", "G");
        X("12-34", @"hh\-mm");
        X("5", "%d");
        X("12:34", @"hh\:mm", TimeSpanStyles.AssumeNegative);
        X("1:2", "c");
        X("12:34", "hh:mm");
        X("12", "");
        Console.WriteLine(TimeSpan.ParseExact("3:04", new[] { "c", @"h\:mm" }, CultureInfo.InvariantCulture));
        Console.WriteLine(TimeSpan.TryParseExact("x", "c", CultureInfo.InvariantCulture, out var t4) + " " + t4);

        try { TimeSpan.Parse((string)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        try { TimeSpan.ParseExact("1", (string)null!, null); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        try { TimeSpan.ParseExact("1", "c", null, (TimeSpanStyles)7); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
    }
}
