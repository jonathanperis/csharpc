// Recursion, loops and static local functions.
for (int i = 0; i <= 20; i += 5)
    Console.WriteLine($"fib({i}) = {Fib(i)}");

Console.WriteLine($"10! = {Factorial(10)}");
Console.WriteLine($"20! = {Factorial(20)}");
Console.WriteLine($"gcd(1071, 462) = {Gcd(1071, 462)}");
Console.WriteLine($"ackermann(2, 3) = {Ackermann(2, 3)}");
Console.WriteLine($"collatz(27) takes {Collatz(27)} steps");

static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
static long Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
static int Ackermann(int m, int n) =>
    m == 0 ? n + 1 : n == 0 ? Ackermann(m - 1, 1) : Ackermann(m - 1, Ackermann(m, n - 1));
static int Collatz(long n)
{
    int steps = 0;
    while (n != 1)
    {
        n = n % 2 == 0 ? n / 2 : 3 * n + 1;
        steps++;
    }
    return steps;
}
