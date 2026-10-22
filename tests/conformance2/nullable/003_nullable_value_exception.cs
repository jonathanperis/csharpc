int? n = null;
try { Console.WriteLine(n.Value); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { int x = (int)n; } catch (InvalidOperationException e) { Console.WriteLine(e.GetType().Name); }
