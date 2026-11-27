// Shortest round-trip formatting of double and float: Grisu3 and its Dragon4 fallback, which csharpc lowers
// from the framework's own IL (src/Csharpc/upstream-types/System.Number.txt).
using System.Globalization;

var inv = CultureInfo.InvariantCulture;
double[] ds = [0.1 + 0.2, 1.0 / 3, double.Epsilon, double.MaxValue, double.MinValue, 2.2250738585072014E-308, 2.225073858507201E-308,
    1e21, 1e-7, 123456789012345678, 5e-324, 0.5, -0.0, 9007199254740993, 1.7976931348623157e308, 4.35, 0.3, 1e23, 8.41e21, 5e-310,
    BitConverter.Int64BitsToDouble(0x43A0942CB1DCA053)]; // Grisu3 gives up on this one: Dragon4 produces the digits
foreach (var d in ds)
    Console.WriteLine($"{d.ToString(inv)} {d.ToString("R", inv)} {d.ToString("G17", inv)} {d.ToString("E", inv)} {double.Parse(d.ToString("R", inv), inv) == d}");
float[] fs = [0.1f + 0.2f, 1f / 3, float.Epsilon, float.MaxValue, float.MinValue, 1.17549435E-38f, 16777217f, 3.4e38f, 1e-45f, 0.3f, 7e-39f];
foreach (var f in fs)
    Console.WriteLine($"{f.ToString(inv)} {f.ToString("R", inv)} {f.ToString("G9", inv)} {f.ToString("E", inv)} {float.Parse(f.ToString("R", inv), inv) == f}");

// Many random bit patterns, Dragon4 fallbacks among them, folded into hashes of the text.
var random = new Random(7);
long dh = 0, fh = 0;
int roundTrips = 0;
for (int i = 0; i < 100000; i++)
{
    double x = BitConverter.Int64BitsToDouble(random.NextInt64() & 0x7FEFFFFFFFFFFFFF);
    var text = x.ToString(inv);
    foreach (var c in text) dh = dh * 31 + c;
    if (double.Parse(text, inv) == x) roundTrips++;
    float y = BitConverter.Int32BitsToSingle(random.Next() & 0x7F7FFFFF);
    foreach (var c in y.ToString(inv)) fh = fh * 31 + c;
}
Console.WriteLine($"{dh} {fh} {roundTrips}");
