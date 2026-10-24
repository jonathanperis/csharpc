var words = new[] { "pear", "apple", "fig", "banana" };
foreach (var w in words.Where(w => w.Length > 3).OrderBy(w => w).Select(w => w.ToUpperInvariant()))
    Console.WriteLine(w);
