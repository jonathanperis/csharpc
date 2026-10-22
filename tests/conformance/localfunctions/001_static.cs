Console.WriteLine(Fib(15));
static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
