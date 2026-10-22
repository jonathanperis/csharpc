// System.Numerics.BigInteger: arbitrary precision (runtime/bigint.c).
using System.Numerics;

BigInteger f = 1;
for (int i = 2; i <= 50; i++) f *= i;
Console.WriteLine($"50! = {f}");
Console.WriteLine(BigInteger.Pow(2, 200));
Console.WriteLine(BigInteger.Pow(-3, 41) + " " + BigInteger.Pow(10, 30) / 7 + " " + BigInteger.Pow(10, 30) % 7);
BigInteger a = BigInteger.Parse("123456789012345678901234567890"), b = BigInteger.Parse("-987654321098765432109876543210");
Console.WriteLine(a + b);
Console.WriteLine(a - b);
Console.WriteLine(a * b);
Console.WriteLine(b / a + " " + b % a + " " + (-b) / 12345 + " " + a / -1000000007);
Console.WriteLine((a < b) + " " + (a > 1000) + " " + (b == -b * -1) + " " + (a != a + 1) + " " + a.CompareTo(b) + " " + BigInteger.Compare(b, 5));
Console.WriteLine(BigInteger.GreatestCommonDivisor(a, b) + " " + BigInteger.ModPow(3, 1000, 1000000007) + " " + BigInteger.Abs(b));
var q = BigInteger.DivRem(a, 1000, out BigInteger r);
Console.WriteLine(q + " rem " + r);
Console.WriteLine((long)(a / BigInteger.Pow(10, 12)) + " " + (int)(BigInteger)123456 + " " + (double)a + " " + (BigInteger)1e20 + " " + new BigInteger(42.9));
Console.WriteLine(BigInteger.Zero.IsZero + " " + BigInteger.One.IsOne + " " + a.IsEven + " " + b.Sign + " " + BigInteger.MinusOne + " " + default(BigInteger));
BigInteger x = 0;
x++; x += 41; x--;
Console.WriteLine(x + " " + BigInteger.Max(a, b) + " " + BigInteger.Min(a, b));
object boxed = f;
Console.WriteLine(boxed.ToString()!.Length + " " + boxed.Equals(f) + " " + (BigInteger.TryParse("12x", out var bad) ? "?" : "bad") + " " + bad);
Console.WriteLine(a.ToString("N0") + " " + new BigInteger(255).ToString("X") + " " + new BigInteger(42).ToString("D6"));
BigInteger fa = 0, fb = 1;
for (int i = 0; i < 300; i++) (fa, fb) = (fb, fa + fb);
Console.WriteLine("fib(300) = " + fa);
