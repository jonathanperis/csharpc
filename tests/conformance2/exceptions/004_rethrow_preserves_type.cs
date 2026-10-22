try {
    try { throw new KeyNotFoundException("missing"); }
    catch (Exception) { Console.WriteLine("logging"); throw; }
} catch (KeyNotFoundException e) { Console.WriteLine("got " + e.GetType().Name + " " + e.Message); }
try {
    try { throw new FormatException("f"); }
    catch (FormatException e) { throw new InvalidOperationException("wrapped", e); }
} catch (InvalidOperationException e) { Console.WriteLine(e.Message + " <- " + e.InnerException!.Message); }
