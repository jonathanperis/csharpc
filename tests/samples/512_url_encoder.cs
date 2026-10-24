// System.Text.Encodings.Web.UrlEncoder, as ASP.NET Core's QueryString and routing escape with it.
using System.Text.Encodings.Web;
using System.Text.Unicode;

var url = UrlEncoder.Default;
foreach (var text in new[] { "plain-text_1.2~", "a b&c=d?e#f", "path/with:colon", "[x]{y}|z^`\\%", "!$()*,;@'+\"<>", "café €", "emoji 😀", "\ud800x", "" })
    Console.WriteLine(url.Encode(text));
Console.WriteLine(ReferenceEquals(url, UrlEncoder.Default) + " " + url.MaxOutputCharactersPerInputCharacter);
Console.WriteLine($"{url.WillEncode('a')} {url.WillEncode(' ')} {url.WillEncode('@')} {url.WillEncode(0xE9)} {url.WillEncode(0x1F600)}");
try { url.WillEncode(0xD800); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("not a scalar: " + e.ParamName); }
var latin = UrlEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement);
Console.WriteLine(latin.Encode("café ü €"));
var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin);
settings.ForbidCharacter('-');
Console.WriteLine(UrlEncoder.Create(settings).Encode("a-b c"));
string untouched = "nothing_to.escape";
Console.WriteLine(ReferenceEquals(url.Encode(untouched), untouched));
try { url.Encode((string)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
