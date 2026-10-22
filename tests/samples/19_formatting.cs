// Numeric format strings, alignment and composite formatting (invariant culture).
double price = 1234.5678;
int count = 42;
Console.WriteLine(price.ToString("F2"));
Console.WriteLine(price.ToString("N2"));
Console.WriteLine(price.ToString("E3"));
Console.WriteLine(price.ToString("0.0"));
Console.WriteLine(price.ToString("#,##0.00"));
Console.WriteLine(0.125.ToString("F2"));
Console.WriteLine(2.5.ToString("F0"));
Console.WriteLine(1.005.ToString("F2"));
Console.WriteLine((-0.004).ToString("F2"));
Console.WriteLine(0.4567.ToString("P1"));
Console.WriteLine(count.ToString("D5"));
Console.WriteLine(count.ToString("X"));
Console.WriteLine((-1).ToString("X8"));
Console.WriteLine(255.ToString("x4"));
Console.WriteLine(1234567.ToString("N0"));
Console.WriteLine(7.ToString("000"));
Console.WriteLine(123456789.0.ToString("G4"));
Console.WriteLine(0.000012345.ToString("G3"));
Console.WriteLine(Math.PI.ToString("G"));

Console.WriteLine($"[{count,6}] [{count,-6}] [{price,12:F3}] [{"left",-8}|]");
Console.WriteLine($"{price:N1} items: {count:D3} hex: {count:X2}");
Console.WriteLine(string.Format("{0} + {1} = {2}", 2, 3, 2 + 3));
Console.WriteLine("{0,-10}|{1,8:F2}|", "name", 3.14159);
Console.WriteLine("braces: {{literal}} {0}", "ok");
Console.WriteLine(string.Format("{0:N2} {1}", 9876.543, true));
Console.Write("{0}-{1}\n", 'a', 1.5f);

for (int i = 1; i <= 3; i++)
    Console.WriteLine($"{i,2}. {i * 1.1,6:F2} {i * i,4:D3}");
