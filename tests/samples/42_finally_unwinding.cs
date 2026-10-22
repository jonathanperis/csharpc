// finally blocks run while an exception unwinds through them, innermost first, across frames.
try { A(); }
catch (InvalidOperationException e) { Console.WriteLine("caught in Main: " + e.Message); }
finally { Console.WriteLine("Main finally"); }

Console.WriteLine(Counter.Value);
try { Loop(); } catch (Exception e) { Console.WriteLine(e.Message + " after " + Counter.Value); }

static void A()
{
    try { B(); }
    finally { Console.WriteLine("A finally"); Counter.Value += 1; }
}

static void B()
{
    try
    {
        try { C(); }
        finally { Console.WriteLine("B inner finally"); Counter.Value += 10; }
    }
    finally { Console.WriteLine("B outer finally"); Counter.Value += 100; }
}

static void C()
{
    Console.WriteLine("C throws");
    throw new InvalidOperationException("from C");
}

static void Loop()
{
    for (int i = 0; ; i++)
    {
        try
        {
            if (i == 5) throw new Exception("stopped at " + i);
        }
        finally { Counter.Value++; }
    }
}

static class Counter { public static int Value; }
