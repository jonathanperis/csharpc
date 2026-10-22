// Parallel.For / ForEach / Invoke: distinct slots, deterministic reductions.
using System.Threading;
using System.Threading.Tasks;

var squares = new long[1_000];
Parallel.For(0, squares.Length, i => squares[i] = (long)i * i);
long sum = 0;
foreach (var s in squares) sum += s;
Console.WriteLine($"sum of squares: {sum}");

var primes = new bool[200_000];
Parallel.For(2, primes.Length, n => primes[n] = IsPrime(n));
int count = 0;
foreach (var p in primes) if (p) count++;
Console.WriteLine($"primes below 200000: {count}");

long total = 0;
Parallel.For(0L, 10_000L, i => Interlocked.Add(ref total, i));
Console.WriteLine($"long range total: {total}");

string[] words = ["alpha", "beta", "gamma", "delta", "epsilon"];
int letters = 0;
Parallel.ForEach(words, w => Interlocked.Add(ref letters, w.Length));
Console.WriteLine($"letters: {letters}");

var results = new int[3];
Parallel.Invoke(() => results[0] = Fib(20), () => results[1] = Fib(21), () => results[2] = Fib(22));
Console.WriteLine(string.Join(",", results));

double[] values = new double[100];
Parallel.For(0, values.Length, i => values[i] = Math.Sqrt(i));
Console.WriteLine(values[81] + values[64]);

static bool IsPrime(int n)
{
    if (n < 2) return false;
    for (int d = 2; d * d <= n; d++) if (n % d == 0) return false;
    return true;
}

static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
