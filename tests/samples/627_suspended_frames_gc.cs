// Suspended async methods keep what only their frames reference: the collector must scan every frame.
// (A coroutine frame used to start where its object's method-table pointer lives, so the collector read
// "flags" from the resume function's machine code and skipped frames whose bytes happened to say "no
// references"; their locals were freed and reused while the methods were still suspended.) Many distinct
// instantiations give many distinct resume functions.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

static class Program
{
    sealed class Payload
    {
        public readonly string Name;
        public readonly int[] Values;
        public Payload(string name, int n) { Name = name; Values = new int[n]; for (int i = 0; i < n; i++) Values[i] = i * 7 + n; }
        public int Sum() { int s = 0; foreach (var v in Values) s += v; return s; }
    }

    struct S1 { } struct S2 { } struct S3 { } struct S4 { } struct S5 { } struct S6 { } struct S7 { } struct S8 { }
    struct S9 { } struct S10 { } struct S11 { } struct S12 { } struct S13 { } struct S14 { } struct S15 { } struct S16 { }

    static async Task<string> Hold<T>(Task gate, int n)
    {
        var payload = new Payload(typeof(T).Name, n);
        var list = new List<string> { payload.Name, n.ToString() };
        await gate;
        return $"{string.Join("/", list)} {payload.Sum()}";
    }

    static async Task<string> Outer<T>(Task gate, int n)
    {
        var tag = new string('x', n % 5 + 1);
        var inner = await Hold<T>(gate, n);
        return tag + " " + inner;
    }

    static void Churn()
    {
        for (int round = 0; round < 3; round++)
        {
            GC.Collect();
            var junk = new List<object>();
            for (int i = 0; i < 20000; i++) junk.Add(new int[i % 16 + 1]);
            for (int i = 0; i < 20000; i++) junk.Add(new string('y', i % 32 + 1));
            GC.KeepAlive(junk);
        }
        GC.Collect();
    }

    static async Task Main()
    {
        var gate = new TaskCompletionSource();
        var tasks = new List<Task<string>>
        {
            Outer<S1>(gate.Task, 1), Outer<S2>(gate.Task, 2), Outer<S3>(gate.Task, 3), Outer<S4>(gate.Task, 4),
            Outer<S5>(gate.Task, 5), Outer<S6>(gate.Task, 6), Outer<S7>(gate.Task, 7), Outer<S8>(gate.Task, 8),
            Outer<S9>(gate.Task, 9), Outer<S10>(gate.Task, 10), Outer<S11>(gate.Task, 11), Outer<S12>(gate.Task, 12),
            Outer<S13>(gate.Task, 13), Outer<S14>(gate.Task, 14), Outer<S15>(gate.Task, 15), Outer<S16>(gate.Task, 16),
            Outer<int>(gate.Task, 17), Outer<long>(gate.Task, 18), Outer<string>(gate.Task, 19), Outer<double>(gate.Task, 20),
        };
        Churn();
        gate.SetResult();
        foreach (var line in await Task.WhenAll(tasks)) Console.WriteLine(line);
    }
}
