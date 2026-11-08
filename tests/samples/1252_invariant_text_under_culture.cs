// Text that .NET defines as culture-invariant stays invariant when the current culture has other number symbols:
// Half and BigInteger formatted with the invariant culture or an explicit provider (they used the current
// culture's negative sign), BigInteger through string.Format / Convert.ToString / FormattableString, and the
// XmlConvert and "O"/"s"/"u"/"R"/"c" round-trip formats. The culture is built from the invariant one, so the
// sample is the same on every platform.
using System;
using System.Globalization;
using System.Numerics;
using System.Xml;

var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
odd.NumberFormat.NegativeSign = "−";
odd.NumberFormat.PositiveSign = "➕";
odd.NumberFormat.NumberDecimalSeparator = ",";
odd.NumberFormat.NumberGroupSeparator = " ";
odd.NumberFormat.NaNSymbol = "nan";
odd.DateTimeFormat.TimeSeparator = "h";
odd.DateTimeFormat.DateSeparator = "~";
var inv = CultureInfo.InvariantCulture;

void Show(string label)
{
    var big = BigInteger.Parse("-123456789012345678901234567890", inv);
    object boxed = big;
    Half[] halves = [(Half)(-0.25f), (Half)0.000061f, (Half)(-1.5e-5f), (Half)65504f, (Half)0.1f, (Half)(-100f)];
    Console.WriteLine(label);
    Console.WriteLine("  half inv:     " + string.Join(" ", Array.ConvertAll(halves, h => h.ToString(inv))));
    Console.WriteLine("  half G inv:   " + string.Join(" ", Array.ConvertAll(halves, h => h.ToString("G", inv))));
    Console.WriteLine("  half parse:   " + Half.Parse("-0.25", inv) + " " + Half.Parse("1e-3", NumberStyles.Float, inv).ToString(inv));
    Console.WriteLine("  big.ToString(fmt, provider): " + big.ToString(null, inv) + " | " + big.ToString("N0", inv) + " | " + big.ToString("D35", inv));
    Console.WriteLine("  big ToString(provider):      " + big.ToString(inv));
    Console.WriteLine("  big string.Format(inv):      " + string.Format(inv, "{0}|{0:N0}|{0:D35}|{0:X}", big));
    Console.WriteLine("  big Convert.ToString(inv):   " + Convert.ToString(boxed, inv));
    Console.WriteLine("  big FormattableString:       " + FormattableString.Invariant($"{big}|{big:N0}"));
    Console.WriteLine("  big odd provider:            " + big.ToString("N0", odd) + " | " + string.Format(odd, "{0:N0}", big));
    Console.WriteLine("  xml numbers:  " + XmlConvert.ToString(-1.5e-7) + " " + XmlConvert.ToString(-1.5e-7f) + " " + XmlConvert.ToString(-1234.5000m) + " " + XmlConvert.ToString(-5) + " " + XmlConvert.ToString(-5000000000L)
        + " " + XmlConvert.ToString(double.NaN) + " " + XmlConvert.ToString(double.NegativeInfinity));
    Console.WriteLine("  xml parse:    " + XmlConvert.ToDouble("-1.5e-7").ToString(inv) + " " + XmlConvert.ToDecimal("-1234.5000").ToString(inv) + " " + XmlConvert.ToInt32("-5") + " " + XmlConvert.ToInt64("-5000000000"));
    var dt = new DateTime(2026, 10, 23, 8, 5, 3, 120, DateTimeKind.Utc);
    var dto = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(-150));
    var ts = new TimeSpan(-3, 4, 5, 6, 789);
    Console.WriteLine("  xml time:     " + XmlConvert.ToString(dt, XmlDateTimeSerializationMode.Utc) + " " + XmlConvert.ToString(dto) + " " + XmlConvert.ToString(ts));
    Console.WriteLine("  dates:        " + dt.ToString("O") + " " + dt.ToString("s") + " " + dt.ToString("u") + " " + dt.ToString("R") + " " + dto.ToString("O"));
    Console.WriteLine("  timespan:     " + ts.ToString() + " " + ts.ToString("c") + " " + ts.ToString("c", odd));
    Console.WriteLine("  round trips:  " + DateTime.ParseExact(dt.ToString("O"), "O", inv, DateTimeStyles.RoundtripKind).ToString("O", inv) + " " + TimeSpan.ParseExact(ts.ToString("c"), "c", inv));
}

Show("invariant:");
CultureInfo.CurrentCulture = odd;
Show("odd culture:");
