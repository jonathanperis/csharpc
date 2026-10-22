// Dictionary<string,int> word counting + List<T>.Sort with a Comparison (corelib collections).
var words = new string[5000];
uint seed = 42;
for (int i = 0; i < words.Length; i++)
{
    seed = seed * 1103515245 + 12345;
    int len = 3 + (int)((seed >> 16) % 6);
    var chars = new char[len];
    for (int j = 0; j < len; j++)
    {
        seed = seed * 1103515245 + 12345;
        chars[j] = (char)('a' + (seed >> 16) % 6);
    }
    words[i] = new string(chars);
}

var counts = new Dictionary<string, int>();
for (int round = 0; round < 400; round++)
{
    for (int i = 0; i < words.Length; i++)
    {
        string w = words[(i * 7 + round) % words.Length];
        counts[w] = counts.TryGetValue(w, out int n) ? n + 1 : 1;
    }
}

var entries = new List<KeyValuePair<string, int>>(counts);
entries.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
for (int i = 0; i < 5; i++) Console.WriteLine($"{entries[i].Key} {entries[i].Value}");
long checksum = 0;
foreach (var e in entries) checksum = checksum * 31 + e.Value + e.Key.Length;
Console.WriteLine($"distinct={counts.Count} checksum={checksum}");
