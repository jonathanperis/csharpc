Func<int, int> twice = x => checked(x * 2);
try { Console.WriteLine(twice(int.MaxValue)); } catch (OverflowException) { Console.WriteLine("overflow"); }
int Local(int v) { checked { return v + 1; } }
try { Local(int.MaxValue); } catch (OverflowException e) { Console.WriteLine(e.Message); }
Func<int, int> unc = x => unchecked(x * 2);
Console.WriteLine(unc(int.MaxValue));
