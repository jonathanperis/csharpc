var e = E.Max;
e++;
Console.WriteLine((int)e + " " + e);
E f = (E)255;
f += 1;
Console.WriteLine((byte)f + " " + f);
enum E : byte { Zero, Max = 254 }
