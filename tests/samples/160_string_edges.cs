// String API edge cases and exact exception messages (string ops implemented in the C# corelib).
string s = "Hello, World";
Try(() => s.Substring(13));
Try(() => s.Substring(-1));
Try(() => s.Substring(5, 20));
Try(() => s.Insert(99, "x"));
Try(() => s.Insert(0, null!));
Try(() => s.Remove(-1));
Try(() => s.Remove(12));
Try(() => s.Remove(3, -1));
Try(() => s.Remove(10, 5));
Try(() => s.IndexOf('o', 13).ToString());
Try(() => s.IndexOf("o", 99).ToString());
Try(() => s.IndexOf((string)null!).ToString());
Try(() => s.Replace("", "x"));
Try(() => s.Replace(null!, "x"));
Try(() => s.PadLeft(-1));
Try(() => s.IndexOfAny(null!).ToString());
Try(() => new string('x', -2));
Try(() => s.Contains((string)null!).ToString());
Try(() => s.StartsWith((string)null!).ToString());
Console.WriteLine(ReferenceEquals(s, s.Substring(0)) + " " + ReferenceEquals(s, s.Substring(0, s.Length)) + " " + ReferenceEquals(s, s.Trim()));
Console.WriteLine(ReferenceEquals(s, s.Replace('z', 'q')) + " " + ReferenceEquals(s, s.Replace("zz", "q")) + " " + ReferenceEquals(s, s.PadLeft(3)));
Console.WriteLine(s.IndexOf("", 4) + " " + s.IndexOf("", 12) + " " + s.IndexOf('o', 12) + " " + "".IndexOf(""));
Console.WriteLine(string.Join("|", "a,,b,".Split(',')) + " " + string.Join("|", " a , ,b ".Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
Console.WriteLine("ÀÉÎõüÿ straße".ToUpper() + " " + "ÀÉÎÕÜŸ".ToLower() + " " + char.ToUpper('ÿ') + char.ToLower('Ÿ'));
Console.WriteLine($"{char.IsLetter('ª')} {char.IsDigit('٣')} {char.IsWhiteSpace(' ')} {char.IsPunctuation('¿')} {char.IsUpper('Ā')} {char.IsLower('ā')} {char.IsLetter('Ω')}");
Console.WriteLine("xxhixx".Trim('x') + " " + "--a--".TrimStart('-') + " " + "ab;,".TrimEnd(';', ',') + " " + "  t  ".Trim(Array.Empty<char>()) + "|");
Console.WriteLine(string.CompareOrdinal("abc", "abd") + " " + string.CompareOrdinal(null, "a") + " " + string.CompareOrdinal("b", "a"));

static void Try(Func<object> f)
{
    try { Console.WriteLine(f()); }
    catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}
