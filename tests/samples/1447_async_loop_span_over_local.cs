// A span over a callee's own parameter or local (string.Split(char)'s IL makes one over its separator), called after an await in a
// loop: inlined into the async method, the span must not point into a stack frame that the resumption no longer has.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

static class Program
{
    static int FirstPlusLength(int value)
    {
        ReadOnlySpan<int> span = new(ref value);
        return span[0] + span.Length;
    }

    static string Words(string line) => string.Join("|", line.Split(' '));

    static async Task<int> Loop(int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++)
        {
            await Task.Yield();
            var request = ("GET /item/" + i + " HTTP/1.1").Split(' ');
            total += request.Length * 100 + FirstPlusLength(i);
            Console.WriteLine(request.Length + " " + request[1] + " " + Words("a b c " + i) + " " + "x;y;z".Split(';').Length);
        }
        return total;
    }

    static async IAsyncEnumerable<string> Lines()
    {
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(1);
            yield return string.Join(",", ("k" + i + " v" + i).Split(' '));
        }
    }

    static IEnumerable<int> Counts()
    {
        for (int i = 0; i < 3; i++) yield return ("p q r s".Split(' ').Length + FirstPlusLength(i));
    }

    static async Task Main()
    {
        Console.WriteLine(await Loop(4));
        await foreach (var line in Lines()) Console.WriteLine(line);
        foreach (var c in Counts()) Console.WriteLine(c);
    }
}
