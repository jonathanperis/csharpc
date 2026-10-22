// decimal arithmetic, rounding, conversions and parsing (corelib DecimalOps) against .NET.
using System;
using System.Collections.Generic;

decimal[] xs = { 0m, 1m, -1m, 0.1m, 1.10m, 3.14159265358979323846264338m, -2.5m, 79228162514264337593543950335m, 0.0000000000000000000000000001m, 12345.6789m, 1e-10m, -0.00m };
foreach (var a in xs)
    foreach (var b in new[] { 3m, -0.7m, 1.000m, 7.9e-5m })
    {
        string Try(Func<decimal> f) { try { return f().ToString(); } catch (Exception e) { return e.GetType().Name; } }
        Console.WriteLine($"{a} {b}: {Try(() => a + b)} {Try(() => a - b)} {Try(() => a * b)} {Try(() => a / b)} {Try(() => a % b)} {a.CompareTo(b)} {a == b}");
    }
Console.WriteLine(1m / 3m + " " + 2m / 3m + " " + 1m / 7m * 7m + " " + 1e-28m / 3m + " " + 10m / 4m + " " + 100m / 0.01m);
foreach (var v in new[] { 2.345m, -2.345m, 2.5m, 3.5m, -2.5m, 0.005m })
    Console.WriteLine($"{Math.Round(v, 2)} {Math.Round(v, 2, MidpointRounding.AwayFromZero)} {decimal.Round(v)} {Math.Floor(v)} {Math.Ceiling(v)} {decimal.Truncate(v)} {Math.Round(v, 1, MidpointRounding.ToZero)} {Math.Round(v, 1, MidpointRounding.ToNegativeInfinity)} {Math.Round(v, 1, MidpointRounding.ToPositiveInfinity)}");
foreach (var d in new[] { 0.1, 1.0 / 3, 123456789.123456789, -1e-20, 1e20, 0.5, 7.9e28 / 2, 1e-30 })
    Console.WriteLine($"{(decimal)d} {(decimal)(float)d} {(double)(decimal)d:R}");
Console.WriteLine((double)(1m / 3m) + " " + (float)(2m / 3m) + " " + (int)12.99m + " " + (long)-7.5m + " " + (byte)255.9m + " " + (char)65.2m + " " + (ulong)18446744073709551615m);
decimal big = 3000000000m, minusOne = -1m, max = decimal.MaxValue, zero = 0m;
double huge = 1e30;
try { Console.WriteLine((int)big); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { Console.WriteLine((byte)minusOne); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { Console.WriteLine(max + 1); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { Console.WriteLine((decimal)huge); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { Console.WriteLine(1m / zero); } catch (DivideByZeroException e) { Console.WriteLine(e.Message); }
foreach (var s in new[] { "1.5", "  -0.000  ", "1,234.5678", "79228162514264337593543950335", "79228162514264337593543950336", "0.00000000000000000000000000015", "1.23456789012345678901234567895", "abc", "", "1e5" })
    Console.WriteLine($"[{s}] {(decimal.TryParse(s, out var r) ? r.ToString() : "fail")}");
var set = new HashSet<decimal> { 1m, 1.0m, 1.00m, 2.5m, -0m, 0m };
Console.WriteLine(set.Count + " " + (1.0m).GetHashCode().Equals(1m.GetHashCode()));
object boxed = 1.50m, other = 1.5m;
Console.WriteLine(boxed.Equals(other) + " " + (boxed.GetHashCode() == other.GetHashCode()) + " " + boxed);
foreach (var (p, q) in new[] { (5.5m, 5.50m), (-5.5m, 5.500m), (7m, 2.5m), (-7.25m, 2m), (3m, 0.003m), (1.5m, -0.4m) })
    Console.WriteLine($"{p % q} {q % p} {0.000m % q}");
char cc = 'x'; decimal dc = 70000m;
try { cc = (char)dc; } catch (OverflowException e) { Console.WriteLine(e.Message + cc); }
try { Console.WriteLine((sbyte)dc); } catch (OverflowException e) { Console.WriteLine(e.Message); }
