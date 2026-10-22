int s = 0;
for (int i = 0; i < 10; i++) s += i;
int w = 0; while (w < 5) w++;
int d = 0; do { d += 2; } while (d < 7);
foreach (var c in "abc") s += c;
Console.WriteLine($"{s} {w} {d}");
