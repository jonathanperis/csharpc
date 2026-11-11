// Random without a seed (seeded from the OS RNG) and Random.Shared: the values differ per run, so the sample prints only what holds for
// every run: ranges, lengths, that two unseeded generators disagree, and the seeded generator's exact sequence next to them.
var a = new Random();
var b = new Random();
bool inRange = true;
for (int i = 0; i < 1000; i++)
{
    int n = a.Next(10);
    double d = a.NextDouble();
    long l = Random.Shared.NextInt64(-5, 5);
    float f = Random.Shared.NextSingle();
    if (n < 0 || n >= 10 || d < 0 || d >= 1 || l < -5 || l >= 5 || f < 0 || f >= 1) inRange = false;
}
Console.WriteLine($"in range: {inRange}");
var x = new byte[32];
var y = new byte[32];
a.NextBytes(x);
b.NextBytes(y);
Console.WriteLine($"two unseeded generators differ: {!x.AsSpan().SequenceEqual(y)}");
int[] items = [1, 2, 3, 4, 5, 6, 7, 8];
Random.Shared.Shuffle(items);
Array.Sort(items);
Console.WriteLine($"shuffle keeps the items: {string.Join(",", items)}");
Console.WriteLine($"GetItems length: {Random.Shared.GetItems(new[] { 'a', 'b' }, 5).Length}");
var seeded = new Random(42);
Console.WriteLine($"seeded: {seeded.Next()} {seeded.Next(100)} {seeded.NextDouble():R} {seeded.NextInt64()}");
try { a.Next(5, 1); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.GetType().Name}: {e.ParamName}"); }
