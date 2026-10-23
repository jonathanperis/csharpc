// System.Text.Json.Utf8JsonReader: token stream, positions, typed getters, comments, trailing
// commas, multiple top-level values, Skip/TrySkip and partial input with JsonReaderState.
using System.Text;
using System.Text.Json;

static void Dump(string json, JsonReaderOptions options = default)
{
    var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), options);
    var sb = new StringBuilder();
    while (reader.Read())
    {
        sb.Append(reader.TokenType).Append('@').Append(reader.TokenStartIndex).Append('/').Append(reader.CurrentDepth);
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
            case JsonTokenType.String:
                sb.Append('=').Append(reader.GetString()).Append(reader.ValueIsEscaped ? "(esc)" : "");
                break;
            case JsonTokenType.Number:
                sb.Append('=').Append(Encoding.UTF8.GetString(reader.ValueSpan));
                break;
            case JsonTokenType.Comment:
                sb.Append('=').Append(reader.GetComment());
                break;
        }
        sb.Append(' ');
    }
    Console.WriteLine(sb.ToString() + "| consumed " + reader.BytesConsumed);
}

Dump("{\"a\": [1, -2.5e3, true, false, null, \"s\\n\\u00e9\\ud83d\\ude00\"], \"b\": {\"c\": {}}, \"d\": []}");
Dump("  \r\n\t42  ");
Dump("\"just a string\"");
Dump("[1, /* inline */ 2 // line\n, 3]", new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow });
Dump("[1, /* inline */ 2 // line\n, 3]", new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
Dump("{\"a\":1,\"b\":[1,2,],}", new JsonReaderOptions { AllowTrailingCommas = true });
Dump("1 [2] {\"x\":3} \"y\"", new JsonReaderOptions { AllowMultipleValues = true });

// Typed getters.
var numbers = Encoding.UTF8.GetBytes("[0, -1, 2147483647, 2147483648, 4294967295, 9223372036854775807, 18446744073709551615, 1.5, 1e2, 1e400, -0, 0.1, 123456789012345678901234567890]");
var r = new Utf8JsonReader(numbers);
r.Read();
while (r.Read() && r.TokenType != JsonTokenType.EndArray)
{
    string text = Encoding.UTF8.GetString(r.ValueSpan);
    Console.WriteLine($"{text,-32} int:{(r.TryGetInt32(out int i) ? i.ToString() : "-")} uint:{(r.TryGetUInt32(out uint u) ? u.ToString() : "-")} long:{(r.TryGetInt64(out long l) ? l.ToString() : "-")} ulong:{(r.TryGetUInt64(out ulong ul) ? ul.ToString() : "-")} double:{(r.TryGetDouble(out double d) ? d.ToString() : "-")} decimal:{(r.TryGetDecimal(out decimal m) ? m.ToString() : "-")} byte:{(r.TryGetByte(out byte b) ? b.ToString() : "-")} short:{(r.TryGetInt16(out short sh) ? sh.ToString() : "-")}");
}

var dates = Encoding.UTF8.GetBytes("[\"2019-07-26T00:00:00\", \"2019-07-26T10:20:30.1234567Z\", \"2019-07-26\", \"2019-07-26T10:20\", \"2019/07/26\", \"2019-02-30T00:00:00\", \"2019-07-26T24:00:00\", \"2019-07-26T10:20:30.5+02:00\", \"x\", \"2019-07-26T10:20:30\\u002E5\"]");
r = new Utf8JsonReader(dates);
r.Read();
while (r.Read() && r.TokenType != JsonTokenType.EndArray)
{
    bool ok = r.TryGetDateTime(out DateTime dt);
    Console.WriteLine($"{r.GetString(),-32} {ok} {(dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt):O} {dt.Kind}");
}

r = new Utf8JsonReader(Encoding.UTF8.GetBytes("[\"AQID+vv8\", \"not base64!\", \"\"]"));
r.Read();
while (r.Read() && r.TokenType != JsonTokenType.EndArray)
    Console.WriteLine(r.TryGetBytesFromBase64(out byte[] bytes) ? string.Join(",", bytes) : "invalid");

// Comparisons and copies.
r = new Utf8JsonReader(Encoding.UTF8.GetBytes("{\"na\\u006De\": \"value\"}"));
r.Read();
r.Read();
Console.WriteLine(r.ValueTextEquals("name") + " " + r.ValueTextEquals("Name") + " " + r.ValueTextEquals("name"u8) + " " + r.ValueIsEscaped);
var dest = new char[10];
int written = r.CopyString(dest);
Console.WriteLine(new string(dest, 0, written));

// Skip and TrySkip.
r = new Utf8JsonReader(Encoding.UTF8.GetBytes("{\"skip\": {\"deep\": [1, [2, 3]]}, \"keep\": 4}"));
r.Read();
r.Read();
r.Skip();
Console.WriteLine(r.TokenType + " " + r.CurrentDepth);
r.Read();
Console.WriteLine(r.GetString());

// Partial input: isFinalBlock=false, then continue with the saved state.
byte[] part1 = Encoding.UTF8.GetBytes("{\"a\": [1, 2");
byte[] whole = Encoding.UTF8.GetBytes("{\"a\": [1, 2, 3]}");
var partial = new Utf8JsonReader(part1, isFinalBlock: false, state: default);
while (partial.Read()) Console.Write(partial.TokenType + " ");
Console.WriteLine("| consumed " + partial.BytesConsumed + " skip=" + partial.TrySkip());
var rest = new Utf8JsonReader(whole.AsSpan((int)partial.BytesConsumed), isFinalBlock: true, partial.CurrentState);
while (rest.Read()) Console.Write(rest.TokenType + " ");
Console.WriteLine();

// Wrong-type getters throw InvalidOperationException; out-of-range throws FormatException.
r = new Utf8JsonReader(Encoding.UTF8.GetBytes("[\"text\", 300, 1.5]"));
r.Read();
r.Read();
try { r.GetInt32(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { r.GetBoolean(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
r.Read();
try { r.GetString(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { r.GetByte(); } catch (FormatException e) { Console.WriteLine(e.Message); }
r.Read();
try { r.GetInt64(); } catch (FormatException e) { Console.WriteLine(e.Message); }
try { r.GetDateTime(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { r.ValueTextEquals("x"); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
r = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"2019/07/26\""));
r.Read();
try { r.GetDateTime(); } catch (FormatException e) { Console.WriteLine(e.Message); }
r = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"\\uD800x\""));
r.Read();
try { r.GetString(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
r = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"\\uDC00\""));
r.Read();
try { r.GetString(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
r = new Utf8JsonReader(new byte[] { 0x22, 0xFF, 0x22 });
r.Read();
try { r.GetString(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { _ = new JsonReaderOptions { MaxDepth = -1 }; } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { _ = new JsonReaderOptions { CommentHandling = (JsonCommentHandling)5 }; } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
