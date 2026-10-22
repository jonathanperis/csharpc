// Early exit from foreach disposes the iterator, running its pending finally blocks.
foreach (var x in Guarded("A"))
{
    Console.WriteLine($"got {x}");
    if (x == 2) break;
}
Console.WriteLine("---");
foreach (var x in Guarded("B")) Console.WriteLine($"got {x}");
Console.WriteLine("---");
Console.WriteLine(FirstOver(Naturals(), 5));
Console.WriteLine("---");
foreach (var x in Nested())
{
    Console.WriteLine($"nested {x}");
    if (x == "inner-1") break;
}
Console.WriteLine("---");
Console.WriteLine(Find(new[] { "x", "y", "z" }, "y"));

static IEnumerable<int> Guarded(string name)
{
    Console.WriteLine($"{name}: begin");
    try
    {
        for (int i = 1; i <= 3; i++) yield return i;
    }
    finally
    {
        Console.WriteLine($"{name}: finally");
    }
    Console.WriteLine($"{name}: end");
}

static IEnumerable<int> Naturals()
{
    try
    {
        int i = 0;
        while (true) yield return i++;
    }
    finally { Console.WriteLine("naturals disposed"); }
}

static int FirstOver(IEnumerable<int> source, int limit)
{
    foreach (var n in source)
        if (n > limit) return n;
    return -1;
}

static IEnumerable<string> Nested()
{
    try
    {
        yield return "outer";
        try
        {
            yield return "inner-1";
            yield return "inner-2";
        }
        finally { Console.WriteLine("inner finally"); }
    }
    finally { Console.WriteLine("outer finally"); }
}

static string Find(string[] items, string wanted)
{
    foreach (var item in Each(items))
        if (item == wanted) return "found " + item;
    return "missing";
}

static IEnumerable<string> Each(string[] items)
{
    try { foreach (var i in items) yield return i; }
    finally { Console.WriteLine("each disposed"); }
}
