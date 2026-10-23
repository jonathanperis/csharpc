using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

var list = ImmutableList.Create(3, 1, 2);
var list2 = list.Add(5).Remove(1).Sort();
Console.WriteLine(string.Join(",", list2));
var arr = ImmutableArray.Create("b", "a", "c");
Console.WriteLine(string.Join(",", arr.Sort()));
var b = ImmutableArray.CreateBuilder<int>();
for (var i = 0; i < 10; i++) b.Add(i * i);
var built = b.ToImmutable();
Console.WriteLine(built.Length + " " + built[9]);
var dict = ImmutableDictionary<string, int>.Empty.Add("x", 1).Add("y", 2).SetItem("x", 10);
foreach (var kv in dict.OrderBy(k => k.Key)) Console.WriteLine($"{kv.Key}={kv.Value}");
var stack = ImmutableStack<int>.Empty.Push(1).Push(2);
Console.WriteLine(stack.Peek());
var set = ImmutableHashSet.Create(1, 2, 3).Union(new[] { 3, 4 });
Console.WriteLine(set.Count);
var sorted = ImmutableSortedDictionary<int, string>.Empty.Add(3, "c").Add(1, "a");
Console.WriteLine(string.Join(",", sorted.Keys));
var queue = ImmutableQueue<string>.Empty.Enqueue("q1").Enqueue("q2");
Console.WriteLine(queue.Peek());
try { list.RemoveAt(10); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { _ = ImmutableStack<int>.Empty.Peek(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { dict.Add("y", 3); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
