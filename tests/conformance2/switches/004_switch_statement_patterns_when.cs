static void M(object? o)
{
    switch (o)
    {
        case int n when n < 0: Console.WriteLine("negative"); break;
        case int n: Console.WriteLine($"int {n}"); break;
        case string s when s.StartsWith("x"): Console.WriteLine("x-string"); break;
        case string: Console.WriteLine("string"); break;
        case null: Console.WriteLine("null"); break;
        default: Console.WriteLine("other"); break;
    }
}
M(-1); M(3); M("xyz"); M("abc"); M(null); M(1.0);
