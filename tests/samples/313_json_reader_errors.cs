// Malformed JSON: every reader error with .NET's message, line number and byte position, through
// Utf8JsonReader (JsonReaderException) and JsonDocument.Parse.
using System.Text;
using System.Text.Json;

string[] inputs =
{
    "",
    "   ",
    "{",
    "[",
    "]",
    "}",
    "{]",
    "[}",
    "[1,]",
    "{\"a\":1,}",
    "[1 2]",
    "{\"a\" 1}",
    "{\"a\":}",
    "{a:1}",
    "{'a':1}",
    "[01]",
    "[-]",
    "[-a]",
    "[1.]",
    "[1.e5]",
    "[1e]",
    "[1e+]",
    "[1x]",
    "[1.5x]",
    "[1e5x]",
    "[tru]",
    "[trux]",
    "[fals]",
    "[nul]",
    "[nulll]",
    "[True]",
    "[NaN]",
    "[\"abc]",
    "[\"a\\qb\"]",
    "[\"a\\u12g4\"]",
    "[\"a\\u12\"]",
    "[\"tab\there\"]",
    "[\"new\nline\"]",
    "1 2",
    "{} {}",
    "[1] x",
    "{\"a\":1} // trailing comment",
    "/* comment */ 1",
    "[1, /* unterminated",
    "[1 /x]",
    "{\"a\":[1,2,{\"b\":[}]}",
    "\n\n  [1,\n   2,\n   ?]",
    "[\r\n1,\r\nx]",
    "{\"key\": \"value\"\n\n, \"k2\": tru }",
    "[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]",
    "{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":1}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}",
    "\u00e9",
    "[\"\u00e9\" ,, 1]",
};

foreach (string json in inputs)
{
    string shown = json.Length > 40 ? json.Substring(0, 37) + "..." : json;
    shown = shown.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    try
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        int tokens = 0;
        while (reader.Read()) tokens++;
        Console.WriteLine($"[{shown}] ok, {tokens} tokens");
    }
    catch (JsonException e)
    {
        Console.WriteLine($"[{shown}] {e.GetType().Name}: {e.Message} ({e.LineNumber}, {e.BytePositionInLine}, path '{e.Path}')");
    }
    try
    {
        using var doc = JsonDocument.Parse(json);
        Console.WriteLine("   document: " + doc.RootElement.ValueKind);
    }
    catch (JsonException e)
    {
        Console.WriteLine("   document: " + e.Message);
    }
}

// Comment handling and trailing commas change which inputs are errors.
var lenient = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
foreach (string json in new[] { "[1, /* c */ 2,]", "{\"a\":1, // c\n}", "/* only */", "[1 /* c */ 2]", "{,}" })
{
    try
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), lenient);
        var sb = new StringBuilder();
        while (reader.Read()) sb.Append(reader.TokenType).Append(' ');
        Console.WriteLine(json.Replace("\n", "\\n") + " -> " + sb);
    }
    catch (JsonException e)
    {
        Console.WriteLine(json.Replace("\n", "\\n") + " -> " + e.Message);
    }
}

// MaxDepth.
try
{
    var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("[[[1]]]"), new JsonReaderOptions { MaxDepth = 2 });
    while (reader.Read()) { }
}
catch (JsonException e)
{
    Console.WriteLine(e.Message);
}
try
{
    JsonDocument.Parse("{\"a\":{\"b\":1}}", new JsonDocumentOptions { MaxDepth = 1 });
}
catch (JsonException e)
{
    Console.WriteLine(e.Message);
}
try
{
    JsonDocument.Parse("[1]", new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Allow });
}
catch (ArgumentException e)
{
    Console.WriteLine(e.GetType().Name + ": " + e.Message);
}
