// String building: StringBuilder, interpolation, concatenation and searching.
using System.Text;

var sb = new StringBuilder();
sb.Append('[');
for (int i = 0; i < 300_000; i++)
{
    if (i > 0) sb.Append(',');
    sb.Append("{\"id\":").Append(i).Append(",\"name\":\"item").Append(i % 1000).Append("\",\"ok\":").Append(i % 3 == 0).Append('}');
}
sb.Append(']');
string json = sb.ToString();
int count = 0, pos = 0;
while ((pos = json.IndexOf("\"ok\":true", pos)) >= 0) { count++; pos += 9; }
long letters = 0;
foreach (char c in json) if (char.IsLetter(c)) letters++;
string label = "";
for (int i = 0; i < 2000; i++) label = $"run {i}: {json.Length} chars, {count} ok";
Console.WriteLine(json.Length);
Console.WriteLine(count);
Console.WriteLine(letters);
Console.WriteLine(label);
Console.WriteLine(json.Substring(0, 60));
