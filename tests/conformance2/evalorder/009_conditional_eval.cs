static bool T(string s) { Console.Write(s); return true; }
static bool F(string s) { Console.Write(s); return false; }
Console.WriteLine(F("a") && T("b") || T("c") && F("d") || T("e"));
Console.WriteLine(F("x") & T("y") | T("z"));
