static void T(string s) { Console.WriteLine($"{s}: {int.TryParse(s, out var v)} {v}"); }
T(" 42 "); T("+7"); T("-0"); T("2147483648"); T("1_000"); T(""); T("0x10");
Console.WriteLine(double.Parse("1e3") + " " + double.Parse("-.5") + " " + long.Parse("-9223372036854775808"));
Console.WriteLine(double.TryParse("abc", out var d) + " " + d);
