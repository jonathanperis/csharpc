var o = new O();
O Get() { Console.WriteLine("get o"); return o; }
Get().P += 3;
Get().P *= 2;
Console.WriteLine(o.P);
class O { int p = 1; public int P { get { Console.WriteLine("get P"); return p; } set { Console.WriteLine("set P"); p = value; } } }
