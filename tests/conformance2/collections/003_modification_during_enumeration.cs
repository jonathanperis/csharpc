var list = new List<int> { 1, 2, 3 };
try { foreach (var x in list) if (x == 2) list.Add(4); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
var d = new Dictionary<int, int> { [1] = 1, [2] = 2 };
try { foreach (var kv in d) d[3] = 3; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
var hs = new HashSet<int> { 1 };
try { foreach (var v in hs) hs.Remove(v); Console.WriteLine("remove ok? " + hs.Count); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
