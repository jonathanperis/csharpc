// char.GetUnicodeCategory (and the predicates built on it) for every UTF-16 code unit, summarized per
// 4096-char block and per category: csharpc's packed category table must give .NET's data exactly.
using System.Globalization;

var counts = new int[30];
for (int block = 0; block < 16; block++)
{
    uint hash = 2166136261;
    for (int c = block * 4096; c < (block + 1) * 4096; c++)
    {
        var category = char.GetUnicodeCategory((char)c);
        counts[(int)category]++;
        int flags = (char.IsLetter((char)c) ? 1 : 0) | (char.IsDigit((char)c) ? 2 : 0) | (char.IsWhiteSpace((char)c) ? 4 : 0)
            | (char.IsPunctuation((char)c) ? 8 : 0) | (char.IsSymbol((char)c) ? 16 : 0) | (char.IsLetterOrDigit((char)c) ? 32 : 0);
        hash = (hash ^ (uint)((int)category | flags << 5)) * 16777619;
    }
    Console.WriteLine($"U+{block * 4096:X4}: {hash:X8}");
}
for (int i = 0; i < counts.Length; i++) Console.WriteLine($"{(UnicodeCategory)i}: {counts[i]}");
