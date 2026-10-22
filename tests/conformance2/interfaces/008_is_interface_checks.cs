object[] xs = { 1, "s", new List<int>(), new int[0], new Dictionary<string, int>() };
foreach (var x in xs) Console.WriteLine($"{x is IComparable} {x is IEnumerable<int>} {x is System.Collections.IEnumerable} {x is IDictionary<string, int>}");
