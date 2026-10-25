// System.Text.Ascii: transcoding, equality and case conversion, as Kestrel's header parsing and
// response writing use them — results, counts written and statuses at non-ASCII input and short buffers.
using System.Buffers;
using System.Text;

var bytes = new byte[8];
var chars = new char[8];
Show(Ascii.FromUtf16("Content", bytes, out int written), written, Encoding.ASCII.GetString(bytes, 0, written));
Show(Ascii.FromUtf16("Content-Length", bytes, out written), written, Encoding.ASCII.GetString(bytes, 0, written));
Show(Ascii.FromUtf16("abécd", bytes, out written), written, Encoding.ASCII.GetString(bytes, 0, written));
Show(Ascii.ToUtf16("Server"u8, chars, out written), written, new string(chars, 0, written));
Show(Ascii.ToUtf16([0x41, 0xC3, 0xA9], chars, out written), written, new string(chars, 0, written));
Show(Ascii.ToUtf16("0123456789"u8, chars, out written), written, new string(chars, 0, written));

Console.WriteLine($"{Ascii.Equals("Host"u8, "Host"u8)} {Ascii.Equals("Host"u8, "host"u8)} {Ascii.Equals("Host"u8, "Host")} {Ascii.Equals("Host", "Hos")} {Ascii.Equals("hé", "hé")}");
Console.WriteLine($"{Ascii.EqualsIgnoreCase("Content-Type"u8, "content-type"u8)} {Ascii.EqualsIgnoreCase("ACCEPT", "accept"u8)} {Ascii.EqualsIgnoreCase("a[", "A{")} {Ascii.EqualsIgnoreCase("É", "é")} {Ascii.EqualsIgnoreCase(""u8, "")}");

Show(Ascii.ToLower("Keep-Alive"u8, bytes, out written), written, Encoding.ASCII.GetString(bytes, 0, written));
Show(Ascii.ToUpper("gzip", chars, out written), written, new string(chars, 0, written));
Show(Ascii.ToUpper("xéy", chars, out written), written, new string(chars, 0, written));
Show(Ascii.ToLower("TEXT"u8, chars, out written), written, new string(chars, 0, written));
Show(Ascii.ToUpper("utf-8", bytes, out written), written, Encoding.ASCII.GetString(bytes, 0, written));
var inPlace = "MiXeD-[Case]"u8.ToArray();
Show(Ascii.ToLowerInPlace(inPlace, out written), written, Encoding.ASCII.GetString(inPlace));
var charsInPlace = "abcéd".ToCharArray();
Show(Ascii.ToUpperInPlace(charsInPlace, out written), written, new string(charsInPlace));
try { Ascii.ToUpper(bytes.AsSpan(0, 4), bytes.AsSpan(2, 4), out _); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
Console.WriteLine($"{Ascii.IsValid((byte)0x7F)} {Ascii.IsValid('\u0080')} {Ascii.IsValid("plain")} {Ascii.IsValid("naïve")}");

static void Show(OperationStatus status, int written, string text) => Console.WriteLine($"{status} {written} [{text}]");
