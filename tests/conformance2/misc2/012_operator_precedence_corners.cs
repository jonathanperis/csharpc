int a = 2, b = 3, c = 4;
Console.WriteLine(a + b * c << 1);
Console.WriteLine(a < b == b < c);
Console.WriteLine(a & b | c ^ a);
Console.WriteLine(-a * -b % c);
Console.WriteLine(!(a > b) ? "x" : "y" + "z");
bool? n = null;
Console.WriteLine(n ?? a > b);
