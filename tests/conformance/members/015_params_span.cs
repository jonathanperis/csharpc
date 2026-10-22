Console.WriteLine(Count(1, 2, 3, 4));
static int Count(params ReadOnlySpan<int> xs) => xs.Length;
