static int Depth(int n) => n == 0 ? 0 : 1 + Depth(n - 1);
Console.WriteLine(Depth(10000));
