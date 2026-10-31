// System.Text.Ascii and the UTF-8 / ASCII / Latin-1 encoders over inputs of every length up to 40, with
// a difference or a non-ASCII element at each position: the results of the 8-at-a-time loops must match
// the element-by-element ones, at block boundaries and in the tails.
using System.Buffers;
using System.Text;

const string Alphabet = "Host: example.com, Accept: text/plain; q=0.9";
var report = new StringBuilder();
for (int length = 0; length <= 40; length++)
{
    string text = Alphabet.Substring(0, length);
    byte[] bytes = Encoding.ASCII.GetBytes(text);
    int equal = 0, total = 0;
    void Count(bool result) { total++; if (result) equal++; }
    Count(Ascii.Equals(bytes, text));
    Count(Ascii.Equals(text, bytes));
    Count(Ascii.Equals(bytes, bytes.ToArray()));
    Count(Ascii.Equals(text, new string(text.ToCharArray())));
    var positions = new StringBuilder();
    for (int at = 0; at < length; at++)
    {
        // A different ASCII element, then a non-ASCII one, at position `at`.
        var differentChars = text.ToCharArray();
        differentChars[at] = differentChars[at] == 'x' ? 'y' : 'x';
        var nonAsciiChars = text.ToCharArray();
        nonAsciiChars[at] = 'é';
        var differentBytes = (byte[])bytes.Clone();
        differentBytes[at] = (byte)differentChars[at];
        var nonAsciiBytes = (byte[])bytes.Clone();
        nonAsciiBytes[at] = 0xE9;
        bool a = Ascii.Equals(differentBytes, text), b = Ascii.Equals(bytes, new string(differentChars));
        bool c = Ascii.Equals(nonAsciiBytes, nonAsciiBytes.ToArray()), d = Ascii.Equals(new string(nonAsciiChars), new string(nonAsciiChars));
        bool e = Ascii.Equals(nonAsciiBytes, new string(nonAsciiChars)), f = Ascii.Equals(differentBytes, differentBytes.ToArray());
        positions.Append(a || b || c || d || e || !f ? '!' : '.');

        var narrowed = new byte[length];
        var status = Ascii.FromUtf16(new string(nonAsciiChars), narrowed, out int narrowedCount);
        var widened = new char[length];
        var wideStatus = Ascii.ToUtf16(nonAsciiBytes, widened, out int widenedCount);
        if (status != OperationStatus.InvalidData || narrowedCount != at || wideStatus != OperationStatus.InvalidData || widenedCount != at
            || Encoding.ASCII.GetString(narrowed, 0, narrowedCount) != text.Substring(0, at) || new string(widened, 0, widenedCount) != text.Substring(0, at))
            positions.Append('#');

        string withNonAscii = new string(nonAsciiChars);
        report.Append($" {Convert.ToHexString(Encoding.UTF8.GetBytes(withNonAscii)).Length}/{Hash(Encoding.ASCII.GetBytes(withNonAscii))}/{Hash(Encoding.Latin1.GetBytes(withNonAscii))}/{Hash(Encoding.UTF8.GetBytes(withNonAscii))}");
    }
    var shortDestination = new byte[Math.Max(0, length - 3)];
    var shortStatus = Ascii.FromUtf16(text, shortDestination, out int shortCount);
    var full = new char[length + 2];
    var fullStatus = Ascii.ToUtf16(bytes, full, out int fullCount);
    Console.WriteLine($"{length,2}: {equal}/{total} {positions} {shortStatus} {shortCount} {fullStatus} {fullCount} {Hash(Encoding.UTF8.GetBytes(text))} {Hash(Encoding.Latin1.GetBytes(text))}");
    try
    {
        int written = Encoding.ASCII.GetBytes(text.AsSpan(), new byte[Math.Max(0, length - 1)]);
        Console.WriteLine($"    fits: {written}");
    }
    catch (ArgumentException ex) { Console.WriteLine($"    {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine(Hash(Encoding.UTF8.GetBytes(report.ToString())));

static int Hash(byte[] data)
{
    int h = 17;
    foreach (byte x in data) h = unchecked(h * 31 + x);
    return h;
}
