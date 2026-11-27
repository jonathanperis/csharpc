// StringBuilder throughput, the program behind the NativeAOT column of docs/il-first/System.Text.StringBuilder.md
// (tools/il-first/measure-aot.sh): Append of strings, chars and numbers, AppendFormat, Insert, Replace, ToString. The output is checksums only.
using System;
using System.Text;

long check = 0;
for (int round = 0; round < 40; round++)
{
    var sb = new StringBuilder();
    for (int i = 0; i < 200_000; i++)
    {
        sb.Append("item").Append(i).Append(',').Append(i * 0.5);
        if (i % 1000 == 0) sb.AppendFormat("[{0}:{1,6}]", i, round);
    }
    sb.Insert(0, "head:").Replace(",", ";", 0, 10_000);
    var s = sb.ToString();
    check += s.Length + s[s.Length / 2];
    sb.Clear();
    for (int i = 0; i < 100_000; i++) sb.Append((char)('a' + i % 26));
    check += sb.ToString().GetHashCode() == 0 ? 1 : sb.Length;
}
Console.WriteLine(check);
