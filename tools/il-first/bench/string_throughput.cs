// String helpers throughput, the program behind the NativeAOT column of docs/il-first/System.String.md (tools/il-first/measure-aot.sh):
// searching, splitting, trimming, replacing, padding, joining, ordinal comparison and char classification on string. Checksums only.
using System;

var lines = new string[2000];
for (int i = 0; i < lines.Length; i++) lines[i] = "  Item " + i + ", größe=" + (i * 7 % 1000) + "; naïve tag:" + (i % 13) + " Ünïcödé 日本  ";
long check = 0;
for (int round = 0; round < 60; round++)
{
    foreach (var line in lines)
    {
        var t = line.Trim();
        check += t.IndexOf("tag:", StringComparison.Ordinal) + t.LastIndexOf(' ') + t.IndexOfAny([';', '=']);
        var parts = t.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        check += parts.Length + parts[^1].Length;
        foreach (var w in line.Split()) check += w.Length;
        var r = t.Replace("Item", "Entry").Replace('a', 'A');
        check += r.Length + (r.StartsWith("Entry", StringComparison.Ordinal) ? 1 : 0) + (r.Contains("größe") ? 2 : 0);
        check += string.CompareOrdinal(t, r) & 15;
        check += t.ToUpperInvariant()[3] & 7;
        check += t.PadLeft(70, '.').Length + t.Substring(5, 10).Length;
        foreach (var c in t) if (char.IsLetterOrDigit(c)) check++; else if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) check += 2;
        check += string.IsNullOrWhiteSpace(line) ? 0 : 3;
    }
    check += string.Join("|", lines[..100]).Length + string.Concat(lines[1], lines[2], lines[3]).Length;
}
Console.WriteLine(check);
