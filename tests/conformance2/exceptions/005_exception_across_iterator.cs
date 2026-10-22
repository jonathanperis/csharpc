static IEnumerable<int> Gen() { try { yield return 1; throw new InvalidOperationException("in iterator"); } finally { Console.WriteLine("iter finally"); } }
try { foreach (var x in Gen()) Console.WriteLine(x); } catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }
