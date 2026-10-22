Console.WriteLine(Registry.Count + " " + Registry.Names.Count);
Registry.Add("x");
Console.WriteLine(Registry.Count);
static class Registry { public static readonly List<string> Names; public static int Count => Names.Count; static Registry() { Names = new List<string> { "seed" }; } public static void Add(string n) => Names.Add(n); }
