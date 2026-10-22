// Exceptions thrown inside iterators propagate to the foreach; the iterator's finally blocks run.
try
{
    foreach (var x in Numbers(5)) Console.WriteLine(x);
}
catch (InvalidOperationException e) { Console.WriteLine("caught: " + e.Message); }

int sum = 0;
try { foreach (var x in Numbers(2)) sum += x; }
catch (Exception) { Console.WriteLine("unexpected"); }
Console.WriteLine($"sum {sum}");

var e2 = Faulty().GetEnumerator();
Console.WriteLine(e2.MoveNext() + " " + e2.Current);
Console.WriteLine(e2.MoveNext() + " " + e2.Current);
try { e2.MoveNext(); } catch (InvalidOperationException ex) { Console.WriteLine("MoveNext threw " + ex.Message); }
Console.WriteLine("after fault MoveNext: " + e2.MoveNext());

try
{
    await foreach (var v in Stream()) Console.WriteLine("async " + v);
}
catch (FormatException e) { Console.WriteLine("await foreach caught: " + e.Message); }

IEnumerable<int> Numbers(int failAt)
{
    try
    {
        for (int i = 1; ; i++)
        {
            if (i == failAt + 1 && failAt > 2) throw new InvalidOperationException($"failed at {i}");
            if (i > failAt) yield break;
            yield return i;
        }
    }
    finally
    {
        Console.WriteLine($"iterator({failAt}) finally");
    }
}

IEnumerable<int> Faulty()
{
    yield return 1;
    yield return 2;
    throw new InvalidOperationException("third");
}

async IAsyncEnumerable<int> Stream()
{
    yield return 1;
    await Task.Delay(1);
    yield return 2;
    throw new FormatException("bad stream");
}
