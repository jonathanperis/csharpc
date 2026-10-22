// Console output throughput: number formatting + writing 1M lines.
long total = 0;
for (int i = 0; i < 1_000_000; i++)
{
    Console.WriteLine(i * 7 % 1000003);
    total += i;
}
Console.WriteLine(total);
Console.WriteLine(2.5 * total);
