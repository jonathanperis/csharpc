// An iterator method's saved arguments (kept so GetEnumerator() on a used enumerable starts over)
// live in a small heap object. csharpc stored them over that object's method-table word, so a
// collection while the iterator was alive read a bogus method table and crashed.
using System;
using System.Collections.Generic;

sealed class Source
{
    public readonly string Prefix;
    public Source(string prefix) { Prefix = prefix; }

    public IEnumerable<string> Items(int count, int scale)
    {
        for (var i = 0; i < count; i++)
        {
            Program.Churn(12);
            yield return Prefix + (i * scale);
        }
    }
}

static class Program
{
    static long s_sink;

    public static void Churn(int megabytes)
    {
        long bytes = (long)megabytes * 1024 * 1024;
        for (long done = 0; done < bytes; done += 128)
        {
            var junk = new int[24];
            junk[1] = (int)done;
            s_sink += junk[1] & 1;
        }
    }

    static IEnumerable<int> Numbers(int start, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Churn(12);
            yield return start + i;
        }
    }

    static void Main()
    {
        var numbers = Numbers(3, 6);
        long sum = 0;
        foreach (var n in numbers) sum += n;
        foreach (var n in numbers) sum += n * 10;  // second enumeration: rebuilt from the saved arguments
        Console.WriteLine(sum);

        var items = new Source("item-").Items(4, 7);
        Console.WriteLine(string.Join(",", items));
        Console.WriteLine(string.Join(",", items));
        Console.WriteLine(s_sink >= 0);
    }
}
