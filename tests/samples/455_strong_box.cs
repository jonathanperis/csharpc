// StrongBox<T>: a shared mutable cell, by field and through IStrongBox.
using System.Runtime.CompilerServices;

var counter = new StrongBox<int>(5);
Increment(counter);
Increment(counter);
Console.WriteLine(counter.Value);
counter.Value += 10;
ref int cell = ref counter.Value;
cell *= 2;
Console.WriteLine(counter.Value);

var empty = new StrongBox<string?>();
Console.WriteLine(empty.Value is null);
IStrongBox untyped = new StrongBox<string>("boxed");
Console.WriteLine(untyped.Value);
untyped.Value = "changed";
Console.WriteLine(((StrongBox<string>)untyped).Value);
try { untyped.Value = 42; }
catch (InvalidCastException) { Console.WriteLine("InvalidCastException"); }

var nullable = new StrongBox<byte?>(null);
Console.WriteLine(nullable.Value.HasValue);
nullable.Value = 3;
Console.WriteLine(nullable.Value);
Console.WriteLine(counter.GetType().Name);
Console.WriteLine(counter is IStrongBox);

var shared = new StrongBox<List<string>>(new List<string>());
Action<string> add = s => shared.Value.Add(s);
add("a");
add("b");
Console.WriteLine(string.Join(",", shared.Value));

static void Increment(StrongBox<int> box) => box.Value++;
