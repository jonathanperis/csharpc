// Console.Write/WriteLine(int / long) format with the current culture like int.ToString(): sv-SE (from
// 423_console_int_culture.env: LC_ALL=sv_SE.UTF-8) writes negative numbers with U+2212 MINUS SIGN.
int[] values = [0, 7, -7, int.MinValue, int.MaxValue];
foreach (var v in values)
{
    Console.WriteLine(v);
    Console.Write(v);
    Console.Write((long)v * 3);
    Console.WriteLine();
}
long big = long.MinValue;
Console.WriteLine(big);
Console.Out.WriteLine(-42);
Console.WriteLine(-values[2] + " " + values[2] + $" {values[2]}");
