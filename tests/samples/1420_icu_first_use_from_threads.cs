using System;
using System.Globalization;
using System.Threading;

// Many threads make the program's first culture-aware call at the same moment: ICU must be loaded once.
const int Threads = 16;
var results = new string[Threads];
using var start = new ManualResetEventSlim(false);
var workers = new Thread[Threads];
for (int i = 0; i < Threads; i++)
{
    int id = i;
    workers[i] = new Thread(() =>
    {
        start.Wait();
        var culture = CultureInfo.GetCultureInfo("en-US");
        int cmp = culture.CompareInfo.Compare("apple", "Banana", CompareOptions.IgnoreCase);
        string upper = culture.TextInfo.ToUpper("straße " + id);
        int index = culture.CompareInfo.IndexOf("Hello World", "WORLD", CompareOptions.IgnoreCase);
        results[id] = $"{Math.Sign(cmp)} {upper} {index}";
    });
    workers[i].Start();
}
start.Set();
foreach (var t in workers) t.Join();
for (int i = 0; i < Threads; i++) Console.WriteLine(results[i]);
