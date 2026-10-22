static IEnumerable<string> Safe(string[] xs) {
    foreach (var s in xs) {
        int v;
        try { v = int.Parse(s); } catch (FormatException) { v = -1; }
        yield return $"{s}->{v}";
    }
}
foreach (var r in Safe(new[] { "1", "x", "3" })) Console.WriteLine(r);
