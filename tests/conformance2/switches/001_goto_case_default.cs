static string M(int x)
{
    var s = "";
    switch (x)
    {
        case 1: s += "one;"; goto case 3;
        case 2: s += "two;"; goto default;
        case 3: s += "three;"; break;
        default: s += "default;"; break;
    }
    return s;
}
for (int i = 0; i <= 3; i++) Console.WriteLine(M(i));
