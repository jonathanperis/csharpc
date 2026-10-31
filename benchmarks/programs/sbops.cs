// StringBuilder operations beyond appending (strbuild.cs covers bulk appends): many small builders
// (the common case: a few appends then ToString), a large builder's ToString, Insert, Remove, Replace,
// the indexer, AppendFormat and interpolation.
using System.Text;

long checksum = 0;

// Many short-lived small builders.
for (int i = 0; i < 400_000; i++)
{
    var small = new StringBuilder();
    small.Append("key").Append(i).Append('=').Append(i * 7).Append(';');
    checksum += small.ToString().Length;
}

// One large builder, appended in mixed pieces, then copied out repeatedly.
var large = new StringBuilder();
for (int i = 0; i < 200_000; i++) large.Append("line ").Append(i).Append('\n');
for (int i = 0; i < 20; i++) checksum += large.ToString().Length;

// Indexer reads over the large builder.
long sum = 0;
for (int r = 0; r < 5; r++)
    for (int i = 0; i < large.Length; i += 7) sum += large[i];
checksum += sum;

// Insert and Remove near the front and in the middle of a medium builder.
var medium = new StringBuilder();
for (int i = 0; i < 2_000; i++) medium.Append("abcdefgh");
for (int i = 0; i < 20_000; i++)
{
    medium.Insert(i % 3 == 0 ? 0 : medium.Length / 2, "xy");
    medium.Remove(i % 2 == 0 ? 0 : medium.Length / 3, 2);
}
checksum += medium.Length + medium[medium.Length / 2];

// Replace (same length, longer, shorter) over a medium text.
var text = new StringBuilder();
for (int i = 0; i < 5_000; i++) text.Append("the quick brown fox ");
for (int i = 0; i < 40; i++)
{
    text.Replace("quick", "QUICK");
    text.Replace("fox", "foxes");
    text.Replace("foxes", "fox");
    text.Replace('o', '0').Replace('0', 'o');
}
checksum += text.Length + text.ToString().GetHashCode() % 1;

// AppendFormat and interpolation into a builder.
var formatted = new StringBuilder();
for (int i = 0; i < 100_000; i++)
{
    formatted.AppendFormat("{0}:{1,6}|", i, i % 97);
    formatted.Append($"[{i,-4}{i % 13:D2}]");
    if (formatted.Length > 10_000) { checksum += formatted.Length; formatted.Clear(); }
}
checksum += formatted.Length;

Console.WriteLine(checksum);
Console.WriteLine(medium.ToString(0, 40));
Console.WriteLine(text.ToString(0, 40));
