try { throw new InvalidOperationException("bad"); }
catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }
try { int[] a = new int[1]; a[2] = 0; }
catch (IndexOutOfRangeException e) { Console.WriteLine(e.Message); }
