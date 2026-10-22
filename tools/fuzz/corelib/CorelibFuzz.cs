// Differential fuzzer for the C# corelib's input handling: number parsing and formatting, composite
// formatting, decimal, BigInteger, TimeSpan/DateTime formatting and string operations. Prints one
// line per case (the result, or the exception type and message); tools/fuzz/corelib.sh runs it under
// real .NET and as a csharpc-compiled native binary (with sanitizers) and diffs the output.
//   CorelibFuzz <seed> <cases>
// Inputs come from a private xorshift generator, so a (seed, cases) pair is reproducible everywhere.
// Each case reseeds from (seed, index): a case that throws early on one side and draws fewer values
// must not shift every later case.
using System.Globalization;
using System.Numerics;
using System.Text;

ulong seed = args.Length > 0 ? (ulong)long.Parse(args[0]) : 1;
ulong state = 1;
int cases = args.Length > 1 ? int.Parse(args[1]) : 10000;

for (int i = 0; i < cases; i++)
{
    state = (seed * 0x9E3779B97F4A7C15UL) ^ ((ulong)i * 0xD1B54A32D192ED03UL) ^ 0x2545F4914F6CDD1DUL;
    for (int k = 0; k < 4; k++) NextU64();
    int target = Next(9);
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
            var date = new DateTime((long)(NextU64() % (ulong)DateTime.MaxValue.Ticks));
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
}
