var c = new C();
c.P += 5; c.P *= 2; c.P++;
Console.WriteLine(c.P);
c.Arr[1] += 10;
Console.WriteLine(c.Arr[1]);
class C { public int P { get; set; } public int[] Arr = new int[3]; }
