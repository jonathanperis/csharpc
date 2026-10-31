// StringBuilder's chunk layout, as .NET builds it: GetChunks (count and lengths), Capacity, Length and
// EnsureCapacity after appends that cross chunks, Insert, Remove, Replace, the Length setter, Clear,
// AppendFormat, interpolation with alignment and MaxCapacity. Each step prints the chunk lengths and the
// content's hash so a different split shows even when the text is the same.
using System.Text;

static void Show(string label, StringBuilder sb)
{
    var lengths = new StringBuilder();
    int count = 0;
    foreach (var chunk in sb.GetChunks())
    {
        if (count++ > 0) lengths.Append(',');
        lengths.Append(chunk.Length);
    }
    string text = sb.ToString();
    int sum = 0;
    for (int i = 0; i < text.Length; i++) sum = unchecked(sum * 31 + text[i]);
    Console.WriteLine($"{label}: chunks {count} [{lengths}] length {sb.Length} capacity {sb.Capacity} sum {sum}");
}

var sb = new StringBuilder();
Show("empty", sb);
sb.Append("0123456789abcdef");
Show("full first chunk", sb);
sb.Append('x');
Show("one more char", sb);
for (int i = 0; i < 100; i++) sb.Append("word").Append(i).Append(' ');
Show("100 words", sb);
for (int i = 0; i < 3000; i++) sb.Append(i);
Show("3000 numbers", sb);
sb.Append('z', 20000);
Show("20000 z", sb);
sb.Append(new string('q', 9000));
Show("9000 q string", sb);

// Insert: small chunks slide in place, larger ones get a new chunk before them.
var ins = new StringBuilder("abc");
ins.Insert(1, "XY");
Show("insert in small", ins);
for (int i = 0; i < 40; i++) ins.Append("0123456789");
ins.Insert(0, "front");
ins.Insert(ins.Length / 2, "middle-insert-text");
ins.Insert(ins.Length, "end");
ins.Insert(5, "rep", 3);
Show("inserts", ins);
ins.Insert(10, 12345).Insert(2, 'c').Insert(3, true).Insert(4, 2.5);
Show("insert values", ins);

// Remove within a chunk, across chunks and whole chunks.
var rem = new StringBuilder();
for (int i = 0; i < 200; i++) rem.Append("abcdefghij");
Show("before remove", rem);
rem.Remove(3, 4);
Show("remove in first", rem);
rem.Remove(10, 300);
Show("remove across", rem);
rem.Remove(rem.Length - 50, 50);
Show("remove tail", rem);
rem.Remove(0, 100);
Show("remove head", rem);

// Replace: same length, longer (MakeRoom), shorter (Remove), and a match across a chunk boundary.
var rep = new StringBuilder();
for (int i = 0; i < 300; i++) rep.Append("the quick brown fox ");
Show("before replace", rep);
rep.Replace("quick", "QUICK");
Show("replace same", rep);
rep.Replace("fox", "foxes");
Show("replace longer", rep);
rep.Replace("foxes", "f");
Show("replace shorter", rep);
rep.Replace('o', '0', 10, 500);
Show("replace char range", rep);
var cross = new StringBuilder("0123456789abcdef");
cross.Append("ghij");
cross.Replace("efgh", "<>");
Show("replace across chunks", cross);

// The Length setter: truncate into an earlier chunk, extend with '\0', and Clear.
var len = new StringBuilder();
for (int i = 0; i < 500; i++) len.Append("0123456789");
len.Length = 3000;
Show("truncate", len);
len.Length = 3100;
Show("extend", len);
len.Length = 10;
Show("truncate to first", len);
len.Clear();
Show("clear", len);
len.Append("after clear");
Show("append after clear", len);

// Capacity and EnsureCapacity.
var cap = new StringBuilder(5);
Show("capacity 5", cap);
Console.WriteLine($"ensure 3 -> {cap.EnsureCapacity(3)}, ensure 40 -> {cap.EnsureCapacity(40)}");
cap.Append(new string('a', 50));
Show("after 50", cap);
Console.WriteLine($"ensure 100 -> {cap.EnsureCapacity(100)}");
Show("ensured 100", cap);
cap.Capacity = cap.Length;
Show("capacity = length", cap);
try { cap.Capacity = 3; } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.ParamName}: {e.Message}"); }
var withText = new StringBuilder("hello", 3);
Show("text over capacity", withText);
var sized = new StringBuilder("hello world", 2, 5, 0);
Show("substring ctor", sized);

// MaxCapacity.
var max = new StringBuilder(4, 30);
Console.WriteLine($"max capacity {max.MaxCapacity}");
max.Append("0123456789");
max.Append("0123456789");
Show("bounded", max);
try { max.Append(new string('x', 20)); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.ParamName}: {e.Message}"); }
try { new StringBuilder(10, 5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.ParamName}: {e.Message}"); }

// AppendFormat appends literal runs, padding and values separately; interpolation pads separately too.
var fmt = new StringBuilder();
fmt.Append("0123456789abcd");
fmt.AppendFormat("{0}|{1,8}|{2,-6}|{{x}}", 12345, "right", "left");
Show("AppendFormat", fmt);
var interp = new StringBuilder();
interp.Append("0123456789abc");
int n = 42;
string? s = null;
interp.Append($"[{n,10}][{"ab",-7}][{s,3}]");
Show("interpolation", interp);
Console.WriteLine(interp.ToString());

// AppendJoin and Append(StringBuilder) cross chunks piece by piece.
var join = new StringBuilder("0123456789");
join.AppendJoin(", ", "alpha", "beta", "gamma", "delta");
Show("AppendJoin", join);
var copy = new StringBuilder("xy");
copy.Append(rem);
Show("Append(StringBuilder)", copy);
Console.WriteLine($"equals {copy.Equals(new StringBuilder("xy").Append(rem.ToString()))} {copy.ToString(2, 5)}");

// The indexer and CopyTo over many chunks.
var many = new StringBuilder();
for (int i = 0; i < 2000; i++) many.Append((char)('a' + i % 26)).Append(i % 10);
Show("many", many);
long total = 0;
for (int i = 0; i < many.Length; i += 13) total += many[i];
many[5] = '#';
many[many.Length - 1] = '$';
char[] dest = new char[12];
many.CopyTo(10, dest, 0, 12);
Console.WriteLine($"indexer {total} {many[5]} {many[many.Length - 1]} copy {new string(dest)}");
try { _ = many[many.Length]; } catch (IndexOutOfRangeException e) { Console.WriteLine(e.GetType().Name); }

// A fixed pseudo-random mix of Replace (matches inside and across chunks, growing and shrinking, over
// ranges), Insert, Remove and Append on a many-chunk builder, its layout printed after each step.
uint seed = 12345;
int Next(int bound) { seed = seed * 1103515245 + 12345; return (int)((seed >> 8) % (uint)bound); }
string[] words = ["a", "ab", "ba", "aab", "b", "", "abba", "bbbbbbbbbbbbbbbbbbbb", "aaaa"];
var mix = new StringBuilder();
for (int i = 0; i < 400; i++) mix.Append(words[Next(words.Length)]).Append(Next(3) == 0 ? "ab" : "a");
Show("mix start", mix);
for (int step = 0; step < 60; step++)
{
    switch (Next(5))
    {
        case 0:
        case 1:
        {
            string oldValue = words[Next(words.Length - 1)];
            if (oldValue.Length == 0) oldValue = "ba";
            string newValue = words[Next(words.Length)];
            int start = Next(mix.Length + 1);
            int count = Next(mix.Length - start + 1);
            mix.Replace(oldValue, newValue, start, count);
            Show($"replace '{oldValue}' '{newValue}' {start} {count}", mix);
            break;
        }
        case 2:
            mix.Insert(Next(mix.Length + 1), words[Next(words.Length)], 1 + Next(3));
            Show("insert", mix);
            break;
        case 3:
        {
            int start = Next(mix.Length + 1);
            mix.Remove(start, Next(Math.Min(mix.Length - start, 300) + 1));
            Show("remove", mix);
            break;
        }
        default:
            mix.Append(words[Next(words.Length)], 0, 0).Append('b', Next(40)).Append(words[Next(words.Length)]);
            Show("append", mix);
            break;
    }
}
mix.Replace("a", "aa").Replace("aa", "b").Replace("b", "");
Show("mix end", mix);
