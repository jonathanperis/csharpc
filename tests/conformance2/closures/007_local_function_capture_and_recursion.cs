int depth = 0;
int Fib(int n) { depth++; return n < 2 ? n : Fib(n - 1) + Fib(n - 2); }
Console.WriteLine($"{Fib(10)} calls={depth}");
static int Pure(int x) => x * 2;
Console.WriteLine(Pure(21));
