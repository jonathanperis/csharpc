// A JSON parser, DOM, query helpers and pretty printer.
using System.Globalization;
using System.Text;

const string document = """
{
  "store": "Corner Books",
  "open": true,
  "rating": 4.75,
  "founded": 1998,
  "tags": ["books", "coffee", "events"],
  "address": { "street": "12 Main St", "city": "Springfield", "zip": null },
  "inventory": [
    { "isbn": "978-0", "title": "The Pragmatic Programmer", "price": 39.99, "stock": 4 },
    { "isbn": "978-1", "title": "Structure and Interpretation", "price": 54.5, "stock": 0 },
    { "isbn": "978-2", "title": "Gödel, Escher, Bach", "price": 22.0, "stock": 11, "notes": "tab\there \"quoted\" é" }
  ],
  "empty": {},
  "nothing": [],
  "big": 12345678901234567890,
  "tiny": -1.5e-3
}
""";

var root = JsonParser.Parse(document);
Console.WriteLine(JsonWriter.Write(root, indent: true));
Console.WriteLine(JsonWriter.Write(root, indent: false).Length);

var inventory = root["inventory"].AsArray();
Console.WriteLine($"{inventory.Count} books, {inventory.Count(b => b["stock"].AsNumber() > 0)} in stock");
var value = inventory.Sum(b => b["price"].AsNumber() * b["stock"].AsNumber());
Console.WriteLine($"inventory value: {value:F2}");
var titles = inventory.OrderBy(b => b["price"].AsNumber()).Select(b => b["title"].AsString());
Console.WriteLine(string.Join(" | ", titles));
Console.WriteLine(root.Path("address.city")?.AsString());
Console.WriteLine(root.Path("inventory.2.notes")?.AsString());
Console.WriteLine(root.Path("address.zip") is JsonNull ? "zip is null" : "zip set");
Console.WriteLine(root.Path("nope.missing") is null ? "missing" : "present");

// Round trip: parse(write(x)) == x
var again = JsonParser.Parse(JsonWriter.Write(root, indent: false));
Console.WriteLine(JsonWriter.Write(again, false) == JsonWriter.Write(root, false) ? "round-trip ok" : "round-trip FAILED");

// Build a document in code
var report = new JsonObject();
report.Set("generated", new JsonString("2026-10-22"));
report.Set("count", new JsonNumber(inventory.Count));
report.Set("outOfStock", new JsonArray(inventory.Where(b => b["stock"].AsNumber() == 0).Select(b => b["isbn"]).ToList()));
Console.WriteLine(JsonWriter.Write(report, indent: true));

foreach (var bad in new[] { "{\"a\": }", "[1, 2", "{\"a\" 1}", "tru", "\"unterminated", "[1,,2]", "01x" })
{
    try { JsonParser.Parse(bad); Console.WriteLine($"{bad}: parsed?!"); }
    catch (JsonException e) { Console.WriteLine($"{bad,-16} => {e.Message}"); }
}

// ------------------------------------------------------------------ DOM
abstract class JsonValue
{
    public virtual JsonValue this[string key] => throw new InvalidOperationException($"{GetType().Name} has no properties");
    public virtual JsonValue this[int index] => throw new InvalidOperationException($"{GetType().Name} is not an array");
    public double AsNumber() => this is JsonNumber n ? n.Value : throw new InvalidOperationException("not a number");
    public string AsString() => this is JsonString s ? s.Value : throw new InvalidOperationException("not a string");
    public List<JsonValue> AsArray() => this is JsonArray a ? a.Items : throw new InvalidOperationException("not an array");

    public JsonValue? Path(string path)
    {
        JsonValue? current = this;
        foreach (var part in path.Split('.'))
        {
            current = current switch
            {
                JsonObject o => o.Properties.TryGetValue(part, out var v) ? v : null,
                JsonArray a when int.TryParse(part, out var i) && i < a.Items.Count => a.Items[i],
                _ => null,
            };
            if (current is null) return null;
        }
        return current;
    }
}

sealed class JsonNull : JsonValue { public static readonly JsonNull Instance = new(); }
sealed class JsonBool(bool value) : JsonValue { public bool Value { get; } = value; }
sealed class JsonNumber(double value) : JsonValue { public double Value { get; } = value; }
sealed class JsonString(string value) : JsonValue { public string Value { get; } = value; }

sealed class JsonArray(List<JsonValue> items) : JsonValue
{
    public List<JsonValue> Items { get; } = items;
    public override JsonValue this[int index] => Items[index];
}

sealed class JsonObject : JsonValue
{
    public Dictionary<string, JsonValue> Properties { get; } = new();
    public override JsonValue this[string key] =>
        Properties.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"no property '{key}'");
    public JsonValue Set(string key, JsonValue value) { Properties[key] = value; return value; }
}

class JsonException(string message, int position) : Exception($"{message} at position {position}");

// ------------------------------------------------------------------ parser
static class JsonParser
{
    public static JsonValue Parse(string text)
    {
        int pos = 0;
        var value = ParseValue(text, ref pos);
        SkipWhitespace(text, ref pos);
        if (pos != text.Length) throw new JsonException("trailing characters", pos);
        return value;
    }

    static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
    }

    static JsonValue ParseValue(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) throw new JsonException("unexpected end of input", pos);
        char c = s[pos];
        return c switch
        {
            '{' => ParseObject(s, ref pos),
            '[' => ParseArray(s, ref pos),
            '"' => new JsonString(ParseString(s, ref pos)),
            't' => Literal(s, ref pos, "true", new JsonBool(true)),
            'f' => Literal(s, ref pos, "false", new JsonBool(false)),
            'n' => Literal(s, ref pos, "null", JsonNull.Instance),
            _ when c == '-' || char.IsDigit(c) => ParseNumber(s, ref pos),
            _ => throw new JsonException($"unexpected character '{c}'", pos),
        };
    }

    static JsonValue Literal(string s, ref int pos, string word, JsonValue value)
    {
        if (pos + word.Length > s.Length || s.Substring(pos, word.Length) != word)
            throw new JsonException($"invalid literal (expected '{word}')", pos);
        pos += word.Length;
        return value;
    }

    static JsonObject ParseObject(string s, ref int pos)
    {
        var obj = new JsonObject();
        pos++;
        SkipWhitespace(s, ref pos);
        if (pos < s.Length && s[pos] == '}') { pos++; return obj; }
        while (true)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != '"') throw new JsonException("expected a property name", pos);
            var key = ParseString(s, ref pos);
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != ':') throw new JsonException("expected ':'", pos);
            pos++;
            obj.Set(key, ParseValue(s, ref pos));
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == ',') { pos++; continue; }
            if (pos < s.Length && s[pos] == '}') { pos++; return obj; }
            throw new JsonException("expected ',' or '}'", pos);
        }
    }

    static JsonArray ParseArray(string s, ref int pos)
    {
        var items = new List<JsonValue>();
        pos++;
        SkipWhitespace(s, ref pos);
        if (pos < s.Length && s[pos] == ']') { pos++; return new JsonArray(items); }
        while (true)
        {
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == ',') throw new JsonException("unexpected ','", pos);
            items.Add(ParseValue(s, ref pos));
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == ',') { pos++; continue; }
            if (pos < s.Length && s[pos] == ']') { pos++; return new JsonArray(items); }
            throw new JsonException("expected ',' or ']'", pos);
        }
    }

    static string ParseString(string s, ref int pos)
    {
        var sb = new StringBuilder();
        int start = pos++;
        while (pos < s.Length)
        {
            char c = s[pos++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (pos >= s.Length) break;
            char e = s[pos++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)Convert.ToInt32(s.Substring(pos, 4), 16));
                    pos += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
        throw new JsonException("unterminated string", start);
    }

    static JsonNumber ParseNumber(string s, ref int pos)
    {
        int start = pos;
        if (s[pos] == '-') pos++;
        while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] is '.' or 'e' or 'E' or '+' or '-')) pos++;
        var text = s[start..pos];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            throw new JsonException($"invalid number '{text}'", start);
        return new JsonNumber(d);
    }
}

// ------------------------------------------------------------------ writer
static class JsonWriter
{
    public static string Write(JsonValue value, bool indent)
    {
        var sb = new StringBuilder();
        Write(sb, value, indent, 0);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, JsonValue value, bool indent, int depth)
    {
        switch (value)
        {
            case JsonNull: sb.Append("null"); break;
            case JsonBool b: sb.Append(b.Value ? "true" : "false"); break;
            case JsonNumber n: sb.Append(n.Value.ToString("R", CultureInfo.InvariantCulture)); break;
            case JsonString s: WriteString(sb, s.Value); break;
            case JsonArray a:
                if (a.Items.Count == 0) { sb.Append("[]"); break; }
                sb.Append('[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, indent, depth + 1);
                    Write(sb, a.Items[i], indent, depth + 1);
                }
                NewLine(sb, indent, depth);
                sb.Append(']');
                break;
            case JsonObject o:
                if (o.Properties.Count == 0) { sb.Append("{}"); break; }
                sb.Append('{');
                bool first = true;
                foreach (var (key, v) in o.Properties)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent, depth + 1);
                    WriteString(sb, key);
                    sb.Append(indent ? ": " : ":");
                    Write(sb, v, indent, depth + 1);
                }
                NewLine(sb, indent, depth);
                sb.Append('}');
                break;
        }
    }

    static void NewLine(StringBuilder sb, bool indent, int depth)
    {
        if (!indent) return;
        sb.Append('\n');
        sb.Append(' ', depth * 2);
    }

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ' || c > '~') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
