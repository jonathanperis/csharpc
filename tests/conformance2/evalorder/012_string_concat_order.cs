static string L(string s) { Console.Write("[" + s + "]"); return s; }
var r = L("a") + 1 + 2 + L("b") + (3 + 4);
Console.WriteLine(" " + r);
