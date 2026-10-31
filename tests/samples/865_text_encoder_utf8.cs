// The inbox encoders' UTF-8 members (HtmlEncoder with TechEmpower's Platform settings, the default HTML,
// JavaScript and URL encoders): EncodeUtf8 over allowed runs, escapes, non-ASCII scalars allowed or not,
// invalid and truncated UTF-8 (final block or not), and destinations too small at every length;
// FindFirstCharacterToEncodeUtf8 on the same inputs.
using System;
using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
settings.AllowCharacter('\u2014');
var encoders = new (string Name, TextEncoder Encoder)[]
{
    ("platform html", HtmlEncoder.Create(settings)),
    ("html", HtmlEncoder.Default),
    ("html all", HtmlEncoder.Create(UnicodeRanges.All)),
    ("js", JavaScriptEncoder.Default),
    ("js relaxed", JavaScriptEncoder.UnsafeRelaxedJsonEscaping),
    ("url", UrlEncoder.Default),
};
var inputs = new byte[][]
{
    "plain ascii text"u8.ToArray(),
    "<script>alert(\"x\" & 'y' + 1);</script>"u8.ToArray(),
    "Emacs \u2014 Tom; フレームワーク; é ü 中文 😀"u8.ToArray(),
    "tab\tnew\nline\r\\back\u0001\u007f/"u8.ToArray(),
    [0x61, 0xFF, 0x62, 0xC3, 0x28, 0x63, 0xED, 0xA0, 0x80, 0x64],
    [0x61, 0x62, 0xE2, 0x80],
    [0xF0, 0x9F, 0x98],
    [],
};

static string Show(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes).Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

foreach (var (name, encoder) in encoders)
{
    Console.WriteLine($"== {name}");
    foreach (var input in inputs)
    {
        var output = new byte[input.Length * 12 + 4];
        var status = encoder.EncodeUtf8(input, output, out int consumed, out int written);
        var partial = encoder.EncodeUtf8(input, output, out int pc, out int pw, isFinalBlock: false);
        Console.WriteLine($"{status} {consumed}/{input.Length} {written} [{Show(output.AsSpan(0, written))}] first {encoder.FindFirstCharacterToEncodeUtf8(input)} partial {partial} {pc} {pw}");
        // Every destination size: the progress made before it fills.
        var sizes = new StringBuilder();
        for (int size = 0; size < written; size++)
        {
            var small = new byte[size];
            var s = encoder.EncodeUtf8(input, small, out int c, out int w);
            sizes.Append($"{(s == OperationStatus.DestinationTooSmall ? "" : s.ToString())}{c}:{w} ");
        }
        if (sizes.Length > 0) Console.WriteLine($"  small: {sizes}");
    }
}
