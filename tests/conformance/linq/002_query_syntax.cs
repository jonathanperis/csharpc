string[] words = { "apple", "bob", "cherry", "date", "egg" };
var q = from w in words where w.Length > 3 orderby w descending select w.ToUpper();
foreach (var w in q) Console.WriteLine(w);
