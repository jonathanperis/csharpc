// Structured control flow — all of it is lowered to basic blocks by Roslyn's CFG builder.
for (int i = 1; i <= 15; i++)
{
    if (i % 15 == 0) Console.WriteLine("FizzBuzz");
    else if (i % 3 == 0) Console.WriteLine("Fizz");
    else if (i % 5 == 0) Console.WriteLine("Buzz");
    else Console.WriteLine(i);
}

int n = 0;
while (true)
{
    n++;
    if (n % 2 == 0) continue;
    if (n > 7) break;
    Console.Write(n);
    Console.Write(' ');
}
Console.WriteLine();

int k = 100;
do { k /= 3; } while (k > 5);
Console.WriteLine(k);

for (int day = 0; day < 8; day++)
{
    switch (day)
    {
        case 0:
        case 6:
            Console.WriteLine("weekend");
            break;
        case 3:
            Console.WriteLine("midweek");
            break;
        default:
            Console.WriteLine("weekday " + day);
            break;
    }
}

foreach (var t in new[] { -40, 0, 15, 22, 35 })
    Console.WriteLine($"{t}: {Describe(t)}");

Console.WriteLine(Classify(0) + " " + Classify(7) + " " + Classify(-3) + " " + Classify(1000));

bool a = true, b = false;
Console.WriteLine(a && !b || b);
Console.WriteLine(a & b | a ^ b);
string? maybe = n > 100 ? "big" : null;
Console.WriteLine(maybe ?? "fallback");
Console.WriteLine(maybe?.Length ?? -1);

// goto is fine too
int g = 0;
again:
g++;
if (g < 3) goto again;
Console.WriteLine($"goto looped {g} times");

static string Describe(int celsius) => celsius switch
{
    < 0 => "freezing",
    0 => "zero",
    >= 1 and < 20 => "mild",
    < 30 => "warm",
    _ => "hot",
};

static string Classify(int value)
{
    if (value is 0) return "zero";
    if (value is > 0 and < 10) return "small";
    if (value is not >= 0) return "negative";
    return "large";
}
