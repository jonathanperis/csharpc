int max = int.MaxValue;
Console.WriteLine(unchecked(max + 1));
try { Console.WriteLine(checked(max + 1)); }
catch (OverflowException e) { Console.WriteLine(e.GetType().Name); }
checked { byte b = 200; try { b += 100; } catch (OverflowException) { Console.WriteLine("byte overflow"); } }
