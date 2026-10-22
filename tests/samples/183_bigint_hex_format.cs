// BigInteger "X"/"x": shortest two's complement whose top nibble carries the sign; a precision pads
// with '0' (positive) or 'F'/'f' (negative).
using System.Numerics;

long[] values =
{
    0, 1, -1, 7, 8, -8, -9, 15, 16, -16, -17, 127, 128, -128, -129, 255, 256, -255, -256, -980362,
    0x80000000, -0x80000000L, -0x80000001L, 0xFFFFFFFFL, -0xFFFFFFFFL, long.MinValue, long.MaxValue,
};
foreach (long v in values)
{
    var b = new BigInteger(v);
    Console.WriteLine(v + " " + b.ToString("X") + " " + b.ToString("x") + " " + b.ToString("X10") + " " + b.ToString("x3"));
}
var big = BigInteger.Pow(2, 100);
Console.WriteLine((-big).ToString("X") + " " + big.ToString("X") + " " + (-big - 1).ToString("x") + " " + (-big + 1).ToString("X40"));
