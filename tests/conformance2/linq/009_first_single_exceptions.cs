static void T(Func<object> f) { try { Console.WriteLine(f()); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); } }
var empty = Array.Empty<int>();
T(() => empty.First());
T(() => new[] { 1, 2 }.Single());
T(() => empty.Max());
T(() => new[] { 1, 2 }.SingleOrDefault(x => x > 5));
T(() => empty.Average());
