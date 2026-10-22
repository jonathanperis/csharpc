Console.WriteLine("aaa".Replace("a", "bb"));
Console.WriteLine("abc".Replace("b", ""));
Console.WriteLine("abcabc".Replace("bc", "X"));
try { "x".Replace("", "y"); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
Console.WriteLine("abc".Insert(3, "!") + " " + "abcdef".Remove(2, 3) + " " + "abc".Remove(1));
