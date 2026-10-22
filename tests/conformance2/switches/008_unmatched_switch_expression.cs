static string M(int x) => x switch { 1 => "one", 2 => "two" };
Console.WriteLine(M(1));
try { M(3); } catch (Exception e) { Console.WriteLine(e.GetType().Name); }
