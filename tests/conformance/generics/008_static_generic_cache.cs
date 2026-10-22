Console.WriteLine(Counter<int>.Next() + Counter<int>.Next() + Counter<string>.Next());
static class Counter<T> { static int _n; public static int Next() => ++_n; }
