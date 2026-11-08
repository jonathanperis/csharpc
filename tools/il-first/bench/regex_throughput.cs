// Regex throughput for the IL-first A/B (tools/il-first/ab.sh): a few typical patterns over a 60 KB text, repeated.
// stdout is a deterministic checksum (compared with real .NET); the elapsed time goes to stderr.
using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

var builder = new StringBuilder();
for (int i = 0; i < 1200; i++)
    builder.Append("user").Append(i).Append("@example").Append(i % 7).Append(".com id=").Append(i * 37).Append(" date=2024-0").Append(1 + i % 9).Append("-1").Append(i % 10).Append(" key").Append(i % 13).Append("=value").Append(i).Append('\n');
string text = builder.ToString();

var email = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");
var date = new Regex(@"(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})");
var pair = new Regex(@"\b(\w+)=(\w+)\b", RegexOptions.Multiline);
var words = new Regex(@"\b[a-z]+\d*\b", RegexOptions.IgnoreCase);
var alternation = new Regex(@"\b(?:user1\d*|example3|key7|value99\d*)\b");

var clock = Stopwatch.StartNew();
long checksum = 0;
for (int round = 0; round < 5; round++)
{
    checksum += email.Matches(text).Count;
    foreach (Match m in date.Matches(text)) checksum += int.Parse(m.Groups["m"].Value) + m.Index % 7;
    foreach (Match m in pair.Matches(text)) checksum += m.Groups[2].Length;
    checksum += words.Matches(text).Count;
    checksum += alternation.Matches(text).Count;
    checksum += pair.Replace(text, "$2=$1").Length;
    checksum += date.Split(text).Length;
}
Console.WriteLine(checksum);
Console.Error.WriteLine($"elapsed {clock.ElapsedMilliseconds} ms");
