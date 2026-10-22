// Number crunching: BigInteger factorials/Fibonacci/powers, primes (sieve, Miller-Rabin), Collatz
// statistics, decimal compound interest, rational arithmetic with a generic Fraction struct.
using System.Numerics;

var f100 = Factorial(100);
Console.WriteLine($"100! has {f100.ToString().Length} digits, digit sum {f100.ToString().Sum(c => c - '0')}, trailing zeros {f100.ToString().Length - f100.ToString().TrimEnd('0').Length}");
Console.WriteLine($"fib(300) = {Fib(300)}");
Console.WriteLine($"2^256 = {BigInteger.Pow(2, 256)}");
Console.WriteLine($"gcd(fib(120), fib(80)) = {BigInteger.GreatestCommonDivisor(Fib(120), Fib(80))} (= fib(40) = {Fib(40)})");
Console.WriteLine($"modpow(3, 1000003, 1000000007) = {BigInteger.ModPow(3, 1000003, 1000000007)}");

var primes = Sieve(2_000_000);
Console.WriteLine($"primes below 2,000,000: {primes.Count:N0}, sum {primes.Sum(p => (long)p):N0}, largest {primes[^1]}");
var twins = primes.Zip(primes.Skip(1)).Count(p => p.Second - p.First == 2);
Console.WriteLine($"twin prime pairs: {twins:N0}; max gap {primes.Zip(primes.Skip(1)).Max(p => p.Second - p.First)}");
foreach (var n in new ulong[] { 1_000_000_007, 999_999_999_989, 1_000_000_000_001, 18_446_744_073_709_551_557 })
    Console.WriteLine($"  {n,26:N0} is {(IsProbablePrime(n) ? "prime" : "composite")}");

var (bestStart, bestSteps) = (0L, 0);
var histogram = new Dictionary<int, int>();
for (long start = 1; start < 300_000; start++)
{
    int steps = 0;
    for (long x = start; x != 1; x = (x & 1) == 0 ? x >> 1 : 3 * x + 1) steps++;
    if (steps > bestSteps) (bestStart, bestSteps) = (start, steps);
    histogram[steps / 50] = histogram.GetValueOrDefault(steps / 50) + 1;
}
Console.WriteLine($"collatz: longest chain below 300,000 starts at {bestStart} ({bestSteps} steps)");
Console.WriteLine("  " + string.Join(" ", histogram.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key * 50}+:{kv.Value}")));

decimal balance = 10_000m;
for (int year = 1; year <= 10; year++)
{
    balance = Math.Round(balance * (1 + 0.0425m / 12) * 1, 2);
    for (int m = 1; m < 12; m++) balance = Math.Round(balance * (1 + 0.0425m / 12), 2);
    if (year % 5 == 0) Console.WriteLine($"year {year,2}: {balance,12:C2}");
}

var h = Fraction<long>.Zero;
for (long k = 1; k <= 20; k++) h += new Fraction<long>(1, k);
Console.WriteLine($"H(20) = {h} ~ {h.ToDouble():F10}");
var big = Fraction<BigInteger>.Zero;
for (int k = 1; k <= 50; k++) big += new Fraction<BigInteger>(1, k);
Console.WriteLine($"H(50) = {big.Numerator.ToString().Length}-digit numerator / {big.Denominator.ToString().Length}-digit denominator");
Console.WriteLine($"(2/3) * (9/4) - 1/2 = {new Fraction<int>(2, 3) * new Fraction<int>(9, 4) - new Fraction<int>(1, 2)}");

static BigInteger Factorial(int n) { BigInteger r = 1; for (int i = 2; i <= n; i++) r *= i; return r; }
static BigInteger Fib(int n) { BigInteger a = 0, b = 1; for (int i = 0; i < n; i++) (a, b) = (b, a + b); return a; }

static List<int> Sieve(int limit)
{
    var composite = new bool[limit];
    var result = new List<int>();
    for (int i = 2; i < limit; i++)
    {
        if (composite[i]) continue;
        result.Add(i);
        for (long j = (long)i * i; j < limit; j += i) composite[j] = true;
    }
    return result;
}

static bool IsProbablePrime(ulong n)
{
    if (n < 2) return false;
    foreach (ulong p in new ulong[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
    {
        if (n % p == 0) return n == p;
    }
    ulong d = n - 1;
    int r = 0;
    while ((d & 1) == 0) { d >>= 1; r++; }
    foreach (ulong a in new ulong[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
    {
        var x = (ulong)BigInteger.ModPow(a, d, n);
        if (x == 1 || x == n - 1) continue;
        bool composite = true;
        for (int i = 1; i < r && composite; i++)
        {
            x = (ulong)((UInt128)x * x % n);
            if (x == n - 1) composite = false;
        }
        if (composite) return false;
    }
    return true;
}

readonly struct Fraction<T> where T : IBinaryInteger<T>
{
    public T Numerator { get; }
    public T Denominator { get; }

    public Fraction(T numerator, T denominator)
    {
        if (T.IsZero(denominator)) throw new DivideByZeroException();
        if (T.IsNegative(denominator)) { numerator = -numerator; denominator = -denominator; }
        var g = Gcd(T.Abs(numerator), denominator);
        Numerator = T.IsZero(g) ? numerator : numerator / g;
        Denominator = T.IsZero(g) ? denominator : denominator / g;
    }

    public static Fraction<T> Zero => new(T.Zero, T.One);
    private static T Gcd(T a, T b) { while (!T.IsZero(b)) (a, b) = (b, a % b); return a; }

    public static Fraction<T> operator +(Fraction<T> a, Fraction<T> b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Fraction<T> operator -(Fraction<T> a, Fraction<T> b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Fraction<T> operator *(Fraction<T> a, Fraction<T> b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public double ToDouble() => double.CreateChecked(Numerator) / double.CreateChecked(Denominator);
    public override string ToString() => $"{Numerator}/{Denominator}";
}
