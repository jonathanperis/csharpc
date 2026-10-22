static int L(string s, int v) { Console.Write(s); return v; }
Console.WriteLine(L("a", 1) + L("b", 2) * L("c", 3));
Console.WriteLine(L("d", 10) - L("e", 3) - L("f", 2));
