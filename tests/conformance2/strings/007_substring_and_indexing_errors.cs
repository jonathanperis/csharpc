static void T(Func<string> f) { try { Console.WriteLine(f()); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("AOORE " + e.ParamName); } }
T(() => "hello".Substring(2));
T(() => "hello".Substring(5));
T(() => "hello".Substring(6));
T(() => "hello".Substring(1, 10));
try { Console.WriteLine("abc"[3]); } catch (IndexOutOfRangeException) { Console.WriteLine("IORE"); }
