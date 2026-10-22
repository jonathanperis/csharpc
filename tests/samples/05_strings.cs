// System.String: UTF-16, immutable, concatenation and interpolation.
string name = "LLVM";
string greeting = "Hello, " + name + "!";
Console.WriteLine(greeting);
Console.WriteLine(greeting.Length);
Console.WriteLine(greeting[7]);
Console.WriteLine($"{name} has {name.Length} chars; pi ~ {3.14159}; ok={true}; c={'x'}");
Console.WriteLine("a" + 1 + 2);
Console.WriteLine(1 + 2 + "a");
Console.WriteLine("long: " + 9_000_000_000L + ", uint: " + 4_000_000_000u + ", double: " + 0.5);

string s = "";
for (int i = 0; i < 5; i++) s += i.ToString();
Console.WriteLine(s);

Console.WriteLine("abc" == "ab" + "c");
Console.WriteLine("abc" != "abd");
string? nothing = null;
Console.WriteLine(nothing == null);
Console.WriteLine("[" + nothing + "]");
Console.WriteLine(string.IsNullOrEmpty(nothing));
Console.WriteLine(string.IsNullOrEmpty(""));
Console.WriteLine(string.IsNullOrEmpty("x"));
Console.WriteLine(string.Concat("con", "cat", "enate"));
Console.WriteLine("abc".Equals("abc"));

Console.WriteLine(Reverse("stressed"));
Console.WriteLine(IsPalindrome("racecar"));
Console.WriteLine(IsPalindrome("csharp"));
Console.WriteLine(CountVowels("The quick brown fox jumps over the lazy dog"));
Console.WriteLine("emoji: 🚀 length=" + "🚀".Length);

static string Reverse(string text)
{
    string result = "";
    for (int i = text.Length - 1; i >= 0; i--) result += text[i];
    return result;
}

static bool IsPalindrome(string text)
{
    for (int i = 0, j = text.Length - 1; i < j; i++, j--)
        if (text[i] != text[j]) return false;
    return true;
}

static int CountVowels(string text)
{
    int count = 0;
    for (int i = 0; i < text.Length; i++)
    {
        switch (text[i])
        {
            case 'a' or 'e' or 'i' or 'o' or 'u':
            case 'A' or 'E' or 'I' or 'O' or 'U':
                count++;
                break;
        }
    }
    return count;
}
