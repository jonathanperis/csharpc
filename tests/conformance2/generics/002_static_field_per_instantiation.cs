Counter<int>.N++; Counter<int>.N++; Counter<string>.N++;
Console.WriteLine($"{Counter<int>.N} {Counter<string>.N} {Counter<double>.N}");
static class Counter<T> { public static int N; }
