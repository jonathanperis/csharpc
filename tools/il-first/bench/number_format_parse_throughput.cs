// Number formatting and parsing throughput, the program behind docs/il-first/System.Globalization.md (#426): int, long, double and decimal
// through ToString / TryFormat / Parse / TryParse, under the invariant culture and a named one (de-DE: ',' decimal separator, '.' groups),
// standard and custom formats, and composite formatting (string.Format and interpolation). The checksum depends on the fixed inputs only.
using System;
using System.Globalization;

class Program
{
    static void Main()
    {
        long check = 0;
        var inv = CultureInfo.InvariantCulture;
        var de = CultureInfo.GetCultureInfo("de-DE");
        Span<char> buf = stackalloc char[64];
        string[] intFormats = ["D", "N0", "X8", "0000"];
        string[] doubleFormats = ["R", "F3", "E5", "N2", "G"];
        for (int i = 0; i < 300_000; i++)
        {
            int n = i * 7919 - 1_000_000;
            long l = (long)n * 1_000_003L;
            double d = n / 7.0;
            decimal m = n / 100m;

            // int and long: ToString, TryFormat, Parse
            string s = n.ToString();
            check += int.Parse(s) == n ? s.Length : -1000;
            foreach (var f in intFormats)
            {
                if (n.TryFormat(buf, out int w, f, inv)) check += w;
            }
            s = l.ToString("N0", de);
            check += long.Parse(s, NumberStyles.Number, de) == l ? s.Length : -1000;
            if (long.TryParse(l.ToString(inv), NumberStyles.Integer, inv, out var lp)) check += lp & 7;

            // double: round-trip and standard formats, both cultures
            s = d.ToString("R", inv);
            check += double.Parse(s, inv) == d ? s.Length : -1000;
            foreach (var f in doubleFormats)
            {
                if (d.TryFormat(buf, out int w, f, (i & 1) == 0 ? inv : de)) check += w;
            }
            s = d.ToString("N2", de);
            if (double.TryParse(s, NumberStyles.Number, de, out var dp)) check += (long)dp & 7;

            // decimal: format, parse, both cultures
            s = m.ToString(inv);
            check += decimal.Parse(s, inv) == m ? s.Length : -1000;
            s = m.ToString("C", de);
            check += s.Length;
            if (decimal.TryParse(m.ToString("F2", de), NumberStyles.Number, de, out var mp)) check += (long)mp & 7;

            // composite formatting
            if ((i & 7) == 0)
            {
                s = string.Format(inv, "{0,8} {1:F2} {2:N0} {3:X}", n, d, l, i);
                check += s.Length;
                s = string.Format(de, "{0:C} | {1:P1}", m, d / 1e6);
                check += s.Length;
                s = $"{n}:{d:F1}:{m}";
                check += s.Length;
            }
        }
        Console.WriteLine(check);
    }
}
