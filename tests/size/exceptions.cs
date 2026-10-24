try { throw new InvalidOperationException("boom"); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
