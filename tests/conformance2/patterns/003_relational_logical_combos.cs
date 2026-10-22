static string Grade(int s) => s switch { < 0 or > 100 => "invalid", >= 90 => "A", >= 80 and < 90 => "B", not (< 70) => "C", _ => "F" };
foreach (var s in new[] { -1, 95, 85, 75, 10, 101 }) Console.Write(Grade(s) + " ");
Console.WriteLine();
object o = 3.5;
Console.WriteLine(o is double and (> 3 and < 4) ? "in range" : "no");
