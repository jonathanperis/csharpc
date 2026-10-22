// Argument validation of System.Array helpers and collection capacities: exception types,
// messages and ParamName must match .NET (including exceptions thrown by the C runtime).
using System;
using System.Collections.Generic;

int[] x = { 3, 1, 2 };
int[] nul = null!;
string[] words = { "b", "a", "c" };

T("Array.Clear past end", () => Array.Clear(x, 1, 5));
T("Array.Clear negative index", () => Array.Clear(x, -1, 1));
T("Array.Clear null", () => Array.Clear(nul, 0, 1));
T("Array.Reverse past end", () => Array.Reverse(x, 1, 5));
T("Array.Reverse negative index", () => Array.Reverse(x, -1, 1));
T("Array.Reverse negative length", () => Array.Reverse(x, 0, -1));
T("Array.Reverse null", () => Array.Reverse(nul));
T("Array.Reverse(string[]) past end", () => Array.Reverse(words, 2, 2));
T("Array.Fill count", () => Array.Fill(x, 0, 2, 5));
T("Array.Fill startIndex", () => Array.Fill(x, 0, 4, 0));
T("Array.IndexOf startIndex", () => Array.IndexOf(x, 1, 5));
T("Array.IndexOf count", () => Array.IndexOf(x, 1, 1, 5));
T("Array.IndexOf null", () => Array.IndexOf(nul, 1));
T("Array.Sort range", () => Array.Sort(words, 1, 5));
T("Array.Sort negative", () => Array.Sort(words, -1, -1));
T("Array.BinarySearch range", () => Array.BinarySearch(x, 1, 5, 2));
T("Array.BinarySearch negative index", () => Array.BinarySearch(x, -1, 1, 2));
T("Array.BinarySearch negative both", () => Array.BinarySearch(x, -1, -1, 2));
T("Array.Sort negative length", () => Array.Sort(words, 0, -1));
T("Array.Find null match", () => Array.Find(x, null!));
T("Array.ForEach null array", () => Array.ForEach(nul, _ => { }));
T("Array.ConvertAll null", () => Array.ConvertAll<int, string>(x, null!));
T("Array.TrueForAll null array", () => Array.TrueForAll(nul, _ => true));
T("Array.Resize negative", () => Array.Resize(ref x, -1));
T("valid calls", () =>
{
    int[] y = { 5, 4, 3, 2, 1 };
    Array.Reverse(y, 1, 3);
    Array.Fill(y, 9, 4, 1);
    Console.WriteLine($"  {string.Join(",", y)} {Array.IndexOf(y, 3, 1, 3)} {Array.IndexOf(y, 7, 5)}");
});
T("HashSet capacity", () => new HashSet<int>(-1, null));
T("HashSet.EnsureCapacity", () => new HashSet<int>().EnsureCapacity(-1));
T("Dictionary capacity", () => new Dictionary<int, int>(-1));
T("Dictionary.EnsureCapacity", () => new Dictionary<int, int>().EnsureCapacity(-1));
T("Queue capacity", () => new Queue<int>(-1));
T("Queue.EnsureCapacity", () => new Queue<int>().EnsureCapacity(-1));
T("Stack capacity", () => new Stack<int>(-1));
T("Stack.EnsureCapacity", () => new Stack<int>().EnsureCapacity(-1));
T("PriorityQueue capacity", () => new PriorityQueue<int, int>(-1));
T("PriorityQueue.EnsureCapacity", () => new PriorityQueue<int, int>().EnsureCapacity(-1));
T("List capacity", () => new List<int>(-1));
T("List.RemoveRange", () => new List<int> { 1 }.RemoveRange(0, 5));

static void T(string label, Action action)
{
    try
    {
        action();
        Console.WriteLine($"{label}: no exception");
    }
    catch (Exception e)
    {
        Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message} [{(e as ArgumentException)?.ParamName ?? "-"}]");
    }
}
