var words = new[] { "apple", "bob", "avocado", "cat", "banana", "cherry" };
foreach (var g in words.GroupBy(w => w[0])) Console.WriteLine($"{g.Key}: {string.Join(",", g)}");
foreach (var g in words.GroupBy(w => w.Length, w => w.ToUpper())) Console.WriteLine($"{g.Key}={g.Count()}:{string.Join("|", g)}");
