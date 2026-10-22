// Corelib collections throw real, catchable .NET exceptions with .NET messages.
var d = new Dictionary<string, int> { ["a"] = 1 };
try { Console.WriteLine(d["zz"]); }
catch (KeyNotFoundException e) { Console.WriteLine("KNF: " + e.Message); }
try { d.Add("a", 2); }
catch (ArgumentException e) { Console.WriteLine("Arg: " + e.Message); }
var list = new List<int> { 1, 2, 3 };
try { Console.WriteLine(list[5]); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine("AOOR: " + e.Message); }
try { foreach (var x in list) if (x == 2) list.Add(9); }
catch (InvalidOperationException e) { Console.WriteLine("IOE: " + e.Message); }
try { new List<int>().First(); }
catch (InvalidOperationException e) { Console.WriteLine("First: " + e.Message); }
try { new Stack<int>().Pop(); }
catch (InvalidOperationException e) { Console.WriteLine("Pop: " + e.Message); }
try { checked { Console.WriteLine(new[] { int.MaxValue, 1 }.ToList().Sum()); } }
catch (OverflowException e) { Console.WriteLine("Sum: " + e.Message); }
try { new[] { 1, 2 }.Single(); }
catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
