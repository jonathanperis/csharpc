// BigInteger (corelib BigIntegerOps): Knuth division, powers, conversions, formatting, boxing.
using System;
using System.Numerics;
using System.Collections.Generic;

var f = BigInteger.One;
for (int i = 1; i <= 60; i++) f *= i;
var p = BigInteger.Pow(new BigInteger(3), 200) - 1;
Console.WriteLine(f + "\n" + p + "\n" + f / p + " " + f % p + " " + p / f);
var q = BigInteger.DivRem(p, BigInteger.Parse("123456789012345678901234567890"), out var r);
Console.WriteLine(q + " " + r + " " + BigInteger.GreatestCommonDivisor(f, p * 7) + " " + BigInteger.ModPow(7, p, f + 1));
BigInteger[] vs = { 0, 1, -1, long.MaxValue, long.MinValue, ulong.MaxValue, BigInteger.Parse("-98765432109876543210"), new BigInteger(1e30), new BigInteger(-123.99), new BigInteger(4294967296.0) };
foreach (var v in vs)
    Console.WriteLine($"{v} {v:D25} {v:N} {v:N0} {(v.Sign >= 0 ? v.ToString("X") : "-")} {v.IsEven} {v.Sign} {(double)v:R} {-v} {BigInteger.Abs(v)} {v + 1} {v - ulong.MaxValue} {v * v} {v.CompareTo(BigInteger.Zero)}");
foreach (var s in new[] { "0", "-0", " 42 ", "+17", "1e3", "", "12a", "340282366920938463463374607431768211456" })
    Console.WriteLine($"[{s}] {(BigInteger.TryParse(s, out var b) ? b.ToString() : "fail")}");
Console.WriteLine((long)(BigInteger)long.MaxValue + " " + (int)new BigInteger(-5) + " " + (ulong)BigInteger.Parse("18446744073709551615"));
try { Console.WriteLine((int)f); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { Console.WriteLine(f / BigInteger.Zero); } catch (DivideByZeroException e) { Console.WriteLine(e.Message); }
object boxed = f, other = BigInteger.Parse(f.ToString());
Console.WriteLine(boxed.Equals(other) + " " + (boxed.GetHashCode() == other.GetHashCode()) + " " + boxed.ToString().Length);
var set = new HashSet<BigInteger> { 1, BigInteger.One, 2, -1 };
Console.WriteLine(set.Count);
