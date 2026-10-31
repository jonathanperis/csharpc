// System.Text.Unicode.Utf8.FromUtf16 / ToUtf16 over long ASCII runs broken by non-ASCII scalars, surrogate
// pairs, lone surrogates and invalid UTF-8 (replaced or InvalidData), truncated input in a non-final block,
// and every destination size: the status, what was read and what was written.
using System;
using System.Text;
using System.Text.Unicode;

string[] texts =
[
    "The quick brown fox jumps over the lazy dog, 0123456789!",
    "ascii run then é and more ascii after the accent, 中文 at the end",
    "pair 😀 in the middle of a long run of plain text characters",
    "lone \ud800 surrogate, then text; and \udc00 a trailing one",
    "ends with a high surrogate \ud83d",
    "",
];

foreach (var text in texts)
{
    var full = new byte[text.Length * 3];
    var status = Utf8.FromUtf16(text, full, out int read, out int written);
    var strict = Utf8.FromUtf16(text, full, out int strictRead, out int strictWritten, replaceInvalidSequences: false);
    var partial = Utf8.FromUtf16(text, full, out int partialRead, out int partialWritten, isFinalBlock: false);
    Console.WriteLine($"{status} {read}/{text.Length} {written} strict {strict} {strictRead} {strictWritten} partial {partial} {partialRead} {partialWritten} [{Convert.ToHexString(full, 0, written)}]");
    var sizes = new StringBuilder();
    for (int size = 0; size < written; size += 3)
    {
        var s = Utf8.FromUtf16(text, new byte[size], out int r, out int w);
        sizes.Append($"{(s == System.Buffers.OperationStatus.DestinationTooSmall ? "" : s.ToString())}{r}:{w} ");
    }
    Console.WriteLine($"  small: {sizes}");

    var back = new char[written];
    var decoded = Utf8.ToUtf16(full.AsSpan(0, written), back, out int bytesRead, out int charsWritten);
    Console.WriteLine($"  back {decoded} {bytesRead} {charsWritten} same {new string(back, 0, charsWritten) == text}");
    var tooSmall = new StringBuilder();
    for (int size = 0; size < charsWritten; size += 5)
    {
        var s = Utf8.ToUtf16(full.AsSpan(0, written), new char[size], out int r, out int w);
        tooSmall.Append($"{(s == System.Buffers.OperationStatus.DestinationTooSmall ? "" : s.ToString())}{r}:{w} ");
    }
    Console.WriteLine($"  back small: {tooSmall}");
}

var bytes = new System.Collections.Generic.List<byte>();
bytes.AddRange(Encoding.ASCII.GetBytes("valid ascii prefix of some length "));
bytes.Add(0xFF);
bytes.AddRange(Encoding.ASCII.GetBytes(" middle "));
bytes.AddRange([0xE2, 0x82]);
bytes.AddRange(Encoding.ASCII.GetBytes(" tail text"));
bytes.AddRange([0xF0, 0x9F]);
var invalid = bytes.ToArray();
var chars = new char[invalid.Length * 2];
Console.WriteLine($"{Utf8.ToUtf16(invalid, chars, out int ir, out int iw)} {ir} {iw} [{new string(chars, 0, iw)}]");
Console.WriteLine($"{Utf8.ToUtf16(invalid, chars, out ir, out iw, replaceInvalidSequences: false)} {ir} {iw}");
Console.WriteLine($"{Utf8.ToUtf16(invalid, chars, out ir, out iw, isFinalBlock: false)} {ir} {iw}");
