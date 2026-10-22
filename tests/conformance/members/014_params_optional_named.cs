Console.WriteLine(Sum(1, 2, 3));
Console.WriteLine(Sum());
Console.WriteLine(Greet("Bob"));
Console.WriteLine(Greet(greeting: "Yo", name: "Al"));
static int Sum(params int[] xs) { int s = 0; foreach (var x in xs) s += x; return s; }
static string Greet(string name, string greeting = "Hello") => greeting + ", " + name;
