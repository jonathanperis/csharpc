try { Console.WriteLine(string.Format("{1}", "a")); } catch (FormatException e) { Console.WriteLine(e.GetType().Name); }
try { Console.WriteLine(string.Format("{0", "a")); } catch (FormatException e) { Console.WriteLine(e.GetType().Name); }
Console.WriteLine(string.Format("{0}{{}}{1}", 1, 2));
