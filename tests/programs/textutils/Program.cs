// Text processing toolkit: word frequencies, Levenshtein distance & spelling suggestions, a CSV parser
// (quotes, escaped quotes, embedded commas/newlines), Span<char>-based tokenizing, wrapping & justification.
using System.Text;

const string text = """
    It was the best of times, it was the worst of times, it was the age of wisdom,
    it was the age of foolishness, it was the epoch of belief, it was the epoch of
    incredulity, it was the season of Light, it was the season of Darkness, it was the
    spring of hope, it was the winter of despair.
    """;

var frequencies = WordStats.Frequencies(text);
Console.WriteLine($"{frequencies.Values.Sum()} words, {frequencies.Count} distinct");
foreach (var (word, count) in frequencies.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(8))
    Console.WriteLine($"  {word,-12}{count,3} {new string('*', count)}");
Console.WriteLine($"longest: {string.Join(", ", frequencies.Keys.OrderByDescending(w => w.Length).ThenBy(w => w).Take(3))}");
Console.WriteLine($"average word length: {frequencies.Sum(kv => kv.Key.Length * kv.Value) / (double)frequencies.Values.Sum():F3}");

var dictionary = frequencies.Keys.ToList();
foreach (var typo in new[] { "wisdon", "seasn", "despare", "times", "zebra" })
{
    var suggestions = Spelling.Suggest(typo, dictionary, maxDistance: 2);
    Console.WriteLine($"{typo,-8} -> {(suggestions.Count == 0 ? "(none)" : string.Join(", ", suggestions.Select(s => $"{s.Word}({s.Distance})")))}");
}
Console.WriteLine($"levenshtein(kitten, sitting) = {Spelling.Levenshtein("kitten", "sitting")}");

const string csv = "id,name,city,notes\n1,Ada,London,\"likes \"\"engines\"\"\"\n2,\"Hopper, Grace\",Arlington,\"multi\nline\"\n3,Alan,,\n4,\"Edsger\",Nuenen,\"a,b,c\"\n";
var rows = Csv.Parse(csv);
Console.WriteLine($"csv: {rows.Count} rows x {rows[0].Length} columns");
foreach (var row in rows.Skip(1)) Console.WriteLine("  " + string.Join(" | ", row.Select(f => f.Replace("\n", "\\n"))));
Console.WriteLine(Csv.Write(rows.Take(3).ToList()).Replace("\n", "\\n"));

var expr = "sum(3, 4.5) * (max(1,2) - -7) / 2";
Console.WriteLine($"tokens: {string.Join(" ", Tokenizer.Tokenize(expr))}");

var paragraph = string.Join(' ', text.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries));
foreach (var line in Layout.Justify(paragraph, 40)) Console.WriteLine($"|{line}|");
Console.WriteLine(Layout.Center("csharpc", 21, '~'));
Console.WriteLine($"title case: {Layout.TitleCase("the quick brown FOX jumps")}");
Console.WriteLine($"is palindrome: {Layout.IsPalindrome("A man, a plan, a canal: Panama")} {Layout.IsPalindrome("csharp")}");

static class WordStats
{
    public static Dictionary<string, int> Frequencies(string text)
    {
        var counts = new Dictionary<string, int>();
        var span = text.AsSpan();
        int start = -1;
        for (int i = 0; i <= span.Length; i++)
        {
            bool letter = i < span.Length && (char.IsLetter(span[i]) || span[i] == '\'');
            if (letter && start < 0) start = i;
            else if (!letter && start >= 0)
            {
                var word = span[start..i].ToString().ToLowerInvariant();
                counts[word] = counts.GetValueOrDefault(word) + 1;
                start = -1;
            }
        }
        return counts;
    }
}

static class Spelling
{
    public static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    public static List<(string Word, int Distance)> Suggest(string word, IEnumerable<string> dictionary, int maxDistance) =>
        dictionary.Select(w => (Word: w, Distance: Levenshtein(word, w)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance).ThenBy(x => x.Word, StringComparer.Ordinal)
            .Take(3).ToList();
}

static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                fields.Add(field.ToString()); field.Clear();
                rows.Add(fields.ToArray()); fields.Clear();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); rows.Add(fields.ToArray()); }
        return rows;
    }

    public static string Write(List<string[]> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendJoin(',', row.Select(Quote));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Quote(string field) =>
        field.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
}

static class Tokenizer
{
    public static IEnumerable<string> Tokenize(string input)
    {
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            int start = i;
            if (char.IsDigit(c) || (c == '.' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
            {
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.')) i++;
                yield return $"NUM:{input[start..i]}";
            }
            else if (char.IsLetter(c))
            {
                while (i < input.Length && char.IsLetterOrDigit(input[i])) i++;
                yield return $"ID:{input[start..i]}";
            }
            else { i++; yield return $"'{c}'"; }
        }
    }
}

static class Layout
{
    public static IEnumerable<string> Justify(string text, int width)
    {
        var words = text.Split(' ');
        int index = 0;
        while (index < words.Length)
        {
            var line = new List<string> { words[index++] };
            int length = line[0].Length;
            while (index < words.Length && length + 1 + words[index].Length <= width) { length += 1 + words[index].Length; line.Add(words[index++]); }
            if (index == words.Length || line.Count == 1) { yield return string.Join(' ', line).PadRight(width); continue; }
            int gaps = line.Count - 1, spaces = width - line.Sum(w => w.Length);
            var sb = new StringBuilder();
            for (int g = 0; g < line.Count; g++)
            {
                sb.Append(line[g]);
                if (g < gaps) sb.Append(' ', spaces / gaps + (g < spaces % gaps ? 1 : 0));
            }
            yield return sb.ToString();
        }
    }

    public static string Center(string s, int width, char fill)
    {
        int total = width - s.Length, left = total / 2;
        return new string(fill, left) + s + new string(fill, total - left);
    }

    public static string TitleCase(string s) =>
        string.Join(' ', s.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    public static bool IsPalindrome(string s)
    {
        var letters = s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return letters.AsSpan().SequenceEqual(letters.Reverse().ToArray());
    }
}
