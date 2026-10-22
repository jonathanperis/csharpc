using System.Numerics;
var f = BigInteger.One;
for (int i = 2; i <= 30; i++) f *= i;
Console.WriteLine(f);
Console.WriteLine(BigInteger.Pow(2, 100) - 1);
Console.WriteLine(BigInteger.Parse("123456789012345678901234567890") % 97);
Console.WriteLine(BigInteger.GreatestCommonDivisor(new BigInteger(1071), 462));
Console.WriteLine(-BigInteger.Pow(10, 20) / 7);
