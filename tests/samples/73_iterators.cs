// Iterators (yield return / yield break) lowered to LLVM coroutines.
foreach (var n in Range(1, 5)) Console.Write(n + " ");
Console.WriteLine();
foreach (var f in Fibonacci(10)) Console.Write(f + " ");
Console.WriteLine();
foreach (var w in Words("the quick brown fox")) Console.WriteLine(w);

var evens = Evens(10);
int total = 0;
foreach (var e in evens) total += e;
foreach (var e in evens) total += e;      // a second enumeration starts over
Console.WriteLine(total);

foreach (var row in Rows(3))
{
    foreach (var cell in Cells(row)) Console.Write(cell + " ");
    Console.WriteLine();
}

var en = Countdown(3);
while (en.MoveNext()) Console.Write(en.Current + "..");
Console.WriteLine("liftoff");
foreach (var o in Mixed()) Console.Write(o + "|");
Console.WriteLine();

static IEnumerable<int> Range(int start, int count)
{
    for (int i = 0; i < count; i++) yield return start + i;
}

static IEnumerable<long> Fibonacci(int n)
{
    long a = 0, b = 1;
    for (int i = 0; i < n; i++)
    {
        yield return a;
        (a, b) = (b, a + b);
    }
}

static IEnumerable<string> Words(string text)
{
    int start = 0;
    for (int i = 0; i <= text.Length; i++)
    {
        if (i == text.Length || text[i] == ' ')
        {
            if (i > start) yield return text.Substring(start, i - start);
            start = i + 1;
        }
    }
}

static IEnumerable<int> Evens(int limit)
{
    for (int i = 0; i <= limit; i += 2) yield return i;
}

static IEnumerable<int> Rows(int n) { for (int r = 0; r < n; r++) yield return r; }
static IEnumerable<string> Cells(int row) { for (int c = 0; c <= row; c++) yield return $"{row}{c}"; }

static IEnumerator<int> Countdown(int from)
{
    while (from > 0) yield return from--;
    yield break;
}

static System.Collections.IEnumerable Mixed()
{
    yield return 1;
    yield return "two";
    yield return 3.5;
}
