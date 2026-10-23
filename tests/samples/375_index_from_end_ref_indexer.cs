// `buffer[^1]` on a type with Length and a `ref T this[int]` indexer (Roslyn's implicit Index
// support): the element is read through the returned reference, and can be assigned through it.
// The culture fuzzer found this through DateTimeFormat, whose "F" specifier drops a '.' left
// in front of an all-zero fraction by testing result[^1].
using System;
using System.Globalization;

var b = new Buffer(new char[8]);
b.Append('a'); b.Append('.');
DropTrailingDot(ref b);
Console.WriteLine(b.Length);
b.Append('x'); b.Append('y');
b[^1] = 'z';
b[^2]++;
char last = b[^1], first = b[^b.Length];
Console.WriteLine($"{first} {b[1]} {b[2]} {last} {b[^1] == 'z'}");

var date = new DateTime(2026, 10, 23, 8, 5, 3);
foreach (var format in new[] { "HH:mm:ss.FFF", "s.F", ".FM", "yhd.Fd", "M-/FHd", "HH:mm:ss.FFFFFFF" })
    Console.WriteLine(format + " -> " + date.ToString(format, CultureInfo.InvariantCulture) + " | " + date.AddTicks(1234).ToString(format, CultureInfo.GetCultureInfo("es-ES")));

static void DropTrailingDot(ref Buffer r)
{
    if (r.Length > 0 && r[^1] == '.') r.Length--;
}

ref struct Buffer
{
    private Span<char> _chars;
    private int _pos;
    public Buffer(Span<char> chars) { _chars = chars; _pos = 0; }
    public int Length { get => _pos; set => _pos = value; }
    public ref char this[int index] => ref _chars[index];
    public void Append(char c) => _chars[_pos++] = c;
}
