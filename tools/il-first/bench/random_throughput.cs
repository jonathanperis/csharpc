// Random throughput, the program behind the speed numbers of docs/il-first/System.Random.md (tools/il-first/measure-aot.sh): Next on a
// seeded (legacy) and an unseeded (xoshiro) generator, NextBytes, NextDouble. Only the seeded results reach the output, so it is
// deterministic; the unseeded ones feed a range check.
using System;

var seeded = new Random(1);
var unseeded = new Random();
long sum = 0;
bool inRange = true;
for (int i = 0; i < 50_000_000; i++)
{
    sum += seeded.Next(1000);
    if ((uint)unseeded.Next(1000) >= 1000) inRange = false;
}
var bytes = new byte[1 << 20];
for (int i = 0; i < 50; i++) unseeded.NextBytes(bytes);
double d = 0;
for (int i = 0; i < 20_000_000; i++) d += seeded.NextDouble();
Console.WriteLine($"{sum} {d:F3} {inRange}");
