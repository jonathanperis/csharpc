// Ascii and Base64 throughput, the program behind the NativeAOT column of docs/il-first/System.Text.Ascii.md
// (tools/il-first/measure-aot.sh): ASCII transcoding, validation, case-insensitive equality and casing, Base64 encoding,
// decoding and validation. The output is checksums only.
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

var text = new StringBuilder();
for (int i = 0; i < 4000; i++) text.Append("Header-Name: value ").Append(i).Append('\n');
string s = text.ToString();
byte[] utf8 = Encoding.UTF8.GetBytes(s);
byte[] ascii = new byte[s.Length];
char[] wide = new char[s.Length];
byte[] raw = new byte[48 * 1024];
for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i * 31 + (i >> 5));
byte[] b64 = new byte[Base64.GetMaxEncodedToUtf8Length(raw.Length)];
byte[] back = new byte[raw.Length];

long check = 0;
for (int round = 0; round < 400; round++)
{
    Ascii.FromUtf16(s, ascii, out int bw);
    Ascii.ToUtf16(ascii.AsSpan(0, bw), wide, out int cw);
    Ascii.ToUpperInPlace(ascii.AsSpan(0, bw), out _);
    check += bw + cw + (Ascii.EqualsIgnoreCase(ascii.AsSpan(0, bw), wide.AsSpan(0, bw)) ? 1 : 0) + (Ascii.IsValid(utf8) ? 1 : 0);
    Base64.EncodeToUtf8(raw, b64, out _, out int written);
    Base64.DecodeFromUtf8(b64.AsSpan(0, written), back, out _, out int decoded);
    check += written + decoded + back[round * 101] + (Base64.IsValid(b64.AsSpan(0, written)) ? 1 : 0);
}
Console.WriteLine(check);
