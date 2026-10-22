// Enums: underlying-type arithmetic, switch, and name formatting.
var colors = new[] { Color.Red, Color.Green, Color.Blue };
foreach (var c in colors) Console.WriteLine($"{c} = {(int)c}");
Color mix = Color.Red + 1;
Console.WriteLine(mix);
Console.WriteLine((Color)42);
Console.WriteLine(Describe(Color.Blue));
Console.WriteLine(Level.High.ToString() + " " + (Level.Low < Level.High));
Console.WriteLine(Level.Medium);

static string Describe(Color c) => c switch
{
    Color.Red => "warm",
    Color.Green or Color.Blue => "cool",
    _ => "unknown",
};

enum Color { Red, Green, Blue }
enum Level : byte { Low = 1, Medium = 5, High = 10 }
