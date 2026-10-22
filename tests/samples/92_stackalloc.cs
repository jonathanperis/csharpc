#:property AllowUnsafeBlocks=true
// stackalloc into pointers and spans (zero-initialized, as in .NET).
unsafe
{
    int* squares = stackalloc int[5];
    for (int i = 0; i < 5; i++) squares[i] = i * i;
    int total = 0;
    for (int i = 0; i < 5; i++) total += squares[i];
    Console.WriteLine(total);

    long* zeros = stackalloc long[3];
    Console.WriteLine(zeros[0] + zeros[1] + zeros[2]);

    int* init = stackalloc int[] { 3, 1, 4, 1, 5 };
    Console.WriteLine(init[2]);
}

Span<int> buffer = stackalloc int[8];
for (int i = 0; i < buffer.Length; i++) buffer[i] = i + 1;
int sum = 0;
foreach (var v in buffer) sum += v;
Console.WriteLine($"{buffer.Length} {sum}");

Span<char> chars = stackalloc char[] { 'c', 's', 'h', 'a', 'r', 'p' };
Console.WriteLine(chars.ToString());
Console.WriteLine(Fib(30));

static int Fib(int n)
{
    Span<int> memo = stackalloc int[n + 1];
    memo[1] = 1;
    for (int i = 2; i <= n; i++) memo[i] = memo[i - 1] + memo[i - 2];
    return memo[n];
}
