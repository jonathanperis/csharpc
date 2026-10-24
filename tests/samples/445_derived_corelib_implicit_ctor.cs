// Classes deriving from library classes the corelib implements, with implicit constructors: the
// base class's constructor runs (it used to be skipped, leaving the base's fields unset).
using System;
using System.Collections.Generic;

var names = new NameList();
names.Add("b");
names.Add("a");
names.Sort();
Console.WriteLine($"{names.Count} {string.Join(",", names)} {names.Capacity >= 2}");

var counts = new Counts();
counts["x"] = 1;
counts["y"] = 2;
Console.WriteLine($"{counts.Count} {counts["y"]} {counts.ContainsKey("z")}");

var queue = new Jobs();
queue.Enqueue(3);
queue.Enqueue(4);
Console.WriteLine($"{queue.Dequeue()} {queue.Count}");

var set = new Tags();
Console.WriteLine($"{set.Add("t")} {set.Add("t")} {set.Count}");

var byLength = new ByLength();
var words = new List<string> { "ccc", "a", "bb" };
words.Sort(byLength);
Console.WriteLine(string.Join(",", words));

sealed class NameList : List<string> { }
sealed class Counts : Dictionary<string, int> { }
sealed class Jobs : Queue<int> { }
sealed class Tags : HashSet<string> { }
sealed class ByLength : Comparer<string>
{
    public override int Compare(string? x, string? y) => (x?.Length ?? 0).CompareTo(y?.Length ?? 0);
}
