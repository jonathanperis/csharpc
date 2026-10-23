// ConfigureAwait / WithCancellation(non-cancelable token) on async iterators compiled from source.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
    static async IAsyncEnumerable<int> Numbers(int n, [EnumeratorCancellation] CancellationToken token = default)
    {
        for (var i = 1; i <= n; i++)
        {
            await Task.Yield();
            yield return i * i;
        }
    }

    static async Task Main()
    {
        var sum = 0;
        await foreach (var x in Numbers(4).ConfigureAwait(false)) sum += x;
        Console.WriteLine(sum);

        await foreach (var x in Numbers(3).WithCancellation(CancellationToken.None).ConfigureAwait(false)) Console.Write(x + " ");
        Console.WriteLine();

        await using var e = Numbers(2).ConfigureAwait(false).GetAsyncEnumerator();
        while (await e.MoveNextAsync()) Console.WriteLine(e.Current);
    }
}
