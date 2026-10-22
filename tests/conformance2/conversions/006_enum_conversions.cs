var e = (E)5;
Console.WriteLine(e);
Console.WriteLine((int)E.B + 1);
E x = E.A;
x++;
Console.WriteLine(x);
x = (E)((int)x << 1);
Console.WriteLine(x);
int three = 300;
Console.WriteLine((byte)(E)three);
Console.WriteLine(E.C - E.A);
enum E { A = 1, B = 2, C = 4 }
