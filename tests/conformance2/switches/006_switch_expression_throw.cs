static int Parse(string s) => s switch { "one" => 1, "two" => 2, _ => throw new ArgumentException($"bad: {s}") };
Console.WriteLine(Parse("two"));
try { Parse("three"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
