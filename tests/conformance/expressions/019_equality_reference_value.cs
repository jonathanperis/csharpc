string a = "hi", b = "h" + "i".ToString();
Console.WriteLine(a == b);
Console.WriteLine((object)a == (object)b);
Console.WriteLine(object.Equals(a, b));
int x = 5; long y = 5;
Console.WriteLine(x == y);
