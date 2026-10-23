// JSON string escaping: the default JavaScriptEncoder (HTML-sensitive and non-ASCII characters as
// \uXXXX), UnsafeRelaxedJsonEscaping, custom Unicode ranges, invalid UTF-16/UTF-8 input, property
// names and JsonEncodedText.
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

static string Write(JavaScriptEncoder encoder, Action<Utf8JsonWriter> body)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = encoder }))
    {
        body(writer);
    }
    return Encoding.UTF8.GetString(stream.ToArray());
}

var ascii = new StringBuilder();
for (int c = 0; c < 128; c++) ascii.Append((char)c);
string all = ascii.ToString();

var samples = new[]
{
    all,
    "café ñ ü ß Ж я € ‰ ∑ ≤ 中文 日本語 한국어",
    "emoji 😀 🎉 and \U0001F600 pair",
    "lone high \uD800 end",
    "lone low \uDC00 end",
    "reversed \uDC00\uD800 pair",
    "line\u2028para\u2029 soft\u00ADhyphen nbsp\u00A0 bom\uFEFF",
    "<script>alert('x' + \"y\") & `z`</script>",
    "",
};

var encoders = new (string Name, JavaScriptEncoder Encoder)[]
{
    ("null", null),
    ("Default", JavaScriptEncoder.Default),
    ("Relaxed", JavaScriptEncoder.UnsafeRelaxedJsonEscaping),
    ("Latin+Cyrillic", JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.Cyrillic)),
    ("All", JavaScriptEncoder.Create(UnicodeRanges.All)),
};

foreach (var (name, encoder) in encoders)
{
    Console.WriteLine("== " + name);
    foreach (string s in samples)
    {
        Console.WriteLine(Write(encoder, w => w.WriteStringValue(s)));
        Console.WriteLine(Write(encoder, w => { w.WriteStartObject(); w.WriteString(s, 1.ToString()); w.WriteEndObject(); }));
    }
}

// Custom settings: allow and forbid individual characters.
var settings = new TextEncoderSettings();
settings.AllowRange(UnicodeRanges.BasicLatin);
settings.AllowCharacters('é', 'ü');
settings.ForbidCharacters('a', 'e');
var custom = JavaScriptEncoder.Create(settings);
Console.WriteLine(Write(custom, w => w.WriteStringValue("café über allé")));

// UTF-8 input, including invalid sequences (replaced by an escaped U+FFFD).
byte[][] utf8 =
{
    Encoding.UTF8.GetBytes("plain ascii"),
    Encoding.UTF8.GetBytes("é 中 😀"),
    new byte[] { 0x61, 0xFF, 0x62 },
    new byte[] { 0x61, 0xC3, 0x28, 0x62 },
    new byte[] { 0x61, 0xE2, 0x82 },
    new byte[] { 0xF0, 0x9F, 0x98 },
    new byte[] { 0xED, 0xA0, 0x80 },
    new byte[] { 0xC0, 0xAF },
};
foreach (var bytes in utf8)
{
    Console.WriteLine(Write(null, w => w.WriteStringValue(bytes)));
    Console.WriteLine(Write(JavaScriptEncoder.UnsafeRelaxedJsonEscaping, w => w.WriteStringValue(bytes)));
}

// JsonEncodedText: pre-escaped names and values.
var encoded = JsonEncodedText.Encode("<tag> é");
Console.WriteLine(encoded.ToString() + " | " + encoded.Value + " | " + encoded.EncodedUtf8Bytes.Length);
var relaxedText = JsonEncodedText.Encode("<tag> é", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
Console.WriteLine(relaxedText.ToString());
Console.WriteLine(encoded.Equals(JsonEncodedText.Encode("<tag> é")) + " " + encoded.Equals(relaxedText));
Console.WriteLine(Write(null, w =>
{
    w.WriteStartObject();
    w.WriteString(encoded, encoded);
    w.WritePropertyName(JsonEncodedText.Encode("x\ny"));
    w.WriteStringValue(JsonEncodedText.Encode(Encoding.UTF8.GetBytes("bytes é")));
    w.WriteEndObject();
}));

// TextEncoder APIs.
Console.WriteLine(JavaScriptEncoder.Default.Encode("a<b>c \"q\" é \uD800"));
Console.WriteLine(JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode("a<b>c \"q\" é \uD800"));
Console.WriteLine(JavaScriptEncoder.Default.WillEncode('a') + " " + JavaScriptEncoder.Default.WillEncode('<') + " " + JavaScriptEncoder.UnsafeRelaxedJsonEscaping.WillEncode('é'));
Console.WriteLine(UnicodeRanges.Cyrillic.FirstCodePoint + " " + UnicodeRanges.Cyrillic.Length + " " + UnicodeRange.Create('a', 'z').Length);
