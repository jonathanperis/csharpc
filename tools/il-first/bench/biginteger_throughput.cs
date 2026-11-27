// BigInteger throughput, the program behind the NativeAOT column of docs/il-first/System.Runtime.Numerics.md (tools/il-first/measure-aot.sh):
// factorials and products (multiplication), division and remainder, ModPow, GCD, decimal and hex formatting, decimal parsing. Checksums only.
using System;
using System.Numerics;

long check = 0;
BigInteger f = BigInteger.One;
for (int i = 2; i <= 3000; i++) f *= i;
check += f.GetBitLength();
BigInteger m = BigInteger.Pow(10, 200) + 357;
for (int round = 0; round < 300; round++)
{
    BigInteger q = BigInteger.DivRem(f, m + round, out var r);
    check += (long)(r % 1000003) + q.GetByteCount();
    check += (long)(BigInteger.ModPow(r + 3, 65537 + round, m) % 1000003);
    check += (long)(BigInteger.GreatestCommonDivisor(q + round, m * 6) % 1000003);
}
for (int round = 0; round < 40; round++)
{
    string text = (f + round).ToString();
    check += text.Length + text[round];
    check += (long)(BigInteger.Parse(text) % 1000003);
    string hex = (f - round).ToString("X");
    check += hex.Length + hex[round];
}
Console.WriteLine(check);
