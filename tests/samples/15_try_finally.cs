// try/finally (no exceptions involved): finally runs on every exit path, in order.
Console.WriteLine(Find(new[] { 4, 8, 15, 16, 23, 42 }, 15));
Console.WriteLine(Find(new[] { 1, 2, 3 }, 99));
Nested();
Console.WriteLine(Loop());

static int Find(int[] items, int wanted)
{
    try
    {
        for (int i = 0; i < items.Length; i++)
            if (items[i] == wanted) return i;
        return -1;
    }
    finally
    {
        Console.WriteLine($"searched for {wanted}");
    }
}

static void Nested()
{
    try
    {
        try { Console.WriteLine("inner try"); return; }
        finally { Console.WriteLine("inner finally"); }
    }
    finally { Console.WriteLine("outer finally"); }
}

static int Loop()
{
    int total = 0;
    for (int i = 0; i < 5; i++)
    {
        try
        {
            if (i == 1) continue;
            if (i == 4) break;
            total += i;
        }
        finally { total += 100; }
    }
    return total;
}
