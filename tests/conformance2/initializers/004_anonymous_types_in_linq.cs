var q = new[] { "apple", "kiwi", "banana" }.Select(w => new { Word = w, w.Length }).Where(x => x.Length > 4).OrderBy(x => x.Length);
foreach (var x in q) Console.WriteLine(x);
var a1 = new { A = 1, B = "x" }; var a2 = new { A = 1, B = "x" };
Console.WriteLine($"{a1.Equals(a2)} {a1 == a2}");
