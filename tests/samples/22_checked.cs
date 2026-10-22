// checked/unchecked arithmetic and conversions; ends with an unhandled OverflowException.
int big = int.MaxValue;
long l = 300;
uint u = 3_000_000_000;
double d = 1e9;
Console.WriteLine(unchecked(big + 1));
Console.WriteLine(checked(big - 1));
Console.WriteLine(checked((byte)(l - 100)));
Console.WriteLine(checked((int)(d / 10)));
Console.WriteLine(checked((long)u * 2));
Console.WriteLine(checked((uint)l));
int x = 5;
checked { x *= 1000; x++; x -= 3; }
Console.WriteLine(x);
Console.WriteLine(unchecked((byte)l));
Console.WriteLine(unchecked((int)u));
Console.WriteLine(Fits(300) + " " + Fits(200));
Console.WriteLine(checked((int)u)); // throws

static bool Fits(long v) => v >= byte.MinValue && v <= byte.MaxValue;
