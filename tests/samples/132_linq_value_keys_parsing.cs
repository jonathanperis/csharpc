// LINQ over decimal / nullable values, comparers for decimal, tuple, DateTime and BigInteger keys,
// NumberStyles parsing, Math.Clamp overloads, and the generic `(T)(object)x` identity cast.
using System.Globalization;
using System.Numerics;

decimal[] prices = [19.99m, 5m, 7.25m, 100.10m];
Console.WriteLine($"{prices.Sum()} {prices.Average():F4} {prices.Min()} {prices.Max()}");
var items = new[] { (Name: "a", Price: 3.5m, Qty: (int?)2), ("b", 1.25m, null), ("c", 10m, 7) };
Console.WriteLine($"{items.Sum(i => i.Price)} {items.Sum(i => i.Qty)} {items.Average(i => i.Qty)} {items.Max(i => i.Qty)} {items.Min(i => i.Price)}");
Console.WriteLine(string.Join(",", items.OrderByDescending(i => i.Price).Select(i => i.Name)));
var dates = new[] { new DateTime(2026, 3, 1), new DateTime(2025, 12, 31), new DateTime(2026, 1, 15) };
Console.WriteLine($"{dates.Max():yyyy-MM-dd} {dates.Min():yyyy-MM-dd} {string.Join(" ", dates.Order().Select(d => d.Month))}");
var spans = new[] { TimeSpan.FromHours(2), TimeSpan.FromMinutes(30) };
Console.WriteLine(spans.Max());
var pairs = new[] { (2, "x"), (1, "z"), (2, "a"), (1, "b") };
Console.WriteLine(string.Join(" ", pairs.Order().Select(p => $"{p.Item1}{p.Item2}")));
Console.WriteLine(string.Join(" ", pairs.GroupBy(p => p.Item1).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
var byPrice = new Dictionary<decimal, string> { [1.0m] = "one" };
Console.WriteLine(byPrice.ContainsKey(1.00m) + " " + byPrice[1m]);
var bigs = new[] { BigInteger.Pow(2, 70), BigInteger.One, BigInteger.Parse("-5") };
Console.WriteLine(string.Join(" ", bigs.Order()) + " " + new HashSet<BigInteger>(bigs).Contains(BigInteger.One));

Console.WriteLine(int.Parse("FF", NumberStyles.HexNumber) + " " + int.Parse("FFFFFFFF", NumberStyles.HexNumber));
Console.WriteLine(int.Parse("1,234,567", NumberStyles.AllowThousands, CultureInfo.InvariantCulture));
Console.WriteLine(long.Parse(" -42 ", NumberStyles.Integer));
Console.WriteLine(double.TryParse("3.5e2", NumberStyles.Float, CultureInfo.InvariantCulture, out var d) + " " + d);
Console.WriteLine(int.TryParse("12x", NumberStyles.Integer, CultureInfo.InvariantCulture, out var bad) + " " + bad);
Console.WriteLine(byte.TryParse("300", NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) + " " + b);
try { int.Parse("99999999999", NumberStyles.Integer); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { int.Parse("nope", NumberStyles.Integer); } catch (FormatException e) { Console.WriteLine(e.Message); }

Console.WriteLine($"{Math.Clamp(1.5, 0.0, 1.0)} {Math.Clamp(-3L, 0L, 10L)} {Math.Clamp(5f, 6f, 9f)} {Math.Clamp(double.NaN, 0, 1)}");
Console.WriteLine(Identity((1, "tuple")) + " " + Identity(42) + " " + Identity("s"));

static T Identity<T>(T value) => (T)(object)value!;
