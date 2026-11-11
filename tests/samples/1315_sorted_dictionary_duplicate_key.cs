using System;
using System.Collections.Generic;

// SortedDictionary adds to a tree set of KeyValuePairs, so .NET's duplicate-key message formats the pair (#277).
var sorted = new SortedDictionary<string, int> { ["a"] = 1 };
try { sorted.Add("a", 2); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

var byNumber = new SortedDictionary<int, string> { [3] = "three" };
try { byNumber.Add(3, "again"); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

// The indexer replaces instead of throwing; SortedList and Dictionary format only the key.
sorted["a"] = 5;
Console.WriteLine(sorted["a"]);
var list = new SortedList<string, int> { ["b"] = 1 };
try { list.Add("b", 2); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
var dictionary = new Dictionary<string, int> { ["c"] = 1 };
try { dictionary.Add("c", 2); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
