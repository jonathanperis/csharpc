// System.Decimal: CLR layout, exact arithmetic, banker's rounding, formatting.
decimal price = 19.99m, qty = 3m, rate = 0.0825m;
decimal subtotal = price * qty;
decimal tax = Math.Round(subtotal * rate, 2);
Console.WriteLine($"subtotal={subtotal} tax={tax} total={subtotal + tax}");
Console.WriteLine(-0.0m); Console.WriteLine(-1m + 1m); Console.WriteLine(Math.Round(-0.4m));
Console.WriteLine(0.125m.ToString("F2")); Console.WriteLine(2.5m.ToString("F0")); Console.WriteLine((-2.5m).ToString("F0"));
Console.WriteLine(1m / 3m); Console.WriteLine(2m / 3m); Console.WriteLine(100m / 3m); Console.WriteLine(10m / 4m);
Console.WriteLine(1.00m / 1m); Console.WriteLine(1m / 0.5m); Console.WriteLine(0m / 2m); Console.WriteLine(0.00m / 3m); Console.WriteLine(1e-28m / 3);
Console.WriteLine(1.5m * 1.50m); Console.WriteLine(0.1m + 0.20m); Console.WriteLine(1.10m - 0.10m); Console.WriteLine(3m * 1.000m);
Console.WriteLine(0.0000000000000000000000000001m * 0.1m);
double d1 = 0.1, d2 = 1.0 / 3.0, d3 = 1e20, d4 = 7.9e28;
float f1 = 0.1f, f2 = 1.5f;
Console.WriteLine((decimal)d1); Console.WriteLine((decimal)d2); Console.WriteLine((decimal)d3); Console.WriteLine((decimal)d4);
Console.WriteLine((decimal)f1); Console.WriteLine((decimal)f2 / 3);
decimal third = 1m / 3m;
Console.WriteLine((double)third); Console.WriteLine((double)0.1m);
Console.WriteLine(decimal.MaxValue); Console.WriteLine(decimal.MinValue);
Console.WriteLine(7.5m % 2m); Console.WriteLine(-7.5m % 2m);
Console.WriteLine(Math.Round(2.345m, 2)); Console.WriteLine(Math.Round(2.5m)); Console.WriteLine(Math.Round(3.5m));
Console.WriteLine(Math.Round(1.50m, 3)); Console.WriteLine(Math.Round(2.5m, MidpointRounding.AwayFromZero));
Console.WriteLine(decimal.Round(2.675m, 2)); Console.WriteLine(Math.Round(2.675m, 2, MidpointRounding.AwayFromZero));
Console.WriteLine(decimal.Floor(-1.5m)); Console.WriteLine(decimal.Ceiling(-1.5m)); Console.WriteLine(decimal.Truncate(-1.5m));
Console.WriteLine(decimal.Parse("1.50")); Console.WriteLine(decimal.Parse(" -12,345.6789 "));
Console.WriteLine(decimal.TryParse("abc", out decimal bad) + " " + bad);
Console.WriteLine(1234567.891m.ToString("N2")); Console.WriteLine(1234.5m.ToString("C")); Console.WriteLine(0.5m.ToString("P0"));
Console.WriteLine(1.23456789m.ToString("E3")); Console.WriteLine(1.5m.ToString("G"));
decimal a = 1234.5678m;
Console.WriteLine($"{a:N1}|{a,12:F2}|{a:0.00}|{a:#,##0.0}");
Console.WriteLine(string.Format("{0:C} {1}", 3.5m, 2.50m));
Console.WriteLine((long)-123.99m); Console.WriteLine((int)99.999m); Console.WriteLine((byte)255.9m);
Console.WriteLine(1m.GetHashCode() == 1.0m.GetHashCode()); Console.WriteLine(1.0m == 1.00m); Console.WriteLine(1.0m.Equals(1.00m));
Console.WriteLine(new decimal(42)); Console.WriteLine(new decimal(1.5)); Console.WriteLine(new decimal(1, 0, 0, true, 2));
decimal acc = 0;
for (int i = 0; i < 10; i++) acc += 0.1m;
Console.WriteLine(acc == 1m);
acc++; acc--; acc -= 0.5m; acc *= 2; acc /= 4;
Console.WriteLine(acc);
object boxed = 12.30m;
Console.WriteLine(boxed); Console.WriteLine(boxed is decimal); Console.WriteLine((decimal)boxed * 2);
decimal[] values = [3.5m, -1.25m, 10m, 0.001m];
decimal sum = 0, max = decimal.MinValue;
foreach (var v in values) { sum += v; max = Math.Max(max, v); }
Console.WriteLine($"sum={sum} max={max} min={Math.Min(values[0], values[1])} abs={Math.Abs(values[1])} sign={Math.Sign(values[1])}");
Console.WriteLine(values[0].CompareTo(values[1]) + " " + decimal.Compare(1m, 2m));
int n = 7;
decimal fromInt = n;
long big = 9_000_000_000L;
Console.WriteLine(fromInt / 2 + " " + (decimal)big * 3 + " " + (fromInt > 6.5m));
Console.WriteLine(Interest(1000m, 0.05m, 10));
decimal mx = decimal.MaxValue;
Console.WriteLine((int)mx); // OverflowException (unhandled)

static decimal Interest(decimal principal, decimal rate, int years)
{
    for (int y = 0; y < years; y++) principal = Math.Round(principal * (1 + rate), 2, MidpointRounding.ToEven);
    return principal;
}
