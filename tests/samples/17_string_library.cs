// System.String / Char / StringBuilder / parsing, implemented in runtime/strings.c.
using System.Text;

string text = "  The quick brown fox jumps over the lazy dog  ";
string trimmed = text.Trim();
Console.WriteLine($"[{trimmed}]");
Console.WriteLine($"[{text.TrimStart()}] [{text.TrimEnd()}]");
Console.WriteLine(trimmed.ToUpper());
Console.WriteLine(trimmed.ToLower());
Console.WriteLine(trimmed.Substring(4, 5));
Console.WriteLine(trimmed.Substring(40));
Console.WriteLine(trimmed.IndexOf('q') + " " + trimmed.IndexOf("fox") + " " + trimmed.IndexOf("cat") + " " + trimmed.LastIndexOf('o'));
Console.WriteLine(trimmed.Contains("lazy") + " " + trimmed.Contains('z') + " " + trimmed.StartsWith("The") + " " + trimmed.EndsWith("cat"));
Console.WriteLine(trimmed.Replace("dog", "cat").Replace('o', '0'));
Console.WriteLine("abc".PadLeft(6) + "|" + "abc".PadRight(6) + "|" + "7".PadLeft(3, '0'));

string[] words = trimmed.Split(' ');
Console.WriteLine(words.Length);
Console.WriteLine(string.Join(", ", words));
string[] parts = "a,,b, c ,".Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
Console.WriteLine(string.Join("|", parts) + " (" + parts.Length + ")");

char[] chars = "hello".ToCharArray();
chars[0] = 'j';
Console.WriteLine(new string(chars));
Console.WriteLine(new string('=', 20));

int digits = 0, letters = 0, spaces = 0, upper = 0;
foreach (char c in "Hello World 2026!")
{
    if (char.IsDigit(c)) digits++;
    if (char.IsLetter(c)) letters++;
    if (char.IsWhiteSpace(c)) spaces++;
    if (char.IsUpper(c)) upper++;
}
Console.WriteLine($"{digits} {letters} {spaces} {upper} {char.ToUpper('x')}{char.ToLower('Y')}");

Console.WriteLine(int.Parse("12345") + 1);
Console.WriteLine(long.Parse("-9000000000") * 2);
Console.WriteLine(double.Parse("3.25") * 2);
Console.WriteLine(int.TryParse("42", out int answer) + " " + answer);
Console.WriteLine(int.TryParse("forty-two", out int bad) + " " + bad);
Console.WriteLine(int.TryParse("99999999999", out _));

var sb = new StringBuilder();
for (int i = 0; i < 5; i++) sb.Append(i).Append(',');
sb.Append('!', 3).AppendLine().Append("done");
Console.WriteLine(sb.ToString());
Console.WriteLine(sb.Length);
Console.WriteLine(new StringBuilder("x").Append(1.5).Append(true).ToString());
Console.WriteLine(string.CompareOrdinal("apple", "banana") < 0);
Console.WriteLine("b".CompareTo("a"));
