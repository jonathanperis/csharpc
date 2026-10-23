// Differential fuzzer for the C# corelib's input handling: number parsing and formatting, composite
// formatting, decimal, BigInteger, TimeSpan/DateTime formatting, string operations and regular
// expressions (random patterns from a small grammar, with every RegexOptions combination). Prints one
// line per case (the result, or the exception type and message); tools/fuzz/corelib.sh runs it under
// real .NET and as a csharpc-compiled native binary (with sanitizers) and diffs the output.
//   CorelibFuzz <seed> <cases>
// Inputs come from a private xorshift generator, so a (seed, cases) pair is reproducible everywhere.
// Each case reseeds from (seed, index): a case that throws early on one side and draws fewer values
// must not shift every later case.
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

ulong seed = args.Length > 0 ? (ulong)long.Parse(args[0]) : 1;
ulong state = 1;
int cases = args.Length > 1 ? int.Parse(args[1]) : 10000;

for (int i = 0; i < cases; i++)
{
    state = (seed * 0x9E3779B97F4A7C15UL) ^ ((ulong)i * 0xD1B54A32D192ED03UL) ^ 0x2545F4914F6CDD1DUL;
    for (int k = 0; k < 4; k++) NextU64();
    int target = Next(12);
    string line;
    try { line = Run(target); }
    catch (Exception e) { line = e.GetType().Name + ": " + e.Message; }
    Console.WriteLine(i + " " + target + " " + Escape(line));
}

string Run(int target)
{
    switch (target)
    {
        case 0:
        {
            string s = Text(NumberAlphabet, 12);
            var sb = new StringBuilder(Escape(s)).Append(" -> ");
            sb.Append(int.TryParse(s, out int a) ? a.ToString() : "no").Append(' ');
            sb.Append(long.TryParse(s, out long b) ? b.ToString() : "no").Append(' ');
            sb.Append(double.TryParse(s, out double c) ? c.ToString("R") : "no");
            sb.Append(' ').Append(int.Parse(s));
            return sb.ToString();
        }
        case 1:
        {
            long v = Value();
            string f = Text(FormatAlphabet, 4);
            return v + " " + Escape(f) + " -> " + v.ToString(f) + " " + ((int)v).ToString(f);
        }
        case 2:
        {
            double v = Next(4) == 0 ? BitConverter.Int64BitsToDouble((long)NextU64()) : Value() / Math.Pow(10, Next(20));
            string f = Text(FormatAlphabet, 4);
            return v.ToString("R") + " " + Escape(f) + " -> " + v.ToString(f);
        }
        case 3:
        {
            string s = Text(NumberAlphabet, 20);
            decimal d = decimal.Parse(s);
            string f = Text(FormatAlphabet, 4);
            return Escape(s) + " -> " + d.ToString() + " " + d.ToString(f);
        }
        case 4:
        {
            string f = Text(CompositeAlphabet, 16);
            return Escape(f) + " -> " + string.Format(f, 42, "text", -1.5);
        }
        case 5:
        {
            var span = new TimeSpan(Value());
            var date = new DateTime((long)(NextU64() % (ulong)DateTime.MaxValue.Ticks), (DateTimeKind)Next(3));
            string f = Text(DateAlphabet, 8);
            return Escape(f) + " -> " + span.ToString() + " " + date.ToString(f) + " " + span.ToString(f);
        }
        case 6:
        {
            string s = Text(NumberAlphabet, 40);
            var big = BigInteger.Parse(s);
            return Escape(s) + " -> " + big.ToString() + " " + (big * big - 1).ToString() + " " + big.ToString("X");
        }
        case 7:
        {
            string s = Text(StringAlphabet, 16), from = Text(StringAlphabet, 3), to = Text(StringAlphabet, 3);
            int start = Next(20) - 2, length = Next(20) - 2, width = Next(24) - 2;
            var sb = new StringBuilder();
            sb.Append(Escape(s.Replace(from.Length > 0 ? from : "a", to))).Append('|');
            sb.Append(string.Join("/", s.Split(from.Length > 0 ? from[0] : ','))).Append('|');
            sb.Append(Escape(s.Trim())).Append('|').Append(Escape(s.PadLeft(width, '.'))).Append('|');
            sb.Append(s.IndexOf(from)).Append('|');
            sb.Append(Escape(s.Substring(start, length)));
            return sb.ToString();
        }
        case 9:
        case 10:
        case 11:
            return RegexCase();
        default:
        {
            string s = Text(NumberAlphabet, 10);
            var sb = new StringBuilder(Escape(s)).Append(" -> ");
            sb.Append(uint.TryParse(s, NumberStyles.Integer, null, out uint a) ? a.ToString() : "no").Append(' ');
            sb.Append(ulong.TryParse(s, NumberStyles.Integer, null, out ulong c) ? c.ToString() : "no").Append(' ');
            sb.Append(uint.Parse(s, NumberStyles.HexNumber));
            return sb.ToString();
        }
    }
}

// A random pattern (sometimes malformed), options and input; prints every match with its groups and
// captures, then Replace and Split results. Inputs are short, so backtracking stays cheap.
string RegexCase()
{
    string pattern = Pattern(3);
    var options = (RegexOptions)(Next(4) == 0 ? 0 : (int)NextU64() & 0x37F); // no Compiled/NonBacktracking
    if (Next(6) != 0) options &= ~RegexOptions.ECMAScript;
    if ((options & RegexOptions.ECMAScript) != 0 && Next(2) == 0)
        options &= RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant;
    string input = Text(RegexInputAlphabet, 12);
    var sb = new StringBuilder(Escape(pattern)).Append(" /").Append((int)options).Append(" on ").Append(Escape(input)).Append(" ->");
    var regex = new Regex(pattern, options);
    for (Match m = regex.Match(input); m.Success; m = m.NextMatch())
    {
        sb.Append(" [").Append(m.Index).Append(',').Append(m.Length);
        foreach (Group g in m.Groups)
        {
            sb.Append(' ').Append(g.Name).Append('=');
            if (!g.Success) { sb.Append('-'); continue; }
            sb.Append(g.Index).Append(':').Append(Escape(g.Value));
            if (g.Captures.Count > 1)
                foreach (Capture c in g.Captures) sb.Append('(').Append(c.Index).Append(':').Append(Escape(c.Value)).Append(')');
        }
        sb.Append(']');
    }
    sb.Append(" | ").Append(Escape(regex.Replace(input, Next(2) == 0 ? "<$&>" : "${1}$$$`_$'$+")));
    sb.Append(" | ").Append(Escape(string.Join("/", regex.Split(input))));
    sb.Append(" | ").Append(string.Join(",", regex.GetGroupNames())).Append(' ').Append(regex.IsMatch(input));
    return sb.ToString();
}

string Pattern(int depth)
{
    var sb = new StringBuilder();
    int n = 1 + Next(4);
    for (int i = 0; i < n; i++)
    {
        if (i > 0 && Next(6) == 0) sb.Append('|');
        string atom = Atom(depth);
        sb.Append(atom);
        // Inline options and comments aren't quantifiable ("Quantifier following nothing").
        bool inline = atom.Length <= 5 && atom.StartsWith("(?") && !atom.StartsWith("(?:");
        if (Next(3) == 0 && !inline) sb.Append(Quantifier());
    }
    return sb.ToString();
}

string Atom(int depth)
{
    // Mostly well-formed: one atom in ~40 is malformed (an unbalanced token, a bad escape or range).
    int k = Next(40) == 0 ? 8 : depth <= 0 ? Next(4) : Next(10) is var j && j == 8 ? 0 : j;
    if (k == 2 && Next(10) != 0) return Pick(@"\d", @"\w", @"\s", @"\D", @"\W", @"\S", @"\b", @"\B", "^", "$", @"\A", @"\z", @"\Z", @"\G", @"\p{L}", @"\P{Lu}", @"\p{Ll}", @"\x41", @"\t", @"\n", @"\e", @"\cA", "#");
    if (k == 6 && Next(2) == 0) return "(?(" + Pick("(?=a)", "a|b", "x") + ")" + "(?:" + Pattern(depth - 1) + ")|(?:" + Pattern(depth - 1) + "))";
    switch (k)
    {
        case 0: case 1: return Pick("a", "b", "c", "A", "B", "x", "0", "1", " ", "-", ".", "\\.", "é", "É", "i", "I", "k", "K", "İ", "ı");
        case 2: return Pick(@"\d", @"\w", @"\s", @"\D", @"\W", @"\S", @"\b", @"\B", "^", "$", @"\A", @"\z", @"\Z", @"\G", @"\p{L}", @"\P{Lu}", @"\p{Ll}", @"\x41", @"b", @"\t", @"\n", @"\e", @"\cA", @"\1", @"\k<n>", @"\q", @"\p{Nope}", "#");
        case 3: return CharClass();
        case 4: return "(" + Pattern(depth - 1) + ")";
        case 5: return Pick("(?:", "(?<n>", "(?'n'", "(?=", "(?!", "(?<=", "(?<!", "(?>", "(?i:", "(?-i:", "(?m:", "(?s:", "(?x:", "(?n:", "(?<n-n>", "(?<1>") + Pattern(depth - 1) + ")";
        case 6: return "(?(" + Pick("1", "n", "a", "x") + ")" + "(?:" + Pattern(depth - 1) + ")|(?:" + Pattern(depth - 1) + "))";
        case 7: return Pick("(?i)", "(?m)", "(?-i)", "(?x)", "(?s)", "(?n)", "(?#c)");
        case 8: return Pick("(", ")", "[", "]", "{", "}", "*", "+", "?", "\\", "(?", "(?<", "a{2,1}", "[b-a]", "x**", "(?z)");
        default: return "(" + Pattern(depth - 1) + ")";
    }
}

string CharClass()
{
    var sb = new StringBuilder("[");
    if (Next(3) == 0) sb.Append('^');
    int n = 1 + Next(3);
    for (int i = 0; i < n; i++)
    {
        sb.Append(Next(5) switch
        {
            0 => Pick("a-c", "A-Z", "0-9", "x-z", "à-ÿ", "a-a"),
            1 => Pick(@"\d", @"\w", @"\s", @"\W", @"\p{Lu}", @"\P{L}"),
            2 => Pick("-", "]", "\\]", "^", ".", "b"),
            _ => Pick("a", "B", "1", " ", "é", "k", "I"),
        });
    }
    if (Next(5) == 0) sb.Append("-[").Append(Pick("a", "aeiou", @"\d", "b-c")).Append(']');
    return sb.Append(']').ToString();
}

string Quantifier() =>
    Pick("*", "+", "?", "{2}", "{1,}", "{0,2}", "{1,3}", "*", "+", "?") + (Next(3) == 0 ? "?" : "");

string Pick(params string[] items) => items[Next(items.Length)];

ulong NextU64()
{
    state ^= state << 13; state ^= state >> 7; state ^= state << 17;
    return state;
}

int Next(int bound) => (int)(NextU64() % (ulong)bound);

// Mostly small and boundary-adjacent values, sometimes any 64-bit pattern.
long Value() => Next(4) switch
{
    0 => (long)NextU64(),
    1 => Next(3) switch { 0 => long.MaxValue - Next(3), 1 => long.MinValue + Next(3), _ => int.MinValue + Next(3) },
    _ => (long)(NextU64() % 2000001) - 1000000,
};

string Text(string alphabet, int maxLength)
{
    int n = Next(maxLength + 1);
    var chars = new char[n];
    for (int i = 0; i < n; i++) chars[i] = alphabet[Next(alphabet.Length)];
    return new string(chars);
}

static string Escape(string s)
{
    var sb = new StringBuilder();
    foreach (char c in s)
    {
        if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("X4"));
        else sb.Append(c);
    }
    return sb.ToString();
}

partial class Program
{
    const string NumberAlphabet = "0123456789000111999-+.,eE xX_()$abcdefABCDEF \t";
    const string FormatAlphabet = "0123456789#.,;%‰eE+-NnFfGgCcPpXxDdRrEe'\\ ";
    const string CompositeAlphabet = "{}{}{}012,:-5XN2D3Ee0#.ab ";
    const string DateAlphabet = "dddHHhhmmssfffFFFtzyyyMMMgK:/.-' \\%";
    const string StringAlphabet = "ab, \t.aéb ";
    const string RegexInputAlphabet = "aabbcAB01 -.\nxéÉiIkKKİı()";
}
