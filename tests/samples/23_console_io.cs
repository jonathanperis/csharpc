// Console input (stdin from 23_console_io.stdin), Console.Out/Error, Math extras.
Console.Write("name? ");
string? name = Console.ReadLine();
Console.WriteLine($"hello, {name}!");
int total = 0, lines = 0;
string? line;
while ((line = Console.ReadLine()) != null)
{
    if (line.Length == 0) continue;
    lines++;
    foreach (var part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        if (int.TryParse(part, out int n)) total += n;
}
Console.Out.WriteLine($"{lines} lines, sum = {total}");
Console.Error.WriteLine("this goes to stderr");
Console.Out.Flush();
Console.WriteLine(Math.Round(2.345, 2) + " " + Math.Round(2.355, 2) + " " + Math.Round(-1.5, 0));
Console.WriteLine($"{Math.Sign(-7)} {Math.Sign(0.0)} {Math.Sign(3L)} {Math.Log2(1024)}");
Console.WriteLine(Math.Max(1.5f, 2.5f) + " " + MathF.Sqrt(16f));
