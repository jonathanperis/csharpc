foreach (var (a, b) in new[] { (7, 2), (-7, 2), (7, -2), (-7, -2) }) Console.Write($"{a / b},{a % b} ");
Console.WriteLine();
Console.WriteLine($"{-7.5 % 2} {7.5 % -2} {Math.IEEERemainder(7.5, 2)}");
int min = int.MinValue, m1 = -1;
try { Console.WriteLine(min / m1); } catch (OverflowException) { Console.WriteLine("ovf"); }
