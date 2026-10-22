int local = 0;
Console.WriteLine($"{nameof(local)} {nameof(Console.WriteLine)} {nameof(List<int>)} {nameof(P.Name)}");
Console.WriteLine($"{typeof(int)} {typeof(List<string>)} {typeof(int[])} {typeof(P).Name}");
Console.WriteLine($"{sizeof(int)} {sizeof(double)} {sizeof(bool)} {sizeof(char)} {sizeof(decimal)}");
class P { public string Name = ""; }
