// String methods called with a constant StringComparison.Ordinal / OrdinalIgnoreCase bind to dedicated
// ordinal implementations (no culture code linked); results and argument exceptions must be .NET's, and
// the same as with a non-constant comparison.
string s = "Hello, World! hello, wORLD! ıſ";
string? nothing = null;
var variable = args.Length > 5 ? StringComparison.CurrentCulture : StringComparison.Ordinal;
var variableIgnoreCase = args.Length > 5 ? StringComparison.CurrentCulture : StringComparison.OrdinalIgnoreCase;

void Show(string label, Func<object> f)
{
    try { Console.WriteLine($"{label}: {f()}"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
}

Show("StartsWith O", () => s.StartsWith("Hello", StringComparison.Ordinal));
Show("StartsWith O miss", () => s.StartsWith("hello", StringComparison.Ordinal));
Show("StartsWith OIC", () => s.StartsWith("hELLO", StringComparison.OrdinalIgnoreCase));
Show("StartsWith var", () => s.StartsWith("hELLO", variableIgnoreCase));
Show("StartsWith empty", () => s.StartsWith("", StringComparison.Ordinal));
Show("StartsWith self", () => s.StartsWith(s, StringComparison.OrdinalIgnoreCase));
Show("StartsWith longer", () => "ab".StartsWith("abc", StringComparison.OrdinalIgnoreCase));
Show("StartsWith null", () => s.StartsWith(nothing!, StringComparison.Ordinal));
Show("StartsWith null OIC", () => s.StartsWith(nothing!, StringComparison.OrdinalIgnoreCase));
Show("EndsWith O", () => s.EndsWith("ıſ", StringComparison.Ordinal));
Show("EndsWith OIC", () => s.EndsWith("Iſ", StringComparison.OrdinalIgnoreCase));
Show("EndsWith OIC 2", () => s.EndsWith("IS", StringComparison.OrdinalIgnoreCase));
Show("EndsWith var", () => s.EndsWith("IS", variableIgnoreCase));
Show("EndsWith null", () => s.EndsWith(nothing!, StringComparison.Ordinal));
Show("IndexOf O", () => s.IndexOf("hello", StringComparison.Ordinal));
Show("IndexOf OIC", () => s.IndexOf("WORLD", StringComparison.OrdinalIgnoreCase));
Show("IndexOf O start", () => s.IndexOf("o", 5, StringComparison.Ordinal));
Show("IndexOf OIC start", () => s.IndexOf("WORLD", 10, StringComparison.OrdinalIgnoreCase));
Show("IndexOf var start", () => s.IndexOf("WORLD", 10, variableIgnoreCase));
Show("IndexOf O start end", () => s.IndexOf("", s.Length, StringComparison.Ordinal));
Show("IndexOf O start bad", () => s.IndexOf("o", s.Length + 1, StringComparison.Ordinal));
Show("IndexOf var start bad", () => s.IndexOf("o", s.Length + 1, variable));
Show("IndexOf O start neg", () => s.IndexOf("o", -1, StringComparison.OrdinalIgnoreCase));
Show("IndexOf O count", () => s.IndexOf("o", 5, 10, StringComparison.Ordinal));
Show("IndexOf OIC count", () => s.IndexOf("WORLD", 14, 13, StringComparison.OrdinalIgnoreCase));
Show("IndexOf O count bad", () => s.IndexOf("o", 5, 100, StringComparison.Ordinal));
Show("IndexOf var count bad", () => s.IndexOf("o", 5, 100, variable));
Show("IndexOf O count neg", () => s.IndexOf("o", 5, -1, StringComparison.OrdinalIgnoreCase));
Show("IndexOf null", () => s.IndexOf(nothing!, StringComparison.Ordinal));
Show("IndexOf null start", () => s.IndexOf(nothing!, 100, StringComparison.OrdinalIgnoreCase));
Show("IndexOf null count", () => s.IndexOf(nothing!, 0, 1, StringComparison.Ordinal));
Show("IndexOf char O", () => s.IndexOf('w', StringComparison.Ordinal));
Show("IndexOf char OIC", () => s.IndexOf('w', StringComparison.OrdinalIgnoreCase));
Show("IndexOf char OIC 2", () => s.IndexOf('S', StringComparison.OrdinalIgnoreCase));
Show("Contains O", () => s.Contains("World", StringComparison.Ordinal));
Show("Contains OIC", () => s.Contains("world!", StringComparison.OrdinalIgnoreCase));
Show("Contains var", () => s.Contains("world!", variable));
Show("Contains null", () => s.Contains(nothing!, StringComparison.OrdinalIgnoreCase));
Show("Equals O", () => string.Equals(s, "hello", StringComparison.Ordinal));
Show("Equals OIC", () => string.Equals("ı", "I", StringComparison.OrdinalIgnoreCase));
Show("Equals OIC null", () => string.Equals(nothing, null, StringComparison.OrdinalIgnoreCase));
Show("Equals inst OIC", () => "ABC".Equals("abc", StringComparison.OrdinalIgnoreCase));
Show("Equals inst O", () => "ABC".Equals("abc", StringComparison.Ordinal));
Show("Compare O", () => Math.Sign(string.Compare("a", "B", StringComparison.Ordinal)));
Show("Compare OIC", () => Math.Sign(string.Compare("a", "B", StringComparison.OrdinalIgnoreCase)));
Show("Compare OIC eq", () => string.Compare("ſ", "S", StringComparison.OrdinalIgnoreCase));
Show("Compare null", () => Math.Sign(string.Compare(null, "x", StringComparison.Ordinal)));
Show("Equals OIC ascii", () => string.Equals("Hello-World@1", "hELLO-wORLD@1", StringComparison.OrdinalIgnoreCase));
Show("Equals OIC ascii sym", () => string.Equals("a@[", "A`{", StringComparison.OrdinalIgnoreCase));
Show("Equals OIC ascii vs unicode", () => string.Equals("\u0130", "i", StringComparison.OrdinalIgnoreCase) || "k".Equals("\u212A", StringComparison.OrdinalIgnoreCase));
Show("Equals OIC ascii null", () => string.Equals(nothing, "abc", StringComparison.OrdinalIgnoreCase) || "abc".Equals(nothing, StringComparison.OrdinalIgnoreCase));
Show("Equals OIC ascii len", () => "abc".Equals("ABCD", StringComparison.OrdinalIgnoreCase));
Show("Equals receiver null", () => nothing!.Equals("abc", StringComparison.OrdinalIgnoreCase));
Show("Equals receiver null O", () => nothing!.Equals("abc", StringComparison.Ordinal));
Show("Equals receiver null var", () => nothing!.Equals("abc", variable));
Show("receiver null", () => nothing!.StartsWith("x", StringComparison.Ordinal));
Show("receiver null IndexOf", () => nothing!.IndexOf("x", 0, StringComparison.OrdinalIgnoreCase));
