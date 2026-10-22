var p = new P();
Console.WriteLine(p.A() + p.B());
partial class P { public int A() => 1; }
partial class P { public int B() => 2; }
