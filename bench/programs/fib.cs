// Recursive Fibonacci — call overhead and integer arithmetic.
Console.WriteLine(Fib(36));

static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
